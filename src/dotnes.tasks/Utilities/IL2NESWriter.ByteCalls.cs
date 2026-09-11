using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    internal IReadOnlyDictionary<string, int> ByteParameterCalls { get; init; } = new Dictionary<string, int>();
    internal bool[] ParamIsByte { get; init; } = [];
    ILValueAnalysis? _byteCallValues;

    bool TryWriteByteCall(ILInstruction instruction, string method)
    {
        if (instruction.OpCode != ILOpCode.Call || !ByteParameterCalls.TryGetValue(method, out int context) || Instructions == null)
            return false;
        int count = _reflectionCache.GetNumberOfArguments(method);
        int logicalCount = count + (context >= 0 ? 1 : 0);
        if (count < 2 || Index < logicalCount)
            return false;
        _byteCallValues ??= new ILValueAnalysis(Instructions, _reflectionCache);
        int first = Index - logicalCount;
        if (!_byteCallValues.Inputs[Index].SequenceEqual(Enumerable.Range(first, logicalCount)) ||
            _byteCallValues.Predecessors[first].Any(p => _byteCallValues.Outputs[p].Length > 0))
            return false;
        var physicalArguments = Enumerable.Range(first, logicalCount).Where(i => i - first != context).ToArray();
        for (int i = first; i < Index; i++)
        {
            if (i > first && _byteCallValues.Predecessors[i].Any(p => p != i - 1))
                return false;
            var arg = Instructions[i];
            if (i - first == context)
            {
                if (arg.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s or ILOpCode.Ldarga or ILOpCode.Ldarga_s ||
                    ClosureArgIndex >= 0 && arg.GetLdargIndex() == ClosureArgIndex)
                    continue;
                return false;
            }
            if (arg.GetLdcValue() is int constant && constant is >= 0 and <= 255)
                continue;
            if (arg.GetLdargIndex() is int parameter && parameter < ParamIsByte.Length && ParamIsByte[parameter])
                continue;
            if (arg.GetLdlocIndex() is not int index || !Locals.TryGetValue(index, out var value)
                || value.Address == null || value.IsWord || value.ArraySize != 0 || value.LabelName != null)
                return false;
        }
        int start = _blockCountAtILOffset[Instructions[first].Offset];
        int argumentAdjustment = _arrayArgumentAdjustments[Instructions[first].Offset];
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        for (int physical = 0; physical < physicalArguments.Length; physical++)
        {
            int i = physicalArguments[physical];
            var arg = Instructions[i];
            if (arg.GetLdcValue() is int constant)
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant);
            else if (arg.GetLdargIndex() is int argument)
            {
                int offset = argumentAdjustment + physical;
                for (int j = argument + 1; j < MethodParamCount; j++)
                    offset += j < ParamIsArray.Length && ParamIsArray[j] ? 2 : 1;
                Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)offset));
                Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)NESConstants.sp);
            }
            else
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)Locals[arg.GetLdlocIndex()!.Value].Address!.Value);
            if (physical != physicalArguments.Length - 1)
            {
                EmitJSR("pusha");
                UsedMethods?.Add("pusha");
            }
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
