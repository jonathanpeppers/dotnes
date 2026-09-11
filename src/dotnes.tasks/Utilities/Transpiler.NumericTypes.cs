using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    internal Dictionary<string, MethodNumericTypes> NumericTypes { get; } = new(StringComparer.Ordinal);

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
