namespace dotnes;

/// <summary>
/// List of NES 6502 assembly instructions
/// 
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
enum NESInstruction : byte
{
    // 0

    /// <summary>
    /// Force Break
    /// </summary>
    BRK       = 0x00,
    /// <summary>
    /// OR Memory with Accumulator
    /// </summary>
    ORA_X_ind = 0x01,
    /// <summary>
    /// OR Memory with Accumulator
    /// </summary>
    ORA_zpg   = 0x05,
    /// <summary>
    /// Shift Left One Bit (Memory or Accumulator)
    /// </summary>
    ASL_zpg   = 0x06,
    /// <summary>
    /// Push Processor Status on Stack
    /// </summary>
    PHP_impl  = 0x08,
    /// <summary>
    /// OR Memory with Accumulator
    /// </summary>
    ORA       = 0x09,
    /// <summary>
    /// Shift Left One Bit (Memory or Accumulator)
    /// </summary>
    ASL_A     = 0x0A,
    /// <summary>
    /// OR Memory with Accumulator
    /// </summary>
    ORA_abs   = 0x0D,
    /// <summary>
    /// Shift Left One Bit (Memory or Accumulator)
    /// </summary>
    ASL_abs   = 0x0E,

    //TODO: 1
    /// <summary>
    /// Branch on Result Plus
    /// </summary>
    BPL       = 0x10,
    /// <summary>
    /// Clear Carry Flag
    /// </summary>
    CLC       = 0x18,

    // 2
    /// <summary>
    /// 20: Jump to New Location Saving Return Address
    /// </summary>
    JSR       = 0x20,
    /// <summary>
    /// AND Memory with Accumulator
    /// </summary>
    AND_X_ind = 0x21,
    /// <summary>
    /// Test Bits in Memory with Accumulator
    /// </summary>
    BIT_zpg   = 0x24,
    /// <summary>
    /// AND Memory with Accumulator
    /// </summary>
    AND_zpg   = 0x25,
    /// <summary>
    /// Rotate One Bit Left (Memory or Accumulator)
    /// </summary>
    ROL_zpg   = 0x26,
    /// <summary>
    /// Pull Processor Status from Stack
    /// </summary>
    PLP_impl  = 0x28,
    /// <summary>
    /// AND Memory with Accumulator
    /// </summary>
    AND       = 0x29,
    /// <summary>
    /// Rotate One Bit Left (Memory or Accumulator)
    /// </summary>
    ROL_A     = 0x2A,
    /// <summary>
    /// Test Bits in Memory with Accumulator
    /// </summary>
    BIT_abs   = 0x2C,
    /// <summary>
    /// AND Memory with Accumulator
    /// </summary>
    AND_abs   = 0x2D,
    /// <summary>
    /// Rotate One Bit Left (Memory or Accumulator)
    /// </summary>
    ROL_abs   = 0x2E,

    // 3

    /// <summary>
    /// Branch on Result Minus
    /// </summary>
    BMI       = 0x30,
    /// <summary>
    /// Set Carry Flag
    /// </summary>
    SEC_impl  = 0x38,
    //TODO: rest of 3

    /// <summary>
    /// Return from Interrupt
    /// </summary>
    RTI_impl  = 0x40,
    /// <summary>
    /// Jump to New Location
    /// </summary>
    JMP_abs   = 0x4C,
    /// <summary>
    /// Push Accumulator on Stack
    /// </summary>
    PHA_impl  = 0x48,
    //TODO: 4-5

    // 6

    /// <summary>
    /// Return from Subroutine
    /// </summary>
    RTS_impl  = 0x60,
    /// <summary>
    /// Add Memory to Accumulator with Carry
    /// </summary>
    ADC_X_ind = 0x61,
    /// <summary>
    /// Add Memory to Accumulator with Carry
    /// </summary>
    ADC_X_zpg = 0x65,
    /// <summary>
    /// Rotate One Bit Right (Memory or Accumulator)
    /// </summary>
    ROR_zpg   = 0x66,
    /// <summary>
    /// Pull Accumulator from Stack
    /// </summary>
    PLA_impl  = 0x68,
    /// <summary>
    /// Add Memory to Accumulator with Carry
    /// </summary>
    ADC       = 0x69,
    /// <summary>
    /// Rotate One Bit Right (Memory or Accumulator)
    /// </summary>
    ROR_A     = 0x6A,
    /// <summary>
    /// Jump to New Location
    /// </summary>
    JMP_ind   = 0x6C,
    /// <summary>
    /// Add Memory to Accumulator with Carry
    /// </summary>
    ADC_abs   = 0x6D,
    /// <summary>
    /// Rotate One Bit Right (Memory or Accumulator)
    /// </summary>
    ROR_abs   = 0x6E,

    //TODO: 7
    /// <summary>
    /// Set Interrupt Disable Status
    /// </summary>
    SEI_impl  = 0x78,

    /// <summary>
    /// Store Accumulator in Memory
    /// </summary>
    STA_X_ind = 0x81,
    /// <summary>
    /// Store Index Y in Memory
    /// </summary>
    STY_zpg   = 0x84,
    /// <summary>
    /// Store Accumulator in Memory
    /// </summary>
    STA_zpg   = 0x85,
    /// <summary>
    /// Store Index X in Memory
    /// </summary>
    STX_zpg   = 0x86,
    /// <summary>
    /// Decrement Index Y by One
    /// </summary>
    DEY_impl  = 0x88,
    /// <summary>
    /// Transfer Index X to Accumulator
    /// </summary>
    TXA_impl  = 0x8A,
    /// <summary>
    /// Store Index Y in Memory
    /// </summary>
    STY_abs   = 0x8C,
    /// <summary>
    /// Store Accumulator in Memory
    /// </summary>
    STA_abs   = 0x8D,
    /// <summary>
    /// Store Index X in Memory
    /// </summary>
    STX_abs   = 0x8E,

    //TODO: 9

    /// <summary>
    /// Branch on Carry Clear
    /// </summary>
    BCC       = 0x90,
    /// <summary>
    /// 91: Store Accumulator in Memory
    /// </summary>
    STA_ind_Y = 0x91,
    /// <summary>
    /// Store Accumulator in Memory
    /// </summary>
    STA_zpg_X = 0x95,
    /// <summary>
    /// Transfer Index Y to Accumulator
    /// </summary>
    TYA_impl  = 0x98,
    //TODO: rest of 9
    /// <summary>
    /// 9D: Store Accumulator in Memory
    /// </summary>
    STA_abs_X = 0x9D,
    /// <summary>
    /// Transfer Index X to Stack Register
    /// </summary>
    TXS_impl = 0x9A,

    // A

    /// <summary>
    /// A0: Load Index Y with Memory
    /// </summary>
    LDY       = 0xA0,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_X_ind = 0xA1,
    /// <summary>
    /// A2: Load Index X with Memory
    /// </summary>
    LDX       = 0xA2,
    /// <summary>
    /// Load Index Y with Memory
    /// </summary>
    LDY_zpg   = 0xA4,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_zpg   = 0xA5,
    /// <summary>
    /// Load Index X with Memory
    /// </summary>
    LDX_zpg   = 0xA6,
    /// <summary>
    /// Transfer Accumulator to Index Y
    /// </summary>
    TAY_impl  = 0xA8,
    /// <summary>
    /// A9: Load Accumulator with Memory
    /// </summary>
    LDA       = 0xA9,
    /// <summary>
    /// Transfer Accumulator to Index X
    /// </summary>
    TAX_impl  = 0xAA,
    /// <summary>
    /// Load Index Y with Memory
    /// </summary>
    LDY_abs   = 0xAC,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_abs   = 0xAD,
    /// <summary>
    /// Load Index X with Memory
    /// </summary>
    LDX_abs   = 0xAE,

    //TODO: B

    /// <summary>
    /// Branch on Carry Set
    /// </summary>
    BCS = 0xB0,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_ind_Y = 0xB1,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_abs_y =0xB9,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_abs_X = 0xBD,

    //TODO:C
    /// <summary>
    /// Compare Memory and Index Y
    /// </summary>
    CPY       = 0xC0,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP_zpg   = 0xC5,
    /// <summary>
    /// Decrement Memory by One
    /// </summary>
    DEC_zpg   = 0xC6,
    // 0xC7 is empty
    /// <summary>
    /// Increment Index Y by One
    /// </summary>
    INY_impl  = 0xC8,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP       = 0xC9,
    /// <summary>
    /// Decrement Index X by One
    /// </summary>
    DEX_impl  = 0xCA,
    /// <summary>
    /// Decrement Memory by One
    /// </summary>
    DEC_abs   = 0xCE,

    /// <summary>
    /// Branch on Result not Zero
    /// </summary>
    BNE_rel   = 0xD0,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP_ind_Y = 0xD1,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP_zpg_X = 0xD5,
    /// <summary>
    /// Decrement Memory by One
    /// </summary>
    DEC_zpg_X = 0xD6,
    /// <summary>
    /// Clear Decimal Mode
    /// </summary>
    CLD_impl  = 0xD8,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP_abs_Y = 0xD9,
    /// <summary>
    /// Compare Memory with Accumulator
    /// </summary>
    CMP_abs_X = 0xDD,
    /// <summary>
    /// Decrement Memory by One
    /// </summary>
    DEC_abs_X = 0xDE,

    /// <summary>
    /// Compare Memory and Index X
    /// </summary>
    CPX = 0xE0,
    /// <summary>
    /// Increment Memory by One
    /// </summary>
    INC_zpg   = 0xE6,
    // 0xE7 is empty
    /// <summary>
    /// Increment Index X by One
    /// </summary>
    INX_impl  = 0xE8,
    /// <summary>
    /// Subtract Memory from Accumulator with Borrow
    /// </summary>
    SBC       = 0xE9,
    /// <summary>
    /// Increment Memory by One
    /// </summary>
    INC_abs   = 0xEE,
    //TODO: rest of E

    //TODO: F

    /// <summary>
    /// Branch on Result Zero
    /// </summary>
    BEQ_rel   = 0xF0,
}
