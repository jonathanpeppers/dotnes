namespace dotnes.ObjectModel;

/// <summary>
/// 6502 opcodes (mnemonics) without addressing mode encoding.
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
public enum Opcode
{
    /// <summary>Add with Carry</summary>
    ADC,
    /// <summary>AND with Accumulator</summary>
    AND,
    /// <summary>Arithmetic Shift Left</summary>
    ASL,
    /// <summary>Branch on Carry Clear</summary>
    BCC,
    /// <summary>Branch on Carry Set</summary>
    BCS,
    /// <summary>Branch on Equal (Zero Set)</summary>
    BEQ,
    /// <summary>Bit Test</summary>
    BIT,
    /// <summary>Branch on Minus (Negative Set)</summary>
    BMI,
    /// <summary>Branch on Not Equal (Zero Clear)</summary>
    BNE,
    /// <summary>Branch on Plus (Negative Clear)</summary>
    BPL,
    /// <summary>Break / Interrupt</summary>
    BRK,
    /// <summary>Branch on Overflow Clear</summary>
    BVC,
    /// <summary>Branch on Overflow Set</summary>
    BVS,
    /// <summary>Clear Carry Flag</summary>
    CLC,
    /// <summary>Clear Decimal Mode</summary>
    CLD,
    /// <summary>Clear Interrupt Disable</summary>
    CLI,
    /// <summary>Clear Overflow Flag</summary>
    CLV,
    /// <summary>Compare with Accumulator</summary>
    CMP,
    /// <summary>Compare with X</summary>
    CPX,
    /// <summary>Compare with Y</summary>
    CPY,
    /// <summary>Decrement Memory</summary>
    DEC,
    /// <summary>Decrement X</summary>
    DEX,
    /// <summary>Decrement Y</summary>
    DEY,
    /// <summary>Exclusive OR with Accumulator</summary>
    EOR,
    /// <summary>Increment Memory</summary>
    INC,
    /// <summary>Increment X</summary>
    INX,
    /// <summary>Increment Y</summary>
    INY,
    /// <summary>Jump</summary>
    JMP,
    /// <summary>Jump Subroutine</summary>
    JSR,
    /// <summary>Load Accumulator</summary>
    LDA,
    /// <summary>Load X</summary>
    LDX,
    /// <summary>Load Y</summary>
    LDY,
    /// <summary>Logical Shift Right</summary>
    LSR,
    /// <summary>No Operation</summary>
    NOP,
    /// <summary>OR with Accumulator</summary>
    ORA,
    /// <summary>Push Accumulator</summary>
    PHA,
    /// <summary>Push Processor Status</summary>
    PHP,
    /// <summary>Pull Accumulator</summary>
    PLA,
    /// <summary>Pull Processor Status</summary>
    PLP,
    /// <summary>Rotate Left</summary>
    ROL,
    /// <summary>Rotate Right</summary>
    ROR,
    /// <summary>Return from Interrupt</summary>
    RTI,
    /// <summary>Return from Subroutine</summary>
    RTS,
    /// <summary>Subtract with Carry</summary>
    SBC,
    /// <summary>Set Carry Flag</summary>
    SEC,
    /// <summary>Set Decimal Mode</summary>
    SED,
    /// <summary>Set Interrupt Disable</summary>
    SEI,
    /// <summary>Store Accumulator</summary>
    STA,
    /// <summary>Store X</summary>
    STX,
    /// <summary>Store Y</summary>
    STY,
    /// <summary>Transfer Accumulator to X</summary>
    TAX,
    /// <summary>Transfer Accumulator to Y</summary>
    TAY,
    /// <summary>Transfer Stack Pointer to X</summary>
    TSX,
    /// <summary>Transfer X to Accumulator</summary>
    TXA,
    /// <summary>Transfer X to Stack Pointer</summary>
    TXS,
    /// <summary>Transfer Y to Accumulator</summary>
    TYA,
}
