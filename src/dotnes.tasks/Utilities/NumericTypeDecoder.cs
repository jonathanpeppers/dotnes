using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Retains declared primitive types instead of inferring a local's width from its
/// initializer. References, arrays and structs are handled by their own lowering.
/// </summary>
sealed class NumericTypeDecoder : ISignatureTypeProvider<PrimitiveTypeCode?, object?>
{
    public PrimitiveTypeCode? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;
    public PrimitiveTypeCode? GetArrayType(PrimitiveTypeCode? elementType, ArrayShape shape) => null;
    public PrimitiveTypeCode? GetSZArrayType(PrimitiveTypeCode? elementType) => null;
    public PrimitiveTypeCode? GetByReferenceType(PrimitiveTypeCode? elementType) => null;
    public PrimitiveTypeCode? GetPointerType(PrimitiveTypeCode? elementType) => null;
    public PrimitiveTypeCode? GetFunctionPointerType(MethodSignature<PrimitiveTypeCode?> signature) => null;
    public PrimitiveTypeCode? GetGenericInstantiation(PrimitiveTypeCode? genericType, ImmutableArray<PrimitiveTypeCode?> typeArguments) => null;
    public PrimitiveTypeCode? GetGenericMethodParameter(object? genericContext, int index) => null;
    public PrimitiveTypeCode? GetGenericTypeParameter(object? genericContext, int index) => null;
    public PrimitiveTypeCode? GetModifiedType(PrimitiveTypeCode? modifier, PrimitiveTypeCode? unmodifiedType, bool isRequired) => unmodifiedType;
    public PrimitiveTypeCode? GetPinnedType(PrimitiveTypeCode? elementType) => elementType;
    public PrimitiveTypeCode? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        if (type.BaseType.Kind != HandleKind.TypeReference
            || reader.GetString(reader.GetTypeReference((TypeReferenceHandle)type.BaseType).Name) != nameof(Enum))
            return null;
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) == "value__")
                return field.DecodeSignature(this, null);
        }
        return null;
    }

    public PrimitiveTypeCode? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var type = typeof(NESLib).Assembly.GetType($"{reader.GetString(reference.Namespace)}.{reader.GetString(reference.Name)}");
        if (type == null || !type.IsEnum)
            return null;
        return Type.GetTypeCode(Enum.GetUnderlyingType(type)) switch
        {
            TypeCode.Byte => PrimitiveTypeCode.Byte,
            TypeCode.SByte => PrimitiveTypeCode.SByte,
            TypeCode.Int16 => PrimitiveTypeCode.Int16,
            TypeCode.UInt16 => PrimitiveTypeCode.UInt16,
            TypeCode.Int32 => PrimitiveTypeCode.Int32,
            TypeCode.UInt32 => PrimitiveTypeCode.UInt32,
            _ => null,
        };
    }
    public PrimitiveTypeCode? GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

sealed record MethodNumericTypes(
    ImmutableArray<PrimitiveTypeCode?> Locals,
    ImmutableArray<PrimitiveTypeCode?> Parameters,
    PrimitiveTypeCode? ReturnType);
