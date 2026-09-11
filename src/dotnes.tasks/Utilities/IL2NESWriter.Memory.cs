using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    readonly HashSet<int> _memoryAddressEnds = new();
    readonly HashSet<int> _runtimeMemoryCalls = new();
    bool _memoryCallsPrepared;

    void EmitConstantPeek(int address)
    {
        // A previous branch/return can leave a tracked runtime value in A, causing
        // WriteLdc to defer the address. Remove only code from this argument, never
        // a preceding return jump or the computation of a live caller value.
        int count = address > byte.MaxValue ? 2 : 1;
        if (Instructions is not null
            && _blockCountAtILOffset.TryGetValue(Instructions[Index - 1].Offset, out int start))
            count = Math.Min(count, GetBufferedBlockCount() - start);
        if (count > 0)
            RemoveLastInstructions(count);
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
                    _memoryAddressEnds.Add(address + 1);
            }
        }

        if (_memoryAddressEnds.Contains(Index))
        {
            // The address must survive arbitrary calls while the value is evaluated.
            // The hardware stack does not change cc65 parameter offsets and nests
            // naturally with JSR/RTS and other memory intrinsics.
            if (!_ushortInAX)
                Emit(Opcode.LDX, AddressMode.Immediate, (byte)0);
            Emit(Opcode.PHA, AddressMode.Implied);
            Emit(Opcode.TXA, AddressMode.Implied);
            Emit(Opcode.PHA, AddressMode.Implied);
            _accState = AccumulatorState.Empty;
            _lastLoadedLocalIndex = null;
            _lastStaticFieldAddress = null;
            _savedRuntimeToTemp = false;
            _savedConstantViaPusha = false;
        }
    }

    void EmitRuntimePeek()
    {
        Stack.Pop();
        if (!_ushortInAX)
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)0);
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
