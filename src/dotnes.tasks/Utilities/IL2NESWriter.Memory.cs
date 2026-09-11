using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    readonly Dictionary<int, int> _memoryAddressCalls = new();
    readonly Dictionary<int, int> _savedMemoryAddresses = new();
    readonly Stack<(int Producer, int Call)> _memorySaveOrder = new();
    readonly HashSet<int> _runtimeMemoryCalls = new();
    readonly HashSet<int> _memoryBranchTargets = new();
    bool _memoryCallsPrepared;

    void EmitConstantPeek(int address)
    {
        // A previous branch/return can leave a tracked runtime value in A, causing
        // WriteLdc to defer the address. Remove only code from this argument, never
        // a preceding return jump or the computation of a live caller value.
        RemoveOperandInstructions(Index - 1, address > byte.MaxValue ? 2 : 1);
        Emit(Opcode.LDA, AddressMode.Absolute, (ushort)address);
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        _immediateInA = null;
        _pokeLastValue = null;
    }

    void PrepareMemoryOperands()
    {
        if (Instructions is null)
            return;
        if (!_memoryCallsPrepared)
        {
            _memoryCallsPrepared = true;
            if (!Instructions.Any(i => i.OpCode == ILOpCode.Call
                && i.String is nameof(NESLib.peek) or nameof(NESLib.poke)))
                return;
            var analysis = new ILValueAnalysis(Instructions, _reflectionCache);
            _memoryBranchTargets.UnionWith(Instructions.SelectMany(ILValueAnalysis.GetBranchTargets));
            for (int call = 0; call < Instructions.Length; call++)
            {
                var instruction = Instructions[call];
                if (instruction.OpCode != ILOpCode.Call
                    || instruction.String is not (nameof(NESLib.peek) or nameof(NESLib.poke)))
                    continue;

                int[] inputs = analysis.Inputs[call];
                if (inputs.Length == 0 || inputs.Any(i => i < 0))
                    throw new TranspileException(
                        $"Unable to identify the evaluation-stack operands of {instruction.String} at IL_{instruction.Offset:X4} ({string.Join(", ", inputs)}).",
                        MethodName);
                int address = inputs[0];
                int value = inputs.Length == 2 ? inputs[1] : call;
                bool constantAddress = address == value - 1
                    && Instructions[address].GetLdcValue() != null;
                bool simpleValue = value == call - 1
                    && (Instructions[value].GetLdcValue() != null
                        || Instructions[value].GetLdlocIndex() != null
                        || Instructions[value].OpCode == ILOpCode.Ldsfld);
                if (constantAddress && (value == call || simpleValue))
                    continue;

                _runtimeMemoryCalls.Add(call);
                if (value != call)
                {
                    if (analysis.Escapes[address] || analysis.Consumers[address].Count != 1
                        || Instructions.Skip(address + 1).Take(call - address - 1)
                            .Any(i => ILValueAnalysis.IsBranch(i.OpCode) || i.OpCode == ILOpCode.Ret)
                        || Instructions.Skip(address + 1).Take(call - address)
                            .Any(i => _memoryBranchTargets.Contains(i.Offset)))
                        throw new TranspileException("A dynamic poke address must have one consumer without intervening control flow.", MethodName);
                    _memoryAddressCalls.Add(address, call);
                }
            }
        }

        int producer = Index - 1;
        if (_memoryAddressCalls.TryGetValue(producer, out int memoryCall))
        {
            // The address must survive arbitrary calls while the value is evaluated.
            // The hardware stack does not change cc65 parameter offsets and nests
            // naturally with JSR/RTS and other memory intrinsics.
            NormalizeMemoryAddress(producer);
            Emit(Opcode.PHA, AddressMode.Implied);
            Emit(Opcode.TXA, AddressMode.Implied);
            Emit(Opcode.PHA, AddressMode.Implied);
            _savedMemoryAddresses.Add(producer, memoryCall);
            _memorySaveOrder.Push((producer, memoryCall));
            _accState = AccumulatorState.Empty;
            _ntadrRuntimeResult = false;
            _lastLoadedLocalIndex = null;
            _lastStaticFieldAddress = null;
            _savedRuntimeToTemp = false;
            _savedConstantViaPusha = false;
        }
    }

    bool IsSavedMemoryAddress(int producer, int consumer) =>
        _savedMemoryAddresses.TryGetValue(producer, out int memoryCall)
        && producer < consumer && consumer < memoryCall;

    void NormalizeMemoryAddress(int producer)
    {
        // NTADR's dedicated handler emits A:X but intentionally bypasses normal
        // return tracking. Its declared word result must not be zero-extended.
        bool wordResult = Instructions is not null
            && Instructions[producer].OpCode == ILOpCode.Call
            && Instructions[producer].String is string method
            && _reflectionCache.TryReturns16Bit(method);
        if (!_ushortInAX && !wordResult)
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)0);
    }

    void EmitRuntimePeek()
    {
        Stack.Pop();
        NormalizeMemoryAddress(Index - 1);
        Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
        Emit(Opcode.STX, AddressMode.ZeroPage, (byte)(ptr1 + 1));
        Emit(Opcode.LDY, AddressMode.Immediate, (byte)0);
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, ptr1);
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        _immediateInA = null;
        _pokeLastValue = null;
    }

    void EmitRuntimePoke()
    {
        if (_memorySaveOrder.Count == 0 || _memorySaveOrder.Peek().Call != Index)
            throw new TranspileException("The dynamic poke address does not match the most recently saved operand.", MethodName);
        var saved = _memorySaveOrder.Pop();
        _savedMemoryAddresses.Remove(saved.Producer);
        Stack.Pop();
        Stack.Pop();
        Emit(Opcode.TAX, AddressMode.Implied);
        Emit(Opcode.PLA, AddressMode.Implied);
        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(ptr1 + 1));
        Emit(Opcode.PLA, AddressMode.Implied);
        Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
        Emit(Opcode.TXA, AddressMode.Implied);
        Emit(Opcode.LDY, AddressMode.Immediate, (byte)0);
        Emit(Opcode.STA, AddressMode.IndirectIndexed, ptr1);
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        _immediateInA = null;
        _pokeLastValue = null;
    }
}
