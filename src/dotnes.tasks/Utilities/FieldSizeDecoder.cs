using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

class FieldSizeDecoder : ISignatureTypeProvider<int, object?>
{
    public int GetArrayType(int elementType, ArrayShape shape) => throw new NotImplementedException();

    public int GetByReferenceType(int elementType) => throw new NotImplementedException();

    public int GetFunctionPointerType(MethodSignature<int> signature) => throw new NotImplementedException();

    public int GetGenericInstantiation(int genericType, ImmutableArray<int> typeArguments) => throw new NotImplementedException();

    public int GetGenericMethodParameter(object? genericContext, int index) => throw new NotImplementedException();

    public int GetGenericTypeParameter(object? genericContext, int index) => throw new NotImplementedException();

    public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => throw new NotImplementedException();

    public int GetPinnedType(int elementType) => throw new NotImplementedException();

    public int GetPointerType(int elementType) => throw new NotImplementedException();

    public int GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte => 1,
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Single => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Double => 8,
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr => 4,
        _ => throw new NotImplementedException($"PrimitiveTypeCode {typeCode}"),
    };

    public int GetSZArrayType(int elementType) => throw new NotImplementedException();

    public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        return td.GetLayout().Size;
    }

    public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => throw new NotImplementedException();

    public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => throw new NotImplementedException();
}
