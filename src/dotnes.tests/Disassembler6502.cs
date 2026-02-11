using System.Text;

namespace dotnes.tests;

/// <summary>
/// Simple 6502 disassembler for debugging NES ROMs in test output.
/// </summary>
public static class Disassembler6502
{
    // 6502 opcode table: opcode -> (mnemonic, addressing mode, byte count)
    static readonly Dictionary<byte, (string Mnemonic, string Mode, int Bytes)> Opcodes = new()
    {
        // ADC
        [0x69] = ("ADC", "imm", 2),
        [0x65] = ("ADC", "zp", 2),
        [0x75] = ("ADC", "zp,X", 2),
        [0x6D] = ("ADC", "abs", 3),
        [0x7D] = ("ADC", "abs,X", 3),
        [0x79] = ("ADC", "abs,Y", 3),
        [0x61] = ("ADC", "(zp,X)", 2),
        [0x71] = ("ADC", "(zp),Y", 2),
        
        // AND
        [0x29] = ("AND", "imm", 2),
        [0x25] = ("AND", "zp", 2),
        [0x35] = ("AND", "zp,X", 2),
        [0x2D] = ("AND", "abs", 3),
        [0x3D] = ("AND", "abs,X", 3),
        [0x39] = ("AND", "abs,Y", 3),
        [0x21] = ("AND", "(zp,X)", 2),
        [0x31] = ("AND", "(zp),Y", 2),
        
        // ASL
        [0x0A] = ("ASL", "A", 1),
        [0x06] = ("ASL", "zp", 2),
        [0x16] = ("ASL", "zp,X", 2),
        [0x0E] = ("ASL", "abs", 3),
        [0x1E] = ("ASL", "abs,X", 3),
        
        // Branch instructions
        [0x90] = ("BCC", "rel", 2),
        [0xB0] = ("BCS", "rel", 2),
        [0xF0] = ("BEQ", "rel", 2),
        [0x30] = ("BMI", "rel", 2),
        [0xD0] = ("BNE", "rel", 2),
        [0x10] = ("BPL", "rel", 2),
        [0x50] = ("BVC", "rel", 2),
        [0x70] = ("BVS", "rel", 2),
        
        // BIT
        [0x24] = ("BIT", "zp", 2),
        [0x2C] = ("BIT", "abs", 3),
        
        // BRK
        [0x00] = ("BRK", "", 1),
        
        // Clear flags
        [0x18] = ("CLC", "", 1),
        [0xD8] = ("CLD", "", 1),
        [0x58] = ("CLI", "", 1),
        [0xB8] = ("CLV", "", 1),
        
        // CMP
        [0xC9] = ("CMP", "imm", 2),
        [0xC5] = ("CMP", "zp", 2),
        [0xD5] = ("CMP", "zp,X", 2),
        [0xCD] = ("CMP", "abs", 3),
        [0xDD] = ("CMP", "abs,X", 3),
        [0xD9] = ("CMP", "abs,Y", 3),
        [0xC1] = ("CMP", "(zp,X)", 2),
        [0xD1] = ("CMP", "(zp),Y", 2),
        
        // CPX
        [0xE0] = ("CPX", "imm", 2),
        [0xE4] = ("CPX", "zp", 2),
        [0xEC] = ("CPX", "abs", 3),
        
        // CPY
        [0xC0] = ("CPY", "imm", 2),
        [0xC4] = ("CPY", "zp", 2),
        [0xCC] = ("CPY", "abs", 3),
        
        // DEC
        [0xC6] = ("DEC", "zp", 2),
        [0xD6] = ("DEC", "zp,X", 2),
        [0xCE] = ("DEC", "abs", 3),
        [0xDE] = ("DEC", "abs,X", 3),
        
        // DEX, DEY
        [0xCA] = ("DEX", "", 1),
        [0x88] = ("DEY", "", 1),
        
        // EOR
        [0x49] = ("EOR", "imm", 2),
        [0x45] = ("EOR", "zp", 2),
        [0x55] = ("EOR", "zp,X", 2),
        [0x4D] = ("EOR", "abs", 3),
        [0x5D] = ("EOR", "abs,X", 3),
        [0x59] = ("EOR", "abs,Y", 3),
        [0x41] = ("EOR", "(zp,X)", 2),
        [0x51] = ("EOR", "(zp),Y", 2),
        
        // INC
        [0xE6] = ("INC", "zp", 2),
        [0xF6] = ("INC", "zp,X", 2),
        [0xEE] = ("INC", "abs", 3),
        [0xFE] = ("INC", "abs,X", 3),
        
        // INX, INY
        [0xE8] = ("INX", "", 1),
        [0xC8] = ("INY", "", 1),
        
        // JMP
        [0x4C] = ("JMP", "abs", 3),
        [0x6C] = ("JMP", "(abs)", 3),
        
        // JSR
        [0x20] = ("JSR", "abs", 3),
        
        // LDA
        [0xA9] = ("LDA", "imm", 2),
        [0xA5] = ("LDA", "zp", 2),
        [0xB5] = ("LDA", "zp,X", 2),
        [0xAD] = ("LDA", "abs", 3),
        [0xBD] = ("LDA", "abs,X", 3),
        [0xB9] = ("LDA", "abs,Y", 3),
        [0xA1] = ("LDA", "(zp,X)", 2),
        [0xB1] = ("LDA", "(zp),Y", 2),
        
        // LDX
        [0xA2] = ("LDX", "imm", 2),
        [0xA6] = ("LDX", "zp", 2),
        [0xB6] = ("LDX", "zp,Y", 2),
        [0xAE] = ("LDX", "abs", 3),
        [0xBE] = ("LDX", "abs,Y", 3),
        
        // LDY
        [0xA0] = ("LDY", "imm", 2),
        [0xA4] = ("LDY", "zp", 2),
        [0xB4] = ("LDY", "zp,X", 2),
        [0xAC] = ("LDY", "abs", 3),
        [0xBC] = ("LDY", "abs,X", 3),
        
        // LSR
        [0x4A] = ("LSR", "A", 1),
        [0x46] = ("LSR", "zp", 2),
        [0x56] = ("LSR", "zp,X", 2),
        [0x4E] = ("LSR", "abs", 3),
        [0x5E] = ("LSR", "abs,X", 3),
        
        // NOP
        [0xEA] = ("NOP", "", 1),
        
        // ORA
        [0x09] = ("ORA", "imm", 2),
        [0x05] = ("ORA", "zp", 2),
        [0x15] = ("ORA", "zp,X", 2),
        [0x0D] = ("ORA", "abs", 3),
        [0x1D] = ("ORA", "abs,X", 3),
        [0x19] = ("ORA", "abs,Y", 3),
        [0x01] = ("ORA", "(zp,X)", 2),
        [0x11] = ("ORA", "(zp),Y", 2),
        
        // Stack
        [0x48] = ("PHA", "", 1),
        [0x08] = ("PHP", "", 1),
        [0x68] = ("PLA", "", 1),
        [0x28] = ("PLP", "", 1),
        
        // ROL
        [0x2A] = ("ROL", "A", 1),
        [0x26] = ("ROL", "zp", 2),
        [0x36] = ("ROL", "zp,X", 2),
        [0x2E] = ("ROL", "abs", 3),
        [0x3E] = ("ROL", "abs,X", 3),
        
        // ROR
        [0x6A] = ("ROR", "A", 1),
        [0x66] = ("ROR", "zp", 2),
        [0x76] = ("ROR", "zp,X", 2),
        [0x6E] = ("ROR", "abs", 3),
        [0x7E] = ("ROR", "abs,X", 3),
        
        // RTI, RTS
        [0x40] = ("RTI", "", 1),
        [0x60] = ("RTS", "", 1),
        
        // SBC
        [0xE9] = ("SBC", "imm", 2),
        [0xE5] = ("SBC", "zp", 2),
        [0xF5] = ("SBC", "zp,X", 2),
        [0xED] = ("SBC", "abs", 3),
        [0xFD] = ("SBC", "abs,X", 3),
        [0xF9] = ("SBC", "abs,Y", 3),
        [0xE1] = ("SBC", "(zp,X)", 2),
        [0xF1] = ("SBC", "(zp),Y", 2),
        
        // Set flags
        [0x38] = ("SEC", "", 1),
        [0xF8] = ("SED", "", 1),
        [0x78] = ("SEI", "", 1),
        
        // STA
        [0x85] = ("STA", "zp", 2),
        [0x95] = ("STA", "zp,X", 2),
        [0x8D] = ("STA", "abs", 3),
        [0x9D] = ("STA", "abs,X", 3),
        [0x99] = ("STA", "abs,Y", 3),
        [0x81] = ("STA", "(zp,X)", 2),
        [0x91] = ("STA", "(zp),Y", 2),
        
        // STX
        [0x86] = ("STX", "zp", 2),
        [0x96] = ("STX", "zp,Y", 2),
        [0x8E] = ("STX", "abs", 3),
        
        // STY
        [0x84] = ("STY", "zp", 2),
        [0x94] = ("STY", "zp,X", 2),
        [0x8C] = ("STY", "abs", 3),
        
        // Transfers
        [0xAA] = ("TAX", "", 1),
        [0xA8] = ("TAY", "", 1),
        [0xBA] = ("TSX", "", 1),
        [0x8A] = ("TXA", "", 1),
        [0x9A] = ("TXS", "", 1),
        [0x98] = ("TYA", "", 1),
    };

    /// <summary>
    /// Disassemble a NES ROM's main program section.
    /// </summary>
    public static string DisassembleRom(byte[] rom)
    {
        if (rom.Length < 16)
            return "Invalid ROM: too small";

        // Check NES header
        if (rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1A)
            return "Invalid ROM: not a NES file";

        var sb = new StringBuilder();
        
        // Header info
        int prgBanks = rom[4];
        int chrBanks = rom[5];
        sb.AppendLine($"; NES ROM - PRG banks: {prgBanks}, CHR banks: {chrBanks}");
        sb.AppendLine();

        // PRG ROM starts at offset 16 (after header)
        // For NROM (mapper 0), PRG is loaded at $8000 or $C000
        int prgStart = 16;
        int prgSize = prgBanks * 16384;
        
        // Find the main program - it typically starts after the reset vector setup
        // The main user code starts at around offset 0x0510 (address $8510)
        int mainOffset = 0x0510;
        ushort baseAddress = 0x8510;
        
        // Disassemble the main program area (first ~512 bytes should cover most samples)
        sb.AppendLine("; Main program:");
        int endOffset = Math.Min(mainOffset + 512, prgStart + prgSize);
        
        DisassembleRange(rom, mainOffset, endOffset, baseAddress, sb);
        
        return sb.ToString();
    }

    /// <summary>
    /// Disassemble a specific byte range.
    /// </summary>
    public static void DisassembleRange(byte[] data, int startOffset, int endOffset, ushort baseAddress, StringBuilder sb)
    {
        int offset = startOffset;
        ushort address = baseAddress;
        
        while (offset < endOffset && offset < data.Length)
        {
            byte opcode = data[offset];
            
            if (Opcodes.TryGetValue(opcode, out var info))
            {
                var (mnemonic, mode, bytes) = info;
                
                // Build the hex bytes string
                var hexBytes = new StringBuilder();
                for (int i = 0; i < bytes && offset + i < data.Length; i++)
                {
                    hexBytes.Append($"{data[offset + i]:X2} ");
                }
                
                // Format the operand based on addressing mode
                string operand = FormatOperand(mode, data, offset, bytes, address);
                
                sb.AppendLine($"${address:X4}: {hexBytes,-12} {mnemonic,-4} {operand}");
                
                offset += bytes;
                address += (ushort)bytes;
            }
            else
            {
                // Unknown opcode - show as data byte
                sb.AppendLine($"${address:X4}: {data[offset]:X2}          .byte ${data[offset]:X2}");
                offset++;
                address++;
            }
        }
    }
    
    static string FormatOperand(string mode, byte[] data, int offset, int bytes, ushort address)
    {
        if (bytes == 2 && offset + 1 < data.Length)
        {
            byte op1 = data[offset + 1];
            return mode switch
            {
                "imm" => $"#${op1:X2}",
                "zp" => $"${op1:X2}",
                "zp,X" => $"${op1:X2},X",
                "zp,Y" => $"${op1:X2},Y",
                "(zp,X)" => $"(${op1:X2},X)",
                "(zp),Y" => $"(${op1:X2}),Y",
                "rel" => 
                    // Relative branch - calculate target address
                    $"${(ushort)(address + bytes + (sbyte)op1):X4}",
                _ => mode
            };
        }
        else if (bytes == 3 && offset + 2 < data.Length)
        {
            ushort op16 = (ushort)(data[offset + 1] | (data[offset + 2] << 8));
            return mode switch
            {
                "abs" => $"${op16:X4}",
                "abs,X" => $"${op16:X4},X",
                "abs,Y" => $"${op16:X4},Y",
                "(abs)" => $"(${op16:X4})",
                _ => mode
            };
        }
        
        return mode;
    }
}
