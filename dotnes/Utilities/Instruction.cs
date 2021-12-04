namespace dotnes;

/// <summary>
/// List of NES 6502 assembly instructions
/// 
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
enum Instruction : byte
{
    // 0
    BRK       = 0x00,
    ORA_X_ind = 0x01,
    ORA_zpg   = 0x05,
    ASL_zpg   = 0x06,
    PHP_impl  = 0x08,
    ORA       = 0x09,
    ASL_A     = 0x0A,
    ORA_abs   = 0x0D,
    ASL_abs   = 0x0E,

    //TODO: 1-5

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

    //TODO: 7-9

    // A
    LDY       = 0xA0,
    LDA_X_ind = 0xA1,
    LDX       = 0xA2,
    LDY_zpg   = 0xA4,
    LDA_zpg   = 0xA5,
    LDX_zpg   = 0xA6,
    TAY_impl  = 0xA8,
    LDA       = 0xA9,
    TAX_impl  = 0xAA,
    LDY_abs   = 0xAC,
    LDA_abs   = 0xAD,
    LDX_abs   = 0xAE,
}
