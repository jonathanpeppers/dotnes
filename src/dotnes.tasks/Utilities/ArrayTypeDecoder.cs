using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

enum ArraySignatureType
{
    Other,
    Void,
    Byte,
    ByteArray,
    UnsupportedArray,
}

/// <summary>
/// Decodes complete signatures so array arguments are not confused with token
/// bytes from preceding struct, reference, or generic parameter types.
/// </summary>
sealed class ArrayTypeDecoder : ISignatureTypeProvider<ArraySignatureType, object?>
{
    public ArraySignatureType GetPrimitiveType(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Byte => ArraySignatureType.Byte,
        PrimitiveTypeCode.Void => ArraySignatureType.Void,
        _ => ArraySignatureType.Other,
    };

    public ArraySignatureType GetSZArrayType(ArraySignatureType elementType) =>
        elementType == ArraySignatureType.Byte ? ArraySignatureType.ByteArray : ArraySignatureType.UnsupportedArray;
    public ArraySignatureType GetArrayType(ArraySignatureType elementType, ArrayShape shape) => ArraySignatureType.UnsupportedArray;
    public ArraySignatureType GetByReferenceType(ArraySignatureType elementType) =>
        elementType is ArraySignatureType.ByteArray or ArraySignatureType.UnsupportedArray
            ? ArraySignatureType.UnsupportedArray : ArraySignatureType.Other;
    public ArraySignatureType GetPointerType(ArraySignatureType elementType) => ArraySignatureType.Other;
    public ArraySignatureType GetFunctionPointerType(MethodSignature<ArraySignatureType> signature) => ArraySignatureType.Other;
    public ArraySignatureType GetGenericInstantiation(ArraySignatureType genericType, ImmutableArray<ArraySignatureType> arguments) => ArraySignatureType.Other;
    public ArraySignatureType GetGenericMethodParameter(object? context, int index) => ArraySignatureType.Other;
    public ArraySignatureType GetGenericTypeParameter(object? context, int index) => ArraySignatureType.Other;
    public ArraySignatureType GetModifiedType(ArraySignatureType modifier, ArraySignatureType unmodifiedType, bool required) => unmodifiedType;
    public ArraySignatureType GetPinnedType(ArraySignatureType elementType) => elementType;
    public ArraySignatureType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) => ArraySignatureType.Other;
    public ArraySignatureType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) => ArraySignatureType.Other;
    public ArraySignatureType GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);
}
