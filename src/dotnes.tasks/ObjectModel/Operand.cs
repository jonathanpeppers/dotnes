namespace dotnes.ObjectModel;

/// <summary>
/// Size of an operand when encoded
/// </summary>
public enum OperandSize
{
    /// <summary>Single byte operand</summary>
    Byte = 1,
    /// <summary>Two byte (word) operand</summary>
    Word = 2,
}

/// <summary>
/// Base class for instruction operands
/// </summary>
public abstract record Operand
{
    /// <summary>
    /// Size in bytes when encoded (1 or 2)
    /// </summary>
    public abstract int Size { get; }

    /// <summary>
    /// Resolves the operand to its byte representation
    /// </summary>
    /// <param name="currentAddress">Address of the instruction containing this operand</param>
    /// <param name="labels">Label table for resolving label references</param>
    /// <returns>Byte array representing the operand</returns>
    public abstract byte[] ToBytes(ushort currentAddress, LabelTable labels);
}

/// <summary>
/// An immediate byte value operand
/// </summary>
public record ImmediateOperand(byte Value) : Operand
{
    public override int Size => 1;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
        => [Value];

    public override string ToString() => $"#${Value:X2}";
}

/// <summary>
/// An absolute 16-bit address operand
/// </summary>
public record AbsoluteOperand(ushort Address) : Operand
{
    public override int Size => 2;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
        => [(byte)(Address & 0xFF), (byte)(Address >> 8)]; // Little-endian

    public override string ToString() => $"${Address:X4}";
}

/// <summary>
/// A label reference operand that will be resolved to an address
/// </summary>
public record LabelOperand(string Label, OperandSize OperandSize) : Operand
{
    public override int Size => (int)OperandSize;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort address))
            throw new UnresolvedLabelException(Label);

        if (OperandSize == OperandSize.Byte)
            return [(byte)address];
        return [(byte)(address & 0xFF), (byte)(address >> 8)];
    }

    public override string ToString() => Label;
}

/// <summary>
/// A label reference plus a constant offset, resolved to (label_address + offset).
/// Used for accessing hi bytes in interleaved 16-bit tables (label+1).
/// </summary>
public record LabelOffsetOperand(string Label, int Offset) : Operand
{
    public override int Size => 2; // Always word-sized

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort address))
            throw new UnresolvedLabelException(Label);

        ushort resolved = (ushort)(address + Offset);
        return [(byte)(resolved & 0xFF), (byte)(resolved >> 8)];
    }

    public override string ToString() => $"{Label}+{Offset}";
}

/// <summary>
/// A relative offset operand for branch instructions (resolved from label)
/// </summary>
public record RelativeOperand(string Label) : Operand
{
    public override int Size => 1;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort targetAddress))
            throw new UnresolvedLabelException(Label);

        // Relative offset is from the instruction AFTER this one (PC + 2)
        int offset = targetAddress - (currentAddress + 2);
        if (offset < -128 || offset > 127)
            throw new BranchOutOfRangeException(Label, offset);

        return [(byte)(sbyte)offset];
    }

    public override string ToString() => Label;
}

/// <summary>
/// A relative offset operand with a pre-calculated byte offset
/// </summary>
public record RelativeByteOperand(sbyte Offset) : Operand
{
    public override int Size => 1;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
        => [(byte)Offset];

    public override string ToString() => Offset >= 0 ? $"+{Offset}" : $"{Offset}";
}

/// <summary>
/// A label reference operand that resolves to the LOW byte of a 16-bit address
/// </summary>
public record LowByteOperand(string Label) : Operand
{
    public override int Size => 1;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort address))
            throw new UnresolvedLabelException(Label);

        return [(byte)(address & 0xFF)];
    }

    public override string ToString() => $"#<{Label}";
}

/// <summary>
/// A label reference operand that resolves to the HIGH byte of a 16-bit address
/// </summary>
public record HighByteOperand(string Label) : Operand
{
    public override int Size => 1;

    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort address))
            throw new UnresolvedLabelException(Label);

        return [(byte)(address >> 8)];
    }

    public override string ToString() => $"#>{Label}";
}
