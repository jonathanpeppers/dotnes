using System.Reflection.Metadata;

namespace dotnes;

partial class IL2NESWriter
{
    IReadOnlyDictionary<string, PrimitiveTypeCode?>? _numericFields;

    PrimitiveTypeCode? DeclaredScalarType(ILInstruction instruction)
    {
        if (instruction.GetLdlocIndex() is int local)
            return _numericTypes != null && local >= 0 && local < _numericTypes.Locals.Length
                ? _numericTypes.Locals[local] : null;
        if (NumericArgIndex(instruction) is int parameter)
            return _numericTypes != null && parameter >= 0 && parameter < _numericTypes.Parameters.Length
                ? _numericTypes.Parameters[parameter] : null;
        return instruction.OpCode == ILOpCode.Ldsfld && instruction.String is string field
            && _numericFields != null && _numericFields.TryGetValue(field, out var type)
                ? type : null;
    }

}
