using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    internal IReadOnlyDictionary<string, int> ByteParameterCalls { get; init; } = new Dictionary<string, int>();
    internal IReadOnlyDictionary<string, string> ByteParameterFrameEntries { get; init; } = new Dictionary<string, string>();
    ILValueAnalysis? _byteCallValues;

    bool TryWriteByteCall(ILInstruction instruction, string method)
    {
        if (instruction.OpCode != ILOpCode.Call || !ByteParameterCalls.TryGetValue(method, out int context)
            || Instructions == null)
            return false;
        int count = _reflectionCache.GetNumberOfArguments(method);
        int logicalCount = count + (context >= 0 ? 1 : 0);
        if (count < 2 || Index < logicalCount || _numericValues == null
            || !_numericValues.Inputs[Index].SequenceEqual(Enumerable.Range(Index - logicalCount, logicalCount)))
            return false;
        int firstArgument = Index - logicalCount;
        var physicalArguments = Enumerable.Range(firstArgument, logicalCount)
            .Where(i => i - firstArgument != context).ToArray();
        // Loading the first argument may push an older outer-call operand.
        // That push belongs to the caller, not this replaceable argument span.
        if (_numericValues.Predecessors[firstArgument].Any(p => _numericValues.Outputs[p].Length != 0))
            return false;
        for (int i = firstArgument; i < Index; i++)
        {
            if (i > firstArgument && _numericValues.Predecessors[i].Any(p => p != i - 1))
                return false;
            var arg = Instructions[i];
            if (i - firstArgument == context)
            {
                if (arg.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s or ILOpCode.Ldarga or ILOpCode.Ldarga_s
                    || ClosureArgIndex >= 0 && NumericArgIndex(arg) == ClosureArgIndex)
                    continue;
                return false;
            }
            if (arg.GetLdcValue() is int constant && constant is >= 0 and <= 255)
                continue;
            if (NumericArgIndex(arg) != null && NumericType(i) == PrimitiveTypeCode.Byte)
                continue;
            if (arg.GetLdlocIndex() is not int index || !Locals.TryGetValue(index, out var value)
                || value.Address == null || value.IsWord || value.ArraySize != 0 || value.LabelName != null)
                return false;
        }
        int start = _blockCountAtILOffset[Instructions[firstArgument].Offset];
        int argumentAdjustment = _numericArgAdjust[Instructions[firstArgument].Offset];
        int ParameterBytesAfter(int argument)
        {
            int bytes = 0;
            for (int j = argument + 1; j < MethodParamCount; j++)
                bytes += j < ParamIsArray.Length && ParamIsArray[j] ? 2 : 1;
            return bytes;
        }
        bool batch = ByteParameterFrameEntries.TryGetValue(method, out string? frameEntry)
            && count <= byte.MaxValue
            && physicalArguments.All(i => NumericArgIndex(Instructions[i]) is not int argument
                || argumentAdjustment + count + ParameterBytesAfter(argument) <= byte.MaxValue);
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        if (batch)
        {
            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.sp);
            Emit(Opcode.SEC, AddressMode.Implied);
            Emit(Opcode.SBC, AddressMode.Immediate, (byte)count);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.sp);
            Emit(Opcode.BCS, AddressMode.Relative, 2);
            Emit(Opcode.DEC, AddressMode.ZeroPage, (byte)(NESConstants.sp + 1));
        }
        for (int physical = 0; physical < physicalArguments.Length; physical++)
        {
            int i = physicalArguments[physical];
            var arg = Instructions[i];
            bool lastDirectLoad = batch && physical == physicalArguments.Length - 1
                && NumericArgIndex(arg) == null;
            if (lastDirectLoad)
                Emit(Opcode.LDY, AddressMode.Immediate, 0);
            if (arg.GetLdcValue() is int constant)
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant);
            else if (NumericArgIndex(arg) is int argument)
            {
                int offset = argumentAdjustment + (batch ? count : physical) + ParameterBytesAfter(argument);
                Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)offset));
                Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)NESConstants.sp);
            }
            else
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)Locals[arg.GetLdlocIndex()!.Value].Address!.Value);
            if (batch)
            {
                if (!lastDirectLoad)
                    Emit(Opcode.LDY, AddressMode.Immediate, (byte)(count - physical - 1));
                Emit(Opcode.STA, AddressMode.IndirectIndexed, (byte)NESConstants.sp);
                if (!lastDirectLoad && physical == physicalArguments.Length - 1)
                    Emit(Opcode.ORA, AddressMode.Immediate, 0);
            }
            else if (physical != physicalArguments.Length - 1)
            {
                EmitJSR("pusha");
                UsedMethods?.Add("pusha");
            }
        }
        EmitJSR(batch ? frameEntry! : method);
        _argStackAdjust = argumentAdjustment;
        for (int i = 0; i < count; i++)
            Stack.Pop();
        _ushortInAX = false;
        _runtimeValueInA = _reflectionCache.HasReturnValue(method);
        if (_runtimeValueInA)
        {
            _padPollResultAvailable = false;
            Stack.Push(0);
        }
        _lastLoadedLocalIndex = null;
        if (context >= 0)
            _pendingClosureAccess = false;
        _lastStaticFieldAddress = null;
        _savedRuntimeToTemp = false;
        _savedConstantViaPusha = false;
        _immediateInA = null;
        _pokeLastValue = null;
        previous = instruction.OpCode;
        return true;
    }
}
