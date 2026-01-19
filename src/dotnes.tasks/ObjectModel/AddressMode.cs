namespace dotnes.ObjectModel;

/// <summary>
/// 6502 addressing modes per the official instruction set.
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
public enum AddressMode
{
    /// <summary>
    /// Implied: OPC (1 byte) - operand is implied by the instruction
    /// </summary>
    Implied,

    /// <summary>
    /// Accumulator: OPC A (1 byte) - operand is the accumulator
    /// </summary>
    Accumulator,

    /// <summary>
    /// Immediate: OPC #$BB (2 bytes) - operand is a literal byte value
    /// </summary>
    Immediate,

    /// <summary>
    /// Zero Page: OPC $LL (2 bytes) - operand is a zero page address ($00-$FF)
    /// </summary>
    ZeroPage,

    /// <summary>
    /// Zero Page,X: OPC $LL,X (2 bytes) - zero page address indexed by X
    /// </summary>
    ZeroPageX,

    /// <summary>
    /// Zero Page,Y: OPC $LL,Y (2 bytes) - zero page address indexed by Y
    /// </summary>
    ZeroPageY,

    /// <summary>
    /// Absolute: OPC $LLHH (3 bytes) - operand is a 16-bit address
    /// </summary>
    Absolute,

    /// <summary>
    /// Absolute,X: OPC $LLHH,X (3 bytes) - absolute address indexed by X
    /// </summary>
    AbsoluteX,

    /// <summary>
    /// Absolute,Y: OPC $LLHH,Y (3 bytes) - absolute address indexed by Y
    /// </summary>
    AbsoluteY,

    /// <summary>
    /// Indirect: OPC ($LLHH) (3 bytes) - JMP only, operand is address containing target
    /// </summary>
    Indirect,

    /// <summary>
    /// Indexed Indirect: OPC ($LL,X) (2 bytes) - zero page address + X gives pointer location
    /// </summary>
    IndexedIndirect,

    /// <summary>
    /// Indirect Indexed: OPC ($LL),Y (2 bytes) - zero page pointer, then add Y to result
    /// </summary>
    IndirectIndexed,

    /// <summary>
    /// Relative: OPC $BB (2 bytes) - signed offset for branch instructions (-128 to +127)
    /// </summary>
    Relative,
}
