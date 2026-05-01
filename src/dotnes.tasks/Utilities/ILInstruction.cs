using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Holds info about IL
/// </summary>
record ILInstruction(ILOpCode OpCode, int Offset = 0, int? Integer = null, string? String = null, ImmutableArray<byte>? Bytes = null)
{
    /// <summary>
    /// Gets the local index for a Stloc opcode, or null if not a Stloc.
    /// </summary>
    public int? GetStlocIndex() => OpCode switch
    {
        ILOpCode.Stloc_0 => 0,
        ILOpCode.Stloc_1 => 1,
        ILOpCode.Stloc_2 => 2,
        ILOpCode.Stloc_3 => 3,
        ILOpCode.Stloc_s or ILOpCode.Stloc => Integer,
        _ => null
    };

    /// <summary>
    /// Gets the constant value for a Ldc_i4 opcode, or null if not a Ldc_i4.
    /// </summary>
    public int? GetLdcValue() => OpCode switch
    {
        ILOpCode.Ldc_i4_m1 => -1,
        ILOpCode.Ldc_i4_0 => 0,
        ILOpCode.Ldc_i4_1 => 1,
        ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldc_i4_3 => 3,
        ILOpCode.Ldc_i4_4 => 4,
        ILOpCode.Ldc_i4_5 => 5,
        ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7,
        ILOpCode.Ldc_i4_8 => 8,
        ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => Integer,
        _ => null
    };
}
