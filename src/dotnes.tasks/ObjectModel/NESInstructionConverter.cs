namespace dotnes.ObjectModel;

/// <summary>
/// Converts the legacy NESInstruction enum to the new Opcode + AddressMode.
/// This provides a migration path from the old combined enum to the cleaner
/// separate Opcode and AddressMode enums used by the object model.
/// </summary>
static class NESInstructionConverter
{
    /// <summary>
    /// Converts a NESInstruction enum value to its corresponding Opcode and AddressMode.
    /// </summary>
    public static (Opcode opcode, AddressMode mode) Convert(NESInstruction i)
    {
        return i switch
        {
            // ADC
            NESInstruction.ADC => (Opcode.ADC, AddressMode.Immediate),
            NESInstruction.ADC_X_ind => (Opcode.ADC, AddressMode.IndexedIndirect),
            NESInstruction.ADC_X_zpg => (Opcode.ADC, AddressMode.ZeroPage),
            NESInstruction.ADC_abs => (Opcode.ADC, AddressMode.Absolute),

            // AND
            NESInstruction.AND => (Opcode.AND, AddressMode.Immediate),
            NESInstruction.AND_zpg => (Opcode.AND, AddressMode.ZeroPage),
            NESInstruction.AND_abs => (Opcode.AND, AddressMode.Absolute),
            NESInstruction.AND_X_ind => (Opcode.AND, AddressMode.IndexedIndirect),
            NESInstruction.AND_Y_ind => (Opcode.AND, AddressMode.IndirectIndexed),
            NESInstruction.AND_Y_abs => (Opcode.AND, AddressMode.AbsoluteY),

            // ASL
            NESInstruction.ASL_A => (Opcode.ASL, AddressMode.Accumulator),
            NESInstruction.ASL_zpg => (Opcode.ASL, AddressMode.ZeroPage),
            NESInstruction.ASL_zpg_X => (Opcode.ASL, AddressMode.ZeroPageX),
            NESInstruction.ASL_abs => (Opcode.ASL, AddressMode.Absolute),
            NESInstruction.ASL_abs_X => (Opcode.ASL, AddressMode.AbsoluteX),

            // Branches
            NESInstruction.BCC => (Opcode.BCC, AddressMode.Relative),
            NESInstruction.BCS => (Opcode.BCS, AddressMode.Relative),
            NESInstruction.BEQ_rel => (Opcode.BEQ, AddressMode.Relative),
            NESInstruction.BMI => (Opcode.BMI, AddressMode.Relative),
            NESInstruction.BNE_rel => (Opcode.BNE, AddressMode.Relative),
            NESInstruction.BPL => (Opcode.BPL, AddressMode.Relative),

            // BIT
            NESInstruction.BIT_zpg => (Opcode.BIT, AddressMode.ZeroPage),
            NESInstruction.BIT_abs => (Opcode.BIT, AddressMode.Absolute),

            // BRK
            NESInstruction.BRK => (Opcode.BRK, AddressMode.Implied),

            // Clear flags
            NESInstruction.CLC => (Opcode.CLC, AddressMode.Implied),
            NESInstruction.CLD_impl => (Opcode.CLD, AddressMode.Implied),

            // CMP
            NESInstruction.CMP => (Opcode.CMP, AddressMode.Immediate),
            NESInstruction.CMP_zpg => (Opcode.CMP, AddressMode.ZeroPage),
            NESInstruction.CMP_zpg_X => (Opcode.CMP, AddressMode.ZeroPageX),
            NESInstruction.CMP_ind_Y => (Opcode.CMP, AddressMode.IndirectIndexed),
            NESInstruction.CMP_abs_X => (Opcode.CMP, AddressMode.AbsoluteX),
            NESInstruction.CMP_abs_Y => (Opcode.CMP, AddressMode.AbsoluteY),

            // CPX
            NESInstruction.CPX => (Opcode.CPX, AddressMode.Immediate),

            // CPY
            NESInstruction.CPY => (Opcode.CPY, AddressMode.Immediate),

            // DEC
            NESInstruction.DEC_zpg => (Opcode.DEC, AddressMode.ZeroPage),
            NESInstruction.DEC_zpg_X => (Opcode.DEC, AddressMode.ZeroPageX),
            NESInstruction.DEC_abs => (Opcode.DEC, AddressMode.Absolute),
            NESInstruction.DEC_abs_X => (Opcode.DEC, AddressMode.AbsoluteX),

            // DEX, DEY
            NESInstruction.DEX_impl => (Opcode.DEX, AddressMode.Implied),
            NESInstruction.DEY_impl => (Opcode.DEY, AddressMode.Implied),

            // EOR
            NESInstruction.EOR_imm => (Opcode.EOR, AddressMode.Immediate),
            NESInstruction.EOR_zpg => (Opcode.EOR, AddressMode.ZeroPage),
            NESInstruction.EOR_zpg_X => (Opcode.EOR, AddressMode.ZeroPageX),
            NESInstruction.EOR_abs => (Opcode.EOR, AddressMode.Absolute),
            NESInstruction.EOR_X => (Opcode.EOR, AddressMode.AbsoluteX),
            NESInstruction.EOR_Y_abs => (Opcode.EOR, AddressMode.AbsoluteY),
            NESInstruction.EOR_Y_ind => (Opcode.EOR, AddressMode.IndirectIndexed),

            // INC
            NESInstruction.INC_zpg => (Opcode.INC, AddressMode.ZeroPage),
            NESInstruction.INC_abs => (Opcode.INC, AddressMode.Absolute),

            // INX, INY
            NESInstruction.INX_impl => (Opcode.INX, AddressMode.Implied),
            NESInstruction.INY_impl => (Opcode.INY, AddressMode.Implied),

            // JMP
            NESInstruction.JMP_abs => (Opcode.JMP, AddressMode.Absolute),
            NESInstruction.JMP_ind => (Opcode.JMP, AddressMode.Indirect),

            // JSR
            NESInstruction.JSR => (Opcode.JSR, AddressMode.Absolute),

            // LDA
            NESInstruction.LDA => (Opcode.LDA, AddressMode.Immediate),
            NESInstruction.LDA_zpg => (Opcode.LDA, AddressMode.ZeroPage),
            NESInstruction.LDA_abs => (Opcode.LDA, AddressMode.Absolute),
            NESInstruction.LDA_abs_X => (Opcode.LDA, AddressMode.AbsoluteX),
            NESInstruction.LDA_abs_y => (Opcode.LDA, AddressMode.AbsoluteY),
            NESInstruction.LDA_X_ind => (Opcode.LDA, AddressMode.IndexedIndirect),
            NESInstruction.LDA_ind_Y => (Opcode.LDA, AddressMode.IndirectIndexed),

            // LDX
            NESInstruction.LDX => (Opcode.LDX, AddressMode.Immediate),
            NESInstruction.LDX_zpg => (Opcode.LDX, AddressMode.ZeroPage),
            NESInstruction.LDX_abs => (Opcode.LDX, AddressMode.Absolute),

            // LDY
            NESInstruction.LDY => (Opcode.LDY, AddressMode.Immediate),
            NESInstruction.LDY_zpg => (Opcode.LDY, AddressMode.ZeroPage),
            NESInstruction.LDY_abs => (Opcode.LDY, AddressMode.Absolute),

            // LSR
            NESInstruction.LSR_impl => (Opcode.LSR, AddressMode.Accumulator),
            NESInstruction.LSR_zpg => (Opcode.LSR, AddressMode.ZeroPage),
            NESInstruction.LSR_zpg_X => (Opcode.LSR, AddressMode.ZeroPageX),
            NESInstruction.LSR_abs => (Opcode.LSR, AddressMode.Absolute),
            NESInstruction.LSR_abs_X => (Opcode.LSR, AddressMode.AbsoluteX),

            // ORA
            NESInstruction.ORA => (Opcode.ORA, AddressMode.Immediate),
            NESInstruction.ORA_zpg => (Opcode.ORA, AddressMode.ZeroPage),
            NESInstruction.ORA_abs => (Opcode.ORA, AddressMode.Absolute),
            NESInstruction.ORA_X_ind => (Opcode.ORA, AddressMode.IndexedIndirect),

            // Stack
            NESInstruction.PHA_impl => (Opcode.PHA, AddressMode.Implied),
            NESInstruction.PHP_impl => (Opcode.PHP, AddressMode.Implied),
            NESInstruction.PLA_impl => (Opcode.PLA, AddressMode.Implied),
            NESInstruction.PLP_impl => (Opcode.PLP, AddressMode.Implied),

            // ROL
            NESInstruction.ROL_A => (Opcode.ROL, AddressMode.Accumulator),
            NESInstruction.ROL_zpg => (Opcode.ROL, AddressMode.ZeroPage),
            NESInstruction.ROL_abs => (Opcode.ROL, AddressMode.Absolute),

            // ROR
            NESInstruction.ROR_A => (Opcode.ROR, AddressMode.Accumulator),
            NESInstruction.ROR_zpg => (Opcode.ROR, AddressMode.ZeroPage),
            NESInstruction.ROR_abs => (Opcode.ROR, AddressMode.Absolute),
            NESInstruction.ROR_X_zpg => (Opcode.ROR, AddressMode.ZeroPageX),
            NESInstruction.ROR_X_abs => (Opcode.ROR, AddressMode.AbsoluteX),

            // RTI, RTS
            NESInstruction.RTI_impl => (Opcode.RTI, AddressMode.Implied),
            NESInstruction.RTS_impl => (Opcode.RTS, AddressMode.Implied),

            // SBC
            NESInstruction.SBC => (Opcode.SBC, AddressMode.Immediate),

            // Set flags
            NESInstruction.SEC_impl => (Opcode.SEC, AddressMode.Implied),
            NESInstruction.SEI_impl => (Opcode.SEI, AddressMode.Implied),

            // STA
            NESInstruction.STA_zpg => (Opcode.STA, AddressMode.ZeroPage),
            NESInstruction.STA_zpg_X => (Opcode.STA, AddressMode.ZeroPageX),
            NESInstruction.STA_abs => (Opcode.STA, AddressMode.Absolute),
            NESInstruction.STA_abs_X => (Opcode.STA, AddressMode.AbsoluteX),
            NESInstruction.STA_abs_Y => (Opcode.STA, AddressMode.AbsoluteY),
            NESInstruction.STA_X_ind => (Opcode.STA, AddressMode.IndexedIndirect),
            NESInstruction.STA_ind_Y => (Opcode.STA, AddressMode.IndirectIndexed),

            // STX
            NESInstruction.STX_zpg => (Opcode.STX, AddressMode.ZeroPage),
            NESInstruction.STX_abs => (Opcode.STX, AddressMode.Absolute),

            // STY
            NESInstruction.STY_zpg => (Opcode.STY, AddressMode.ZeroPage),
            NESInstruction.STY_abs => (Opcode.STY, AddressMode.Absolute),

            // Transfers
            NESInstruction.TAX_impl => (Opcode.TAX, AddressMode.Implied),
            NESInstruction.TAY_impl => (Opcode.TAY, AddressMode.Implied),
            NESInstruction.TXA_impl => (Opcode.TXA, AddressMode.Implied),
            NESInstruction.TXS_impl => (Opcode.TXS, AddressMode.Implied),
            NESInstruction.TYA_impl => (Opcode.TYA, AddressMode.Implied),

            _ => throw new ArgumentException($"Unknown NESInstruction: {i}", nameof(i))
        };
    }
}
