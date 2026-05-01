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
}
