namespace dotnes;

/// <summary>
/// List of NES 6502 assembly instructions
/// 
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
enum Instruction : byte
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

    // 2
    /// <summary>
    /// Jump to New Location Saving Return Address
    /// </summary>
    JSR       = 0x20,

    // 3

    /// <summary>
    /// Branch on Result Minus
    /// </summary>
    BMI       = 0x30,
    //TODO: rest of 3

    //TODO: 4-5

    // 6
    RTS_impl  = 0x60,
    ADC_X_ind = 0x61,
    ADC_X_zpg = 0x65,
    ROR_zpg   = 0x66,
    PLA_impl  = 0x68,
    ADC       = 0x69,
    ROR_A     = 0x6A,
    JMP_ind   = 0x6C,
    ADC_abs   = 0x6D,
    ROR_abs   = 0x6E,

    //TODO: 7

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
    STA_abs   = 0x8C,
    /// <summary>
    /// Store Index X in Memory
    /// </summary>
    STX_abs   = 0x8C,

    //TODO: 9

    // A

    /// <summary>
    /// Load Index Y with Memory
    /// </summary>
    LDY       = 0xA0,
    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    LDA_X_ind = 0xA1,
    /// <summary>
    /// Load Index X with Memory
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
    /// Load Accumulator with Memory
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
}
