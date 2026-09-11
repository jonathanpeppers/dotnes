using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    internal Dictionary<string, MethodNumericTypes> NumericTypes { get; } = new(StringComparer.Ordinal);

    Dictionary<string, PrimitiveTypeCode?> GetNumericFieldTypes()
    {
        var decoder = new NumericTypeDecoder();
        return _reader.FieldDefinitions.Select(handle => _reader.GetFieldDefinition(handle))
            .Where(field => (field.Attributes & System.Reflection.FieldAttributes.Static) != 0)
            .GroupBy(field => _reader.GetString(field.Name))
            .ToDictionary(group => group.Key, group =>
            {
                var types = group.Select(field => field.DecodeSignature(decoder, null)).Distinct().ToArray();
                return types.Length == 1 ? types[0] : null;
            });
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
