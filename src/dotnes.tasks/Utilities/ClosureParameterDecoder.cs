using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

enum ClosureParameterKind
{
    None,
    Value,
    ByReference,
}

sealed class ClosureParameterDecoder(ISet<TypeDefinitionHandle> closureTypes)
    : ISignatureTypeProvider<ClosureParameterKind, object?>
{
    public ClosureParameterKind GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        closureTypes.Contains(handle) ? ClosureParameterKind.Value : ClosureParameterKind.None;
    public ClosureParameterKind GetByReferenceType(ClosureParameterKind elementType) =>
        elementType == ClosureParameterKind.Value ? ClosureParameterKind.ByReference : ClosureParameterKind.None;
    public ClosureParameterKind GetModifiedType(ClosureParameterKind modifier, ClosureParameterKind unmodifiedType, bool isRequired) => unmodifiedType;
    public ClosureParameterKind GetPinnedType(ClosureParameterKind elementType) => elementType;
    public ClosureParameterKind GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public ClosureParameterKind GetPrimitiveType(PrimitiveTypeCode typeCode) => ClosureParameterKind.None;
    public ClosureParameterKind GetArrayType(ClosureParameterKind elementType, ArrayShape shape) => ClosureParameterKind.None;
    public ClosureParameterKind GetSZArrayType(ClosureParameterKind elementType) => ClosureParameterKind.None;
    public ClosureParameterKind GetPointerType(ClosureParameterKind elementType) => ClosureParameterKind.None;
    public ClosureParameterKind GetFunctionPointerType(MethodSignature<ClosureParameterKind> signature) => ClosureParameterKind.None;
    public ClosureParameterKind GetGenericInstantiation(ClosureParameterKind genericType, ImmutableArray<ClosureParameterKind> typeArguments) => ClosureParameterKind.None;
    public ClosureParameterKind GetGenericMethodParameter(object? genericContext, int index) => ClosureParameterKind.None;
    public ClosureParameterKind GetGenericTypeParameter(object? genericContext, int index) => ClosureParameterKind.None;
    public ClosureParameterKind GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => ClosureParameterKind.None;
}
