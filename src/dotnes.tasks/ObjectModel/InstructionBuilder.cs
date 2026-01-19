namespace dotnes.ObjectModel;

/// <summary>
/// Fluent builder for creating 6502 instructions.
/// Provides static methods for all instruction types and addressing modes.
/// </summary>
public static class Asm
{
    #region Implied/Accumulator Instructions (no operand)

    public static Instruction BRK() => new(Opcode.BRK, AddressMode.Implied);
    public static Instruction CLC() => new(Opcode.CLC, AddressMode.Implied);
    public static Instruction CLD() => new(Opcode.CLD, AddressMode.Implied);
    public static Instruction CLI() => new(Opcode.CLI, AddressMode.Implied);
    public static Instruction CLV() => new(Opcode.CLV, AddressMode.Implied);
    public static Instruction DEX() => new(Opcode.DEX, AddressMode.Implied);
    public static Instruction DEY() => new(Opcode.DEY, AddressMode.Implied);
    public static Instruction INX() => new(Opcode.INX, AddressMode.Implied);
    public static Instruction INY() => new(Opcode.INY, AddressMode.Implied);
    public static Instruction NOP() => new(Opcode.NOP, AddressMode.Implied);
    public static Instruction PHA() => new(Opcode.PHA, AddressMode.Implied);
    public static Instruction PHP() => new(Opcode.PHP, AddressMode.Implied);
    public static Instruction PLA() => new(Opcode.PLA, AddressMode.Implied);
    public static Instruction PLP() => new(Opcode.PLP, AddressMode.Implied);
    public static Instruction RTI() => new(Opcode.RTI, AddressMode.Implied);
    public static Instruction RTS() => new(Opcode.RTS, AddressMode.Implied);
    public static Instruction SEC() => new(Opcode.SEC, AddressMode.Implied);
    public static Instruction SED() => new(Opcode.SED, AddressMode.Implied);
    public static Instruction SEI() => new(Opcode.SEI, AddressMode.Implied);
    public static Instruction TAX() => new(Opcode.TAX, AddressMode.Implied);
    public static Instruction TAY() => new(Opcode.TAY, AddressMode.Implied);
    public static Instruction TSX() => new(Opcode.TSX, AddressMode.Implied);
    public static Instruction TXA() => new(Opcode.TXA, AddressMode.Implied);
    public static Instruction TXS() => new(Opcode.TXS, AddressMode.Implied);
    public static Instruction TYA() => new(Opcode.TYA, AddressMode.Implied);

    // Accumulator mode for shift/rotate
    public static Instruction ASL_A() => new(Opcode.ASL, AddressMode.Accumulator);
    public static Instruction LSR_A() => new(Opcode.LSR, AddressMode.Accumulator);
    public static Instruction ROL_A() => new(Opcode.ROL, AddressMode.Accumulator);
    public static Instruction ROR_A() => new(Opcode.ROR, AddressMode.Accumulator);

    #endregion

    #region Immediate Mode (#$XX)

    public static Instruction LDA(byte value) =>
        new(Opcode.LDA, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction LDX(byte value) =>
        new(Opcode.LDX, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction LDY(byte value) =>
        new(Opcode.LDY, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction ADC(byte value) =>
        new(Opcode.ADC, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction SBC(byte value) =>
        new(Opcode.SBC, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction AND(byte value) =>
        new(Opcode.AND, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction ORA(byte value) =>
        new(Opcode.ORA, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction EOR(byte value) =>
        new(Opcode.EOR, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction CMP(byte value) =>
        new(Opcode.CMP, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction CPX(byte value) =>
        new(Opcode.CPX, AddressMode.Immediate, new ImmediateOperand(value));

    public static Instruction CPY(byte value) =>
        new(Opcode.CPY, AddressMode.Immediate, new ImmediateOperand(value));

    #endregion

    #region Zero Page Mode ($XX)

    public static Instruction LDA_zpg(byte address) =>
        new(Opcode.LDA, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction LDX_zpg(byte address) =>
        new(Opcode.LDX, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction LDY_zpg(byte address) =>
        new(Opcode.LDY, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction STA_zpg(byte address) =>
        new(Opcode.STA, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction STX_zpg(byte address) =>
        new(Opcode.STX, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction STY_zpg(byte address) =>
        new(Opcode.STY, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction ADC_zpg(byte address) =>
        new(Opcode.ADC, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction SBC_zpg(byte address) =>
        new(Opcode.SBC, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction AND_zpg(byte address) =>
        new(Opcode.AND, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction ORA_zpg(byte address) =>
        new(Opcode.ORA, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction EOR_zpg(byte address) =>
        new(Opcode.EOR, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction CMP_zpg(byte address) =>
        new(Opcode.CMP, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction CPX_zpg(byte address) =>
        new(Opcode.CPX, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction CPY_zpg(byte address) =>
        new(Opcode.CPY, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction INC_zpg(byte address) =>
        new(Opcode.INC, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction DEC_zpg(byte address) =>
        new(Opcode.DEC, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction ASL_zpg(byte address) =>
        new(Opcode.ASL, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction LSR_zpg(byte address) =>
        new(Opcode.LSR, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction ROL_zpg(byte address) =>
        new(Opcode.ROL, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction ROR_zpg(byte address) =>
        new(Opcode.ROR, AddressMode.ZeroPage, new ImmediateOperand(address));

    public static Instruction BIT_zpg(byte address) =>
        new(Opcode.BIT, AddressMode.ZeroPage, new ImmediateOperand(address));

    #endregion

    #region Zero Page,X Mode ($XX,X)

    public static Instruction LDA_zpg_X(byte address) =>
        new(Opcode.LDA, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction LDY_zpg_X(byte address) =>
        new(Opcode.LDY, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction STA_zpg_X(byte address) =>
        new(Opcode.STA, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction STY_zpg_X(byte address) =>
        new(Opcode.STY, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction INC_zpg_X(byte address) =>
        new(Opcode.INC, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction DEC_zpg_X(byte address) =>
        new(Opcode.DEC, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction ASL_zpg_X(byte address) =>
        new(Opcode.ASL, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction LSR_zpg_X(byte address) =>
        new(Opcode.LSR, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction ROL_zpg_X(byte address) =>
        new(Opcode.ROL, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction ROR_X_zpg(byte address) =>
        new(Opcode.ROR, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction ADC_zpg_X(byte address) =>
        new(Opcode.ADC, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction SBC_zpg_X(byte address) =>
        new(Opcode.SBC, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction AND_zpg_X(byte address) =>
        new(Opcode.AND, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction ORA_zpg_X(byte address) =>
        new(Opcode.ORA, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction EOR_zpg_X(byte address) =>
        new(Opcode.EOR, AddressMode.ZeroPageX, new ImmediateOperand(address));

    public static Instruction CMP_zpg_X(byte address) =>
        new(Opcode.CMP, AddressMode.ZeroPageX, new ImmediateOperand(address));

    #endregion

    #region Zero Page,Y Mode ($XX,Y)

    public static Instruction LDX_zpg_Y(byte address) =>
        new(Opcode.LDX, AddressMode.ZeroPageY, new ImmediateOperand(address));

    public static Instruction STX_zpg_Y(byte address) =>
        new(Opcode.STX, AddressMode.ZeroPageY, new ImmediateOperand(address));

    #endregion

    #region Absolute Mode ($XXXX)

    public static Instruction LDA_abs(ushort address) =>
        new(Opcode.LDA, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction LDX_abs(ushort address) =>
        new(Opcode.LDX, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction LDY_abs(ushort address) =>
        new(Opcode.LDY, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction STA_abs(ushort address) =>
        new(Opcode.STA, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction STX_abs(ushort address) =>
        new(Opcode.STX, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction STY_abs(ushort address) =>
        new(Opcode.STY, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction INC_abs(ushort address) =>
        new(Opcode.INC, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction DEC_abs(ushort address) =>
        new(Opcode.DEC, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction BIT_abs(ushort address) =>
        new(Opcode.BIT, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction JMP(ushort address) =>
        new(Opcode.JMP, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction JSR(ushort address) =>
        new(Opcode.JSR, AddressMode.Absolute, new AbsoluteOperand(address));

    #endregion

    #region Absolute,X Mode ($XXXX,X)

    public static Instruction LDA_abs_X(ushort address) =>
        new(Opcode.LDA, AddressMode.AbsoluteX, new AbsoluteOperand(address));

    public static Instruction LDY_abs_X(ushort address) =>
        new(Opcode.LDY, AddressMode.AbsoluteX, new AbsoluteOperand(address));

    public static Instruction STA_abs_X(ushort address) =>
        new(Opcode.STA, AddressMode.AbsoluteX, new AbsoluteOperand(address));

    #endregion

    #region Absolute,Y Mode ($XXXX,Y)

    public static Instruction LDA_abs_Y(ushort address) =>
        new(Opcode.LDA, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction LDX_abs_Y(ushort address) =>
        new(Opcode.LDX, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction STA_abs_Y(ushort address) =>
        new(Opcode.STA, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction ADC_abs_Y(ushort address) =>
        new(Opcode.ADC, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction SBC_abs_Y(ushort address) =>
        new(Opcode.SBC, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction AND_Y_abs(ushort address) =>
        new(Opcode.AND, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction ORA_abs_Y(ushort address) =>
        new(Opcode.ORA, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction EOR_Y_abs(ushort address) =>
        new(Opcode.EOR, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    public static Instruction CMP_abs_Y(ushort address) =>
        new(Opcode.CMP, AddressMode.AbsoluteY, new AbsoluteOperand(address));

    #endregion

    #region Indirect Mode (JMP only)

    public static Instruction JMP_ind(ushort address) =>
        new(Opcode.JMP, AddressMode.Indirect, new AbsoluteOperand(address));

    #endregion

    #region Indexed Indirect Mode ($XX,X)

    public static Instruction LDA_ind_X(byte address) =>
        new(Opcode.LDA, AddressMode.IndexedIndirect, new ImmediateOperand(address));

    public static Instruction STA_ind_X(byte address) =>
        new(Opcode.STA, AddressMode.IndexedIndirect, new ImmediateOperand(address));

    #endregion

    #region Indirect Indexed Mode ($XX),Y

    public static Instruction LDA_ind_Y(byte address) =>
        new(Opcode.LDA, AddressMode.IndirectIndexed, new ImmediateOperand(address));

    public static Instruction STA_ind_Y(byte address) =>
        new(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(address));

    #endregion

    #region Label-Based Absolute Addressing

    public static Instruction JMP(string label) =>
        new(Opcode.JMP, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    public static Instruction JMP_abs(string label) =>
        new(Opcode.JMP, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    public static Instruction JMP_abs(ushort address) =>
        new(Opcode.JMP, AddressMode.Absolute, new AbsoluteOperand(address));

    public static Instruction JSR(string label) =>
        new(Opcode.JSR, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    public static Instruction LDA_abs(string label) =>
        new(Opcode.LDA, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    public static Instruction STA_abs(string label) =>
        new(Opcode.STA, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    #endregion

    #region Relative Addressing (Branches) - Label

    public static Instruction BCC(string label) =>
        new(Opcode.BCC, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BCS(string label) =>
        new(Opcode.BCS, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BEQ(string label) =>
        new(Opcode.BEQ, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BMI(string label) =>
        new(Opcode.BMI, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BNE(string label) =>
        new(Opcode.BNE, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BPL(string label) =>
        new(Opcode.BPL, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BVC(string label) =>
        new(Opcode.BVC, AddressMode.Relative, new RelativeOperand(label));

    public static Instruction BVS(string label) =>
        new(Opcode.BVS, AddressMode.Relative, new RelativeOperand(label));

    #endregion

    #region Relative Addressing (Branches) - Byte Offset

    public static Instruction BCC(sbyte offset) =>
        new(Opcode.BCC, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BCS(sbyte offset) =>
        new(Opcode.BCS, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BEQ(sbyte offset) =>
        new(Opcode.BEQ, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BMI(sbyte offset) =>
        new(Opcode.BMI, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BNE(sbyte offset) =>
        new(Opcode.BNE, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BPL(sbyte offset) =>
        new(Opcode.BPL, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BVC(sbyte offset) =>
        new(Opcode.BVC, AddressMode.Relative, new RelativeByteOperand(offset));

    public static Instruction BVS(sbyte offset) =>
        new(Opcode.BVS, AddressMode.Relative, new RelativeByteOperand(offset));

    #endregion

    #region Generic Instruction Creation

    /// <summary>
    /// Creates an instruction with the specified opcode, mode, and operand
    /// </summary>
    public static Instruction Create(Opcode opcode, AddressMode mode, Operand? operand = null, string? comment = null)
        => new(opcode, mode, operand, comment);

    /// <summary>
    /// Creates an implied instruction
    /// </summary>
    public static Instruction Implied(Opcode opcode)
        => new(opcode, AddressMode.Implied);

    /// <summary>
    /// Creates an immediate mode instruction
    /// </summary>
    public static Instruction Imm(Opcode opcode, byte value)
        => new(opcode, AddressMode.Immediate, new ImmediateOperand(value));

    /// <summary>
    /// Creates a zero page instruction
    /// </summary>
    public static Instruction Zpg(Opcode opcode, byte address)
        => new(opcode, AddressMode.ZeroPage, new ImmediateOperand(address));

    /// <summary>
    /// Creates an absolute instruction
    /// </summary>
    public static Instruction Abs(Opcode opcode, ushort address)
        => new(opcode, AddressMode.Absolute, new AbsoluteOperand(address));

    /// <summary>
    /// Creates an absolute instruction with a label
    /// </summary>
    public static Instruction Abs(Opcode opcode, string label)
        => new(opcode, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));

    #endregion
}
