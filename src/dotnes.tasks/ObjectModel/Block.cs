namespace dotnes.ObjectModel;

/// <summary>
/// Represents a contiguous sequence of instructions, typically a subroutine.
/// Can also represent raw data bytes (e.g., lookup tables).
/// </summary>
public class Block
{
    private readonly List<(Instruction Instruction, string? Label)> _instructions = new();

    /// <summary>
    /// Creates a new block with an optional label
    /// </summary>
    public Block(string? label = null, int labelOffset = 0)
    {
        Label = label;
        LabelOffset = labelOffset;
    }

    /// <summary>
    /// Creates a data-only block containing raw bytes
    /// </summary>
    public static Block FromRawData(byte[] data, string? label = null)
    {
        return new Block(label) { RawData = data };
    }

    /// <summary>
    /// Optional label for this block (e.g., subroutine name)
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Offset in bytes from start of block where the label should point.
    /// Used when blocks have prefix instructions before the main entry point.
    /// </summary>
    public int LabelOffset { get; set; }

    /// <summary>
    /// Raw data bytes for data-only blocks. When set, instructions are ignored.
    /// </summary>
    public byte[]? RawData { get; set; }

    /// <summary>
    /// Returns true if this block contains raw data instead of instructions
    /// </summary>
    public bool IsDataBlock => RawData != null;

    /// <summary>
    /// Number of instructions in this block (0 for data blocks)
    /// </summary>
    public int Count => RawData != null ? 0 : _instructions.Count;

    /// <summary>
    /// Total size in bytes
    /// </summary>
    public int Size => RawData?.Length ?? _instructions.Sum(i => i.Instruction.Size);

    /// <summary>
    /// Gets instruction at the specified index
    /// </summary>
    public Instruction this[int index] => _instructions[index].Instruction;

    /// <summary>
    /// All instructions with their labels
    /// </summary>
    public IEnumerable<(Instruction Instruction, string? Label)> InstructionsWithLabels
        => _instructions;

    /// <summary>
    /// Gets the label at the specified index, if any
    /// </summary>
    public string? GetLabelAt(int index) => _instructions[index].Label;

    /// <summary>
    /// Adds an instruction at the end of the block
    /// </summary>
    /// <param name="instruction">The instruction to add</param>
    /// <param name="label">Optional label for this instruction</param>
    /// <returns>This block for fluent chaining</returns>
    public Block Emit(Instruction instruction, string? label = null)
    {
        _instructions.Add((instruction, label));
        return this;
    }

    /// <summary>
    /// Adds multiple instructions at the end of the block
    /// </summary>
    public Block EmitRange(IEnumerable<Instruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            _instructions.Add((instruction, null));
        }
        return this;
    }

    /// <summary>
    /// Inserts an instruction at the specified index
    /// </summary>
    public void Insert(int index, Instruction instruction, string? label = null)
    {
        _instructions.Insert(index, (instruction, label));
    }

    /// <summary>
    /// Removes the instruction at the specified index
    /// </summary>
    public void RemoveAt(int index)
    {
        _instructions.RemoveAt(index);
    }

    /// <summary>
    /// Removes the last N instructions (replaces SeekBack!)
    /// </summary>
    /// <param name="count">Number of instructions to remove</param>
    public void RemoveLast(int count = 1)
    {
        for (int i = 0; i < count && _instructions.Count > 0; i++)
        {
            _instructions.RemoveAt(_instructions.Count - 1);
        }
    }

    /// <summary>
    /// Replaces the instruction at the specified index
    /// </summary>
    public void Replace(int index, Instruction newInstruction)
    {
        var (_, label) = _instructions[index];
        _instructions[index] = (newInstruction, label);
    }

    /// <summary>
    /// Sets the label at the specified index
    /// </summary>
    public void SetLabel(int index, string? label)
    {
        var (instruction, _) = _instructions[index];
        _instructions[index] = (instruction, label);
    }

    /// <summary>
    /// Clears all instructions from the block
    /// </summary>
    public void Clear()
    {
        _instructions.Clear();
    }

    /// <summary>
    /// Returns the last instruction, or null if empty
    /// </summary>
    public Instruction? LastOrDefault()
    {
        return _instructions.Count > 0 ? _instructions[_instructions.Count - 1].Instruction : null;
    }

    /// <summary>
    /// Returns the last N instructions
    /// </summary>
    public IEnumerable<Instruction> TakeLast(int count)
    {
        var start = Math.Max(0, _instructions.Count - count);
        for (int i = start; i < _instructions.Count; i++)
            yield return _instructions[i].Instruction;
    }

    /// <summary>
    /// Finds instructions matching a predicate
    /// </summary>
    public IEnumerable<(int Index, Instruction Instruction)> FindAll(Func<Instruction, bool> predicate)
    {
        for (int i = 0; i < _instructions.Count; i++)
        {
            if (predicate(_instructions[i].Instruction))
                yield return (i, _instructions[i].Instruction);
        }
    }

    /// <summary>
    /// Calculates the byte offset from the start of the block to the instruction at the given index
    /// </summary>
    public int GetOffsetAt(int index)
    {
        int offset = 0;
        for (int i = 0; i < index && i < _instructions.Count; i++)
        {
            offset += _instructions[i].Instruction.Size;
        }
        return offset;
    }
}
