using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    bool TryEmitDisplacedByteIndexRead()
    {
        if (Instructions == null || _numericValues == null
            || !ByteIndexDisplacement.TryMatch(Instructions, _numericValues, Index,
                out int arrayIndex, out int variable, out int displacement)
            || !Locals.TryGetValue(arrayIndex, out var array) || array.Address == null
            || array.ArraySize == 0 || array.ArrayParameterIndex != null || array.LabelName != null
            || Instructions[variable].GetLdlocIndex() is not int local
            || NumericType(variable) != PrimitiveTypeCode.Byte
            || !Locals.TryGetValue(local, out var index) || index.Address == null
            || index.IsWord || index.ArraySize != 0 || index.LabelName != null
            || !_blockCountAtILOffset.TryGetValue(Instructions[Index - 4].Offset, out int start))
            return false;
        // Absolute indexed addressing carries into the high address byte, so
        // folding the displacement into the base does not truncate the index.
        RemoveLastInstructions(GetBufferedBlockCount() - start);
        _argStackAdjust = _numericArgAdjust[Instructions[Index - 4].Offset];
        Emit(Opcode.LDX, AddressMode.Absolute, (ushort)index.Address.Value);
        Emit(Opcode.LDA, AddressMode.AbsoluteX, unchecked((ushort)(array.Address.Value + displacement)));
        Stack.Push(0);
        _accState = AccumulatorState.Runtime;
        _savedState = SavedValueState.None;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        return true;
    }
}
