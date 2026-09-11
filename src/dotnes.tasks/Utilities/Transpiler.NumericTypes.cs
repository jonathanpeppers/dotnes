using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    internal Dictionary<string, MethodNumericTypes> NumericTypes { get; } = new(StringComparer.Ordinal);

    Dictionary<string, PrimitiveTypeCode?> GetNumericFieldTypes(ISet<string>? ambiguousFields = null) =>
        _reader.FieldDefinitions.Select(h => _reader.GetFieldDefinition(h))
            .Where(f => (f.Attributes & System.Reflection.FieldAttributes.Static) != 0)
            .GroupBy(f => _reader.GetString(f.Name))
            .ToDictionary(g => g.Key, g =>
            {
                var types = g.Select(field => field.DecodeSignature(new NumericTypeDecoder(), null)).Distinct().ToArray();
                if (types.Length > 1)
                    ambiguousFields?.Add(g.Key);
                return types.Length == 1 ? types[0] : null;
            });

    PrimitiveTypeCode?[] GetExpressionValueTypes(ILInstruction[] instructions,
        ILValueAnalysis analysis, ReflectionCache reflection, string method,
        IReadOnlyDictionary<int, PrimitiveTypeCode>? compactInts = null)
    {
        var types = new PrimitiveTypeCode?[instructions.Length];
        var fieldTypes = GetNumericFieldTypes();
        NumericTypes.TryGetValue(method, out var signature);
        for (int i = 0; i < instructions.Length; i++)
        {
            if (!analysis.ProducesValue[i])
                continue;
            var instruction = instructions[i];
            PrimitiveTypeCode? type = null;
            if (instruction.GetLdlocIndex() is int local && signature != null && local < signature.Locals.Length)
                type = compactInts != null && compactInts.TryGetValue(local, out var compact) ? compact : signature.Locals[local];
            else if (IL2NESWriter.NumericArgIndex(instruction) is int arg && signature != null && arg < signature.Parameters.Length)
                type = signature.Parameters[arg];
            else if (instruction.OpCode == ILOpCode.Call && instruction.String is string name)
            {
                if (NumericTypes.TryGetValue(name, out var callee))
                    type = callee.ReturnType;
                else if (reflection.HasReturnValue(name))
                    type = reflection.TryReturns16Bit(name) ? PrimitiveTypeCode.UInt16 : PrimitiveTypeCode.Byte;
            }
            else if (instruction.GetLdcValue() is int value)
                type = value < sbyte.MinValue ? PrimitiveTypeCode.Int16
                    : value < 0 ? PrimitiveTypeCode.SByte
                    : value <= byte.MaxValue ? PrimitiveTypeCode.Byte : PrimitiveTypeCode.UInt16;
            else if (instruction.OpCode == ILOpCode.Ldsfld && instruction.String is string field
                && fieldTypes.TryGetValue(field, out var fieldType))
                type = fieldType;
            else if (instruction.OpCode is ILOpCode.Ceq or ILOpCode.Clt or ILOpCode.Clt_un
                or ILOpCode.Cgt or ILOpCode.Cgt_un)
                type = PrimitiveTypeCode.Boolean;
            else if (instruction.OpCode is ILOpCode.Ldelem_u1 or ILOpCode.Ldind_u1 or ILOpCode.Conv_u1)
                type = PrimitiveTypeCode.Byte;
            else if (instruction.OpCode is ILOpCode.Ldelem_u2 or ILOpCode.Ldind_u2 or ILOpCode.Conv_u2)
                type = PrimitiveTypeCode.UInt16;
            else if (instruction.OpCode is ILOpCode.Ldelem_i1 or ILOpCode.Ldind_i1 or ILOpCode.Conv_i1)
                type = PrimitiveTypeCode.SByte;
            else if (instruction.OpCode is ILOpCode.Ldelem_i2 or ILOpCode.Ldind_i2 or ILOpCode.Conv_i2)
                type = PrimitiveTypeCode.Int16;
            else if (instruction.OpCode is ILOpCode.Add or ILOpCode.Sub
                && analysis.Inputs[i].Length == 2
                && analysis.Inputs[i].All(p => p >= 0 && types[p] is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte))
                type = NumericValueUsage.IsExplicitlyNarrowed(instructions, analysis, i, byteOnly: true) ? PrimitiveTypeCode.Byte
                    : instruction.OpCode == ILOpCode.Sub
                    || analysis.Inputs[i].Any(p => types[p] == PrimitiveTypeCode.SByte)
                        ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16;
            else if (instruction.OpCode is ILOpCode.Mul or ILOpCode.Shl
                && analysis.Inputs[i].Length == 2
                && analysis.Inputs[i].All(p => p >= 0 && types[p] != null))
                type = NumericValueUsage.IsExplicitlyNarrowed(instructions, analysis, i, byteOnly: true)
                    ? PrimitiveTypeCode.Byte
                    : analysis.Inputs[i].Any(p => types[p] is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16)
                        ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16;
            else if (IsScalarExpression(instruction.OpCode)
                && analysis.Inputs[i].All(p => p >= 0 && types[p] != null && !analysis.Escapes[p]))
                type = analysis.Inputs[i].Any(p => types[p] is PrimitiveTypeCode.Int16 or PrimitiveTypeCode.SByte)
                    ? PrimitiveTypeCode.Int16
                    : analysis.Inputs[i].Any(p => types[p] is PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32)
                        ? PrimitiveTypeCode.UInt16 : PrimitiveTypeCode.Byte;
            types[i] = type;
        }
        return types;
    }

    void ReadNumericTypes(MethodDefinition method, string name)
    {
        var decoder = new NumericTypeDecoder();
        var signature = method.DecodeSignature(decoder, null);
        ImmutableArray<PrimitiveTypeCode?> locals = [];
        if (method.RelativeVirtualAddress != 0)
        {
            var localSignature = _pe.GetMethodBody(method.RelativeVirtualAddress).LocalSignature;
            if (!localSignature.IsNil)
                locals = _reader.GetStandaloneSignature(localSignature).DecodeLocalSignature(decoder, null);
        }
        NumericTypes[name] = new(locals, signature.ParameterTypes, signature.ReturnType);
    }
}
