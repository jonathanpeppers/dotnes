namespace dotnes.ObjectModel;

/// <summary>
/// Represents a single 6502 instruction
/// </summary>
public record Instruction(
    Opcode Opcode,
    AddressMode Mode,
    Operand? Operand = null,
    string? Comment = null)
{
    /// <summary>
    /// Total size in bytes (opcode + operand)
    /// </summary>
    public int Size => 1 + (Operand?.Size ?? 0);

    /// <summary>
    /// Gets the encoded opcode byte for this instruction + addressing mode
    /// </summary>
    public byte EncodedOpcode => OpcodeTable.Encode(Opcode, Mode);

    /// <summary>
    /// Emits the instruction bytes at the given address
    /// </summary>
    /// <param name="address">Address where this instruction is located</param>
    /// <param name="labels">Label table for resolving references</param>
    /// <returns>Byte array containing the encoded instruction</returns>
    public byte[] ToBytes(ushort address, LabelTable labels)
    {
        var result = new byte[Size];
        result[0] = EncodedOpcode;
        if (Operand != null)
        {
            var operandBytes = Operand.ToBytes(address, labels);
            operandBytes.CopyTo(result, 1);
        }
        return result;
    }

    /// <summary>
    /// Returns a disassembly-style string representation
    /// </summary>
    public override string ToString()
    {
        var mnemonic = Opcode.ToString();
        var operandStr = Operand?.ToString() ?? "";

        var formatted = Mode switch
        {
            AddressMode.Implied => mnemonic,
            AddressMode.Accumulator => $"{mnemonic} A",
            AddressMode.Immediate => $"{mnemonic} {operandStr}",
            AddressMode.ZeroPage => $"{mnemonic} {operandStr}",
            AddressMode.ZeroPageX => $"{mnemonic} {operandStr},X",
            AddressMode.ZeroPageY => $"{mnemonic} {operandStr},Y",
            AddressMode.Absolute => $"{mnemonic} {operandStr}",
            AddressMode.AbsoluteX => $"{mnemonic} {operandStr},X",
            AddressMode.AbsoluteY => $"{mnemonic} {operandStr},Y",
            AddressMode.Indirect => $"{mnemonic} ({operandStr})",
            AddressMode.IndexedIndirect => $"{mnemonic} ({operandStr},X)",
            AddressMode.IndirectIndexed => $"{mnemonic} ({operandStr}),Y",
            AddressMode.Relative => $"{mnemonic} {operandStr}",
            _ => $"{mnemonic} {operandStr}"
        };

        return Comment != null ? $"{formatted} ; {Comment}" : formatted;
    }
}
