using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    internal ISet<string> ByteParameterCalls { get; init; } = new HashSet<string>();

    bool TryWriteByteCall(ILInstruction instruction, string method)
    {
        if (instruction.OpCode != ILOpCode.Call || !ByteParameterCalls.Contains(method) || Instructions == null)
            return false;
        int count = _reflectionCache.GetNumberOfArguments(method);
        if (count < 2 || Index < count || _numericValues == null
            || !_numericValues.Inputs[Index].SequenceEqual(Enumerable.Range(Index - count, count)))
            return false;
        int firstArgument = Index - count;
        // Loading the first argument may push an older outer-call operand.
        // That push belongs to the caller, not this replaceable argument span.
        if (firstArgument > 0 && _numericValues.Outputs[firstArgument - 1].Length != 0)
            return false;
        for (int i = Index - count; i < Index; i++)
        {
            if (i > Index - count && _numericValues.Predecessors[i].Any(p => p != i - 1))
                return false;
            var arg = Instructions[i];
            if (arg.GetLdcValue() is int constant && constant is >= 0 and <= 255)
                continue;
            if (NumericArgIndex(arg) != null && NumericType(i) == PrimitiveTypeCode.Byte)
                continue;
            if (arg.GetLdlocIndex() is not int index || !Locals.TryGetValue(index, out var value)
                || value.Address == null || value.IsWord || value.ArraySize != 0 || value.LabelName != null)
                return false;
        }
        int start = _blockCountAtILOffset[Instructions[Index - count].Offset];
        int argumentAdjustment = _numericArgAdjust[Instructions[Index - count].Offset];
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        for (int i = Index - count; i < Index; i++)
        {
            var arg = Instructions[i];
            if (arg.GetLdcValue() is int constant)
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant);
            else if (NumericArgIndex(arg) is int argument)
            {
                int offset = argumentAdjustment + i - (Index - count);
                for (int j = argument + 1; j < MethodParamCount; j++)
                    offset += j < ParamIsArray.Length && ParamIsArray[j] ? 2 : 1;
                Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)offset));
                Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)NESConstants.sp);
            }
            else
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)Locals[arg.GetLdlocIndex()!.Value].Address!.Value);
            if (i != Index - 1)
                EmitJSR("pusha");
        }
        EmitJSR(method);
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
        _lastStaticFieldAddress = null;
        _savedRuntimeToTemp = false;
        _savedConstantViaPusha = false;
        _immediateInA = null;
        _pokeLastValue = null;
        previous = instruction.OpCode;
        return true;
    }
}
