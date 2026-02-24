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
    /// Additional labels that resolve to the same address as this block's Label.
    /// Used for ca65 label aliases (e.g., _famitone_init=FamiToneInit).
    /// </summary>
    public List<string>? AdditionalLabels { get; set; }

    /// <summary>
    /// Raw data bytes for data-only blocks. When set, instructions are ignored.
    /// </summary>
    public byte[]? RawData { get; set; }

    /// <summary>
    /// Relocation entries for data blocks with label references (e.g., .word @label).
    /// Each entry is (byte offset in RawData, label name) where 2 bytes at the offset
    /// should be patched with the label's resolved absolute address.
    /// </summary>
    public List<(int Offset, string Label)>? Relocations { get; set; }

    /// <summary>
    /// Internal labels within a data block, mapping label name to byte offset.
    /// Used when a data segment has multiple labels at different positions.
    /// </summary>
    public Dictionary<string, int>? InternalLabels { get; set; }

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
    /// <param name="label">Optional label for this instruction (overrides pending label)</param>
    /// <returns>This block for fluent chaining</returns>
    public Block Emit(Instruction instruction, string? label = null)
    {
        // Use explicit label if provided, otherwise use first pending label
        var effectiveLabel = label ?? (_pendingLabels.Count > 0 ? _pendingLabels[0] : null);
        
        // Add instruction with first label
        _instructions.Add((instruction, effectiveLabel));
        
        // For any additional pending labels, add them as aliases pointing to same instruction
        // We store them in the _labelAliases dictionary to resolve during address calculation
        if (label == null && _pendingLabels.Count > 1)
        {
            for (int i = 1; i < _pendingLabels.Count; i++)
            {
                _labelAliases[_pendingLabels[i]] = effectiveLabel!;
            }
        }
        
        _pendingLabels.Clear();
        return this;
    }
    
    /// <summary>
    /// Maps aliased labels to their primary label (for IL instructions that don't emit code)
    /// </summary>
    private readonly Dictionary<string, string> _labelAliases = new();
    
    /// <summary>
    /// Gets the label aliases dictionary for address resolution
    /// </summary>
    public IReadOnlyDictionary<string, string> LabelAliases => _labelAliases;

    /// <summary>
    /// Adds multiple instructions at the end of the block
    /// </summary>
    public Block EmitRange(IEnumerable<Instruction> instructions)
    {
        bool first = true;
        foreach (var instruction in instructions)
        {
            if (first)
            {
                // First instruction gets all pending labels
                Emit(instruction);
                first = false;
            }
            else
            {
                _instructions.Add((instruction, null));
            }
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
    /// Any labels on removed instructions are moved back to pending labels.
    /// </summary>
    /// <param name="count">Number of instructions to remove</param>
    public void RemoveLast(int count = 1)
    {
        for (int i = 0; i < count && _instructions.Count > 0; i++)
        {
            var (_, label) = _instructions[_instructions.Count - 1];
            _instructions.RemoveAt(_instructions.Count - 1);
            
            // Preserve any label that was on the removed instruction
            if (label != null)
            {
                // Insert at the beginning so they're processed first
                _pendingLabels.Insert(0, label);
            }
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
    /// Pending labels to be applied to the next emitted instruction.
    /// Multiple labels can accumulate when IL instructions don't emit code.
    /// </summary>
    private readonly List<string> _pendingLabels = new();

    /// <summary>
    /// Sets a label to be applied to the next emitted instruction.
    /// Used for single-pass transpilation where labels are added as instructions are emitted.
    /// If multiple labels are set before an instruction is emitted, all are preserved.
    /// </summary>
    public void SetNextLabel(string label)
    {
        _pendingLabels.Add(label);
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

    /// <summary>
    /// Gets the total byte size of this block (all instructions or raw data).
    /// </summary>
    public int ByteSize
    {
        get
        {
            if (RawData != null) return RawData.Length;
            int size = 0;
            for (int i = 0; i < _instructions.Count; i++)
                size += _instructions[i].Instruction.Size;
            return size;
        }
    }
}
