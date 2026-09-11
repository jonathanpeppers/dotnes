using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

static class NumericStorage
{
    public static bool IsWord(PrimitiveTypeCode? type) =>
        type is PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16;

    public static bool IsSigned(PrimitiveTypeCode? type) =>
        type is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16;

    public static void RequireNarrowType(PrimitiveTypeCode? type, string? method)
    {
        if (type is PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32)
            throw new TranspileException(
                "32-bit values are not supported by typed expression lowering. Use explicit byte, sbyte, short, or ushort source storage.",
                method);
    }
}
