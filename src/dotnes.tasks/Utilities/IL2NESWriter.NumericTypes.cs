using System.Reflection.Metadata;

namespace dotnes;

partial class IL2NESWriter
{
    MethodNumericTypes? _numericTypes;
    IReadOnlyDictionary<string, PrimitiveTypeCode?>? _numericFields;

    internal void ConfigureNumericTypes(IReadOnlyDictionary<string, MethodNumericTypes> methods,
        IReadOnlyDictionary<string, PrimitiveTypeCode?>? fields = null)
    {
        _numericFields = fields;
        methods.TryGetValue(MethodName ?? "main", out _numericTypes);
    }

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

    internal static int? NumericArgIndex(ILInstruction instruction) => instruction.OpCode switch
    {
        ILOpCode.Ldarg_0 => 0,
        ILOpCode.Ldarg_1 => 1,
        ILOpCode.Ldarg_2 => 2,
        ILOpCode.Ldarg_3 => 3,
        ILOpCode.Ldarg_s or ILOpCode.Ldarg => instruction.Integer,
        _ => null,
    };
}
