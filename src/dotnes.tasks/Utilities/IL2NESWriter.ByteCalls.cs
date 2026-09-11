using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    internal ISet<string> ByteParameterCalls { get; init; } = new HashSet<string>();
    internal bool[] ParamIsByte { get; init; } = [];
    ILValueAnalysis? _byteCallValues;

    bool TryWriteByteCall(ILInstruction instruction, string method)
    {
        if (instruction.OpCode != ILOpCode.Call || !ByteParameterCalls.Contains(method) || Instructions == null)
            return false;
        int count = _reflectionCache.GetNumberOfArguments(method);
        if (count < 2 || Index < count)
            return false;
        _byteCallValues ??= new ILValueAnalysis(Instructions, _reflectionCache);
        int first = Index - count;
        if (!_byteCallValues.Inputs[Index].SequenceEqual(Enumerable.Range(first, count)) ||
            _byteCallValues.Predecessors[first].Any(p => _byteCallValues.Outputs[p].Length > 0))
            return false;
        for (int i = Index - count; i < Index; i++)
        {
            if (i > first && _byteCallValues.Predecessors[i].Any(p => p != i - 1))
                return false;
            var arg = Instructions[i];
            if (arg.GetLdcValue() is int constant && constant is >= 0 and <= 255)
                continue;
            if (arg.GetLdargIndex() is int parameter && parameter < ParamIsByte.Length && ParamIsByte[parameter])
                continue;
            if (arg.GetLdlocIndex() is not int index || !Locals.TryGetValue(index, out var value)
                || value.Address == null || value.IsWord || value.ArraySize != 0 || value.LabelName != null)
                return false;
        }
        int start = _blockCountAtILOffset[Instructions[Index - count].Offset];
        int argumentAdjustment = _arrayArgumentAdjustments[Instructions[Index - count].Offset];
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        for (int i = Index - count; i < Index; i++)
        {
            var arg = Instructions[i];
            if (arg.GetLdcValue() is int constant)
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant);
            else if (arg.GetLdargIndex() is int argument)
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
            Stack.Push(0);
            _padPollResultAvailable = false;
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
