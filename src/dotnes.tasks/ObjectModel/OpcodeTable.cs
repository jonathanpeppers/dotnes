namespace dotnes.ObjectModel;

/// <summary>
/// Maps (Opcode, AddressMode) pairs to their byte encoding and vice versa.
/// See: https://www.masswerk.at/6502/6502_instruction_set.html
/// </summary>
public static class OpcodeTable
{
    private static readonly Dictionary<(Opcode, AddressMode), byte> _encodings = new()
    {
        // ADC - Add with Carry
        { (Opcode.ADC, AddressMode.Immediate),       0x69 },
        { (Opcode.ADC, AddressMode.ZeroPage),        0x65 },
        { (Opcode.ADC, AddressMode.ZeroPageX),       0x75 },
        { (Opcode.ADC, AddressMode.Absolute),        0x6D },
        { (Opcode.ADC, AddressMode.AbsoluteX),       0x7D },
        { (Opcode.ADC, AddressMode.AbsoluteY),       0x79 },
        { (Opcode.ADC, AddressMode.IndexedIndirect), 0x61 },
        { (Opcode.ADC, AddressMode.IndirectIndexed), 0x71 },

        // AND - Logical AND
        { (Opcode.AND, AddressMode.Immediate),       0x29 },
        { (Opcode.AND, AddressMode.ZeroPage),        0x25 },
        { (Opcode.AND, AddressMode.ZeroPageX),       0x35 },
        { (Opcode.AND, AddressMode.Absolute),        0x2D },
        { (Opcode.AND, AddressMode.AbsoluteX),       0x3D },
        { (Opcode.AND, AddressMode.AbsoluteY),       0x39 },
        { (Opcode.AND, AddressMode.IndexedIndirect), 0x21 },
        { (Opcode.AND, AddressMode.IndirectIndexed), 0x31 },

        // ASL - Arithmetic Shift Left
        { (Opcode.ASL, AddressMode.Accumulator),     0x0A },
        { (Opcode.ASL, AddressMode.ZeroPage),        0x06 },
        { (Opcode.ASL, AddressMode.ZeroPageX),       0x16 },
        { (Opcode.ASL, AddressMode.Absolute),        0x0E },
        { (Opcode.ASL, AddressMode.AbsoluteX),       0x1E },

        // BCC - Branch on Carry Clear
        { (Opcode.BCC, AddressMode.Relative),        0x90 },

        // BCS - Branch on Carry Set
        { (Opcode.BCS, AddressMode.Relative),        0xB0 },

        // BEQ - Branch on Equal (Zero Set)
        { (Opcode.BEQ, AddressMode.Relative),        0xF0 },

        // BIT - Bit Test
        { (Opcode.BIT, AddressMode.ZeroPage),        0x24 },
        { (Opcode.BIT, AddressMode.Absolute),        0x2C },

        // BMI - Branch on Minus
        { (Opcode.BMI, AddressMode.Relative),        0x30 },

        // BNE - Branch on Not Equal
        { (Opcode.BNE, AddressMode.Relative),        0xD0 },

        // BPL - Branch on Plus
        { (Opcode.BPL, AddressMode.Relative),        0x10 },

        // BRK - Break
        { (Opcode.BRK, AddressMode.Implied),         0x00 },

        // BVC - Branch on Overflow Clear
        { (Opcode.BVC, AddressMode.Relative),        0x50 },

        // BVS - Branch on Overflow Set
        { (Opcode.BVS, AddressMode.Relative),        0x70 },

        // CLC - Clear Carry
        { (Opcode.CLC, AddressMode.Implied),         0x18 },

        // CLD - Clear Decimal
        { (Opcode.CLD, AddressMode.Implied),         0xD8 },

        // CLI - Clear Interrupt Disable
        { (Opcode.CLI, AddressMode.Implied),         0x58 },

        // CLV - Clear Overflow
        { (Opcode.CLV, AddressMode.Implied),         0xB8 },

        // CMP - Compare
        { (Opcode.CMP, AddressMode.Immediate),       0xC9 },
        { (Opcode.CMP, AddressMode.ZeroPage),        0xC5 },
        { (Opcode.CMP, AddressMode.ZeroPageX),       0xD5 },
        { (Opcode.CMP, AddressMode.Absolute),        0xCD },
        { (Opcode.CMP, AddressMode.AbsoluteX),       0xDD },
        { (Opcode.CMP, AddressMode.AbsoluteY),       0xD9 },
        { (Opcode.CMP, AddressMode.IndexedIndirect), 0xC1 },
        { (Opcode.CMP, AddressMode.IndirectIndexed), 0xD1 },

        // CPX - Compare X
        { (Opcode.CPX, AddressMode.Immediate),       0xE0 },
        { (Opcode.CPX, AddressMode.ZeroPage),        0xE4 },
        { (Opcode.CPX, AddressMode.Absolute),        0xEC },

        // CPY - Compare Y
        { (Opcode.CPY, AddressMode.Immediate),       0xC0 },
        { (Opcode.CPY, AddressMode.ZeroPage),        0xC4 },
        { (Opcode.CPY, AddressMode.Absolute),        0xCC },

        // DEC - Decrement Memory
        { (Opcode.DEC, AddressMode.ZeroPage),        0xC6 },
        { (Opcode.DEC, AddressMode.ZeroPageX),       0xD6 },
        { (Opcode.DEC, AddressMode.Absolute),        0xCE },
        { (Opcode.DEC, AddressMode.AbsoluteX),       0xDE },

        // DEX - Decrement X
        { (Opcode.DEX, AddressMode.Implied),         0xCA },

        // DEY - Decrement Y
        { (Opcode.DEY, AddressMode.Implied),         0x88 },

        // EOR - Exclusive OR
        { (Opcode.EOR, AddressMode.Immediate),       0x49 },
        { (Opcode.EOR, AddressMode.ZeroPage),        0x45 },
        { (Opcode.EOR, AddressMode.ZeroPageX),       0x55 },
        { (Opcode.EOR, AddressMode.Absolute),        0x4D },
        { (Opcode.EOR, AddressMode.AbsoluteX),       0x5D },
        { (Opcode.EOR, AddressMode.AbsoluteY),       0x59 },
        { (Opcode.EOR, AddressMode.IndexedIndirect), 0x41 },
        { (Opcode.EOR, AddressMode.IndirectIndexed), 0x51 },

        // INC - Increment Memory
        { (Opcode.INC, AddressMode.ZeroPage),        0xE6 },
        { (Opcode.INC, AddressMode.ZeroPageX),       0xF6 },
        { (Opcode.INC, AddressMode.Absolute),        0xEE },
        { (Opcode.INC, AddressMode.AbsoluteX),       0xFE },

        // INX - Increment X
        { (Opcode.INX, AddressMode.Implied),         0xE8 },

        // INY - Increment Y
        { (Opcode.INY, AddressMode.Implied),         0xC8 },

        // JMP - Jump
        { (Opcode.JMP, AddressMode.Absolute),        0x4C },
        { (Opcode.JMP, AddressMode.Indirect),        0x6C },

        // JSR - Jump to Subroutine
        { (Opcode.JSR, AddressMode.Absolute),        0x20 },

        // LDA - Load Accumulator
        { (Opcode.LDA, AddressMode.Immediate),       0xA9 },
        { (Opcode.LDA, AddressMode.ZeroPage),        0xA5 },
        { (Opcode.LDA, AddressMode.ZeroPageX),       0xB5 },
        { (Opcode.LDA, AddressMode.Absolute),        0xAD },
        { (Opcode.LDA, AddressMode.AbsoluteX),       0xBD },
        { (Opcode.LDA, AddressMode.AbsoluteY),       0xB9 },
        { (Opcode.LDA, AddressMode.IndexedIndirect), 0xA1 },
        { (Opcode.LDA, AddressMode.IndirectIndexed), 0xB1 },

        // LDX - Load X
        { (Opcode.LDX, AddressMode.Immediate),       0xA2 },
        { (Opcode.LDX, AddressMode.ZeroPage),        0xA6 },
        { (Opcode.LDX, AddressMode.ZeroPageY),       0xB6 },
        { (Opcode.LDX, AddressMode.Absolute),        0xAE },
        { (Opcode.LDX, AddressMode.AbsoluteY),       0xBE },

        // LDY - Load Y
        { (Opcode.LDY, AddressMode.Immediate),       0xA0 },
        { (Opcode.LDY, AddressMode.ZeroPage),        0xA4 },
        { (Opcode.LDY, AddressMode.ZeroPageX),       0xB4 },
        { (Opcode.LDY, AddressMode.Absolute),        0xAC },
        { (Opcode.LDY, AddressMode.AbsoluteX),       0xBC },

        // LSR - Logical Shift Right
        { (Opcode.LSR, AddressMode.Accumulator),     0x4A },
        { (Opcode.LSR, AddressMode.ZeroPage),        0x46 },
        { (Opcode.LSR, AddressMode.ZeroPageX),       0x56 },
        { (Opcode.LSR, AddressMode.Absolute),        0x4E },
        { (Opcode.LSR, AddressMode.AbsoluteX),       0x5E },

        // NOP - No Operation
        { (Opcode.NOP, AddressMode.Implied),         0xEA },

        // ORA - Logical OR
        { (Opcode.ORA, AddressMode.Immediate),       0x09 },
        { (Opcode.ORA, AddressMode.ZeroPage),        0x05 },
        { (Opcode.ORA, AddressMode.ZeroPageX),       0x15 },
        { (Opcode.ORA, AddressMode.Absolute),        0x0D },
        { (Opcode.ORA, AddressMode.AbsoluteX),       0x1D },
        { (Opcode.ORA, AddressMode.AbsoluteY),       0x19 },
        { (Opcode.ORA, AddressMode.IndexedIndirect), 0x01 },
        { (Opcode.ORA, AddressMode.IndirectIndexed), 0x11 },

        // PHA - Push Accumulator
        { (Opcode.PHA, AddressMode.Implied),         0x48 },

        // PHP - Push Processor Status
        { (Opcode.PHP, AddressMode.Implied),         0x08 },

        // PLA - Pull Accumulator
        { (Opcode.PLA, AddressMode.Implied),         0x68 },

        // PLP - Pull Processor Status
        { (Opcode.PLP, AddressMode.Implied),         0x28 },

        // ROL - Rotate Left
        { (Opcode.ROL, AddressMode.Accumulator),     0x2A },
        { (Opcode.ROL, AddressMode.ZeroPage),        0x26 },
        { (Opcode.ROL, AddressMode.ZeroPageX),       0x36 },
        { (Opcode.ROL, AddressMode.Absolute),        0x2E },
        { (Opcode.ROL, AddressMode.AbsoluteX),       0x3E },

        // ROR - Rotate Right
        { (Opcode.ROR, AddressMode.Accumulator),     0x6A },
        { (Opcode.ROR, AddressMode.ZeroPage),        0x66 },
        { (Opcode.ROR, AddressMode.ZeroPageX),       0x76 },
        { (Opcode.ROR, AddressMode.Absolute),        0x6E },
        { (Opcode.ROR, AddressMode.AbsoluteX),       0x7E },

        // RTI - Return from Interrupt
        { (Opcode.RTI, AddressMode.Implied),         0x40 },

        // RTS - Return from Subroutine
        { (Opcode.RTS, AddressMode.Implied),         0x60 },

        // SBC - Subtract with Carry
        { (Opcode.SBC, AddressMode.Immediate),       0xE9 },
        { (Opcode.SBC, AddressMode.ZeroPage),        0xE5 },
        { (Opcode.SBC, AddressMode.ZeroPageX),       0xF5 },
        { (Opcode.SBC, AddressMode.Absolute),        0xED },
        { (Opcode.SBC, AddressMode.AbsoluteX),       0xFD },
        { (Opcode.SBC, AddressMode.AbsoluteY),       0xF9 },
        { (Opcode.SBC, AddressMode.IndexedIndirect), 0xE1 },
        { (Opcode.SBC, AddressMode.IndirectIndexed), 0xF1 },

        // SEC - Set Carry
        { (Opcode.SEC, AddressMode.Implied),         0x38 },

        // SED - Set Decimal
        { (Opcode.SED, AddressMode.Implied),         0xF8 },

        // SEI - Set Interrupt Disable
        { (Opcode.SEI, AddressMode.Implied),         0x78 },

        // STA - Store Accumulator
        { (Opcode.STA, AddressMode.ZeroPage),        0x85 },
        { (Opcode.STA, AddressMode.ZeroPageX),       0x95 },
        { (Opcode.STA, AddressMode.Absolute),        0x8D },
        { (Opcode.STA, AddressMode.AbsoluteX),       0x9D },
        { (Opcode.STA, AddressMode.AbsoluteY),       0x99 },
        { (Opcode.STA, AddressMode.IndexedIndirect), 0x81 },
        { (Opcode.STA, AddressMode.IndirectIndexed), 0x91 },

        // STX - Store X
        { (Opcode.STX, AddressMode.ZeroPage),        0x86 },
        { (Opcode.STX, AddressMode.ZeroPageY),       0x96 },
        { (Opcode.STX, AddressMode.Absolute),        0x8E },

        // STY - Store Y
        { (Opcode.STY, AddressMode.ZeroPage),        0x84 },
        { (Opcode.STY, AddressMode.ZeroPageX),       0x94 },
        { (Opcode.STY, AddressMode.Absolute),        0x8C },

        // TAX - Transfer A to X
        { (Opcode.TAX, AddressMode.Implied),         0xAA },

        // TAY - Transfer A to Y
        { (Opcode.TAY, AddressMode.Implied),         0xA8 },

        // TSX - Transfer SP to X
        { (Opcode.TSX, AddressMode.Implied),         0xBA },

        // TXA - Transfer X to A
        { (Opcode.TXA, AddressMode.Implied),         0x8A },

        // TXS - Transfer X to SP
        { (Opcode.TXS, AddressMode.Implied),         0x9A },

        // TYA - Transfer Y to A
        { (Opcode.TYA, AddressMode.Implied),         0x98 },
    };

    // Reverse lookup table built lazily
    private static Dictionary<byte, (Opcode, AddressMode)>? _decodings;

    /// <summary>
    /// Encodes an opcode and addressing mode to its byte representation
    /// </summary>
    public static byte Encode(Opcode opcode, AddressMode mode)
    {
        // Map label-based immediate modes to standard Immediate for encoding
        var effectiveMode = mode switch
        {
            AddressMode.Immediate_LowByte => AddressMode.Immediate,
            AddressMode.Immediate_HighByte => AddressMode.Immediate,
            _ => mode
        };
        
        if (_encodings.TryGetValue((opcode, effectiveMode), out byte encoding))
            return encoding;
        throw new InvalidOpcodeAddressModeException(opcode, mode);
    }

    /// <summary>
    /// Tries to encode an opcode and addressing mode
    /// </summary>
    public static bool TryEncode(Opcode opcode, AddressMode mode, out byte encoding)
    {
        return _encodings.TryGetValue((opcode, mode), out encoding);
    }

    /// <summary>
    /// Decodes a byte to its opcode and addressing mode
    /// </summary>
    public static (Opcode Opcode, AddressMode Mode) Decode(byte encoding)
    {
        EnsureDecodingsBuilt();
        if (_decodings!.TryGetValue(encoding, out var result))
            return result;
        throw new UnknownOpcodeException(encoding);
    }

    /// <summary>
    /// Tries to decode a byte to its opcode and addressing mode
    /// </summary>
    public static bool TryDecode(byte encoding, out Opcode opcode, out AddressMode mode)
    {
        EnsureDecodingsBuilt();
        if (_decodings!.TryGetValue(encoding, out var result))
        {
            opcode = result.Item1;
            mode = result.Item2;
            return true;
        }
        opcode = default;
        mode = default;
        return false;
    }

    /// <summary>
    /// Gets the size of an instruction in bytes given its addressing mode
    /// </summary>
    public static int GetInstructionSize(AddressMode mode)
    {
        return mode switch
        {
            AddressMode.Implied => 1,
            AddressMode.Accumulator => 1,
            AddressMode.Immediate => 2,
            AddressMode.ZeroPage => 2,
            AddressMode.ZeroPageX => 2,
            AddressMode.ZeroPageY => 2,
            AddressMode.Relative => 2,
            AddressMode.IndexedIndirect => 2,
            AddressMode.IndirectIndexed => 2,
            AddressMode.Absolute => 3,
            AddressMode.AbsoluteX => 3,
            AddressMode.AbsoluteY => 3,
            AddressMode.Indirect => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Checks if an opcode/mode combination is valid
    /// </summary>
    public static bool IsValid(Opcode opcode, AddressMode mode)
    {
        return _encodings.ContainsKey((opcode, mode));
    }

    private static void EnsureDecodingsBuilt()
    {
        if (_decodings != null) return;

        _decodings = new Dictionary<byte, (Opcode, AddressMode)>();
        foreach (var kvp in _encodings)
        {
            _decodings[kvp.Value] = kvp.Key;
        }
    }
}
