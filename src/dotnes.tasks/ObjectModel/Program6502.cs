namespace dotnes.ObjectModel;

/// <summary>
/// Represents an entire 6502 program with support for address-based indexing,
/// label resolution, and code manipulation.
/// </summary>
public class Program6502
{
    private readonly List<Block> _blocks = new();
    private readonly LabelTable _labels = new();
    private readonly Dictionary<string, ushort> _externalLabels = new();
    private readonly List<byte[]> _rawData = new();
    private bool _addressesValid = false;

    /// <summary>
    /// Base address where the program is loaded (typically $8000 for NES)
    /// </summary>
    public ushort BaseAddress { get; set; } = 0x8000;

    /// <summary>
    /// All instruction blocks in order
    /// </summary>
    public IReadOnlyList<Block> Blocks => _blocks;

    /// <summary>
    /// Label table for the program
    /// </summary>
    public LabelTable Labels => _labels;

    /// <summary>
    /// Defines an external label (e.g., subroutines from library code).
    /// External labels are preserved across ResolveAddresses() calls.
    /// </summary>
    public void DefineExternalLabel(string name, ushort address)
    {
        _externalLabels[name] = address;
        _labels.DefineOrUpdate(name, address);
    }

    /// <summary>
    /// Total size in bytes of all blocks
    /// </summary>
    public int TotalSize
    {
        get
        {
            int size = 0;
            foreach (var block in _blocks)
                size += block.Size;
            foreach (var data in _rawData)
                size += data.Length;
            return size;
        }
    }

    /// <summary>
    /// Number of blocks in the program
    /// </summary>
    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Gets the instruction at a specific address, or null if not found
    /// </summary>
    public Instruction? this[ushort address]
    {
        get => FindInstructionAt(address);
    }

    /// <summary>
    /// Creates a new named block (subroutine) and adds it to the program
    /// </summary>
    /// <param name="label">Optional label for the block</param>
    /// <returns>The newly created block</returns>
    public Block CreateBlock(string? label = null)
    {
        var block = new Block(label);
        _blocks.Add(block);
        _addressesValid = false;
        return block;
    }

    /// <summary>
    /// Adds an existing block to the program
    /// </summary>
    public void AddBlock(Block block)
    {
        _blocks.Add(block);
        _addressesValid = false;
    }

    /// <summary>
    /// Inserts a block at a specific index
    /// </summary>
    public void InsertBlock(int index, Block block)
    {
        _blocks.Insert(index, block);
        _addressesValid = false;
    }

    /// <summary>
    /// Removes a block from the program
    /// </summary>
    public bool RemoveBlock(Block block)
    {
        bool removed = _blocks.Remove(block);
        if (removed) _addressesValid = false;
        return removed;
    }

    /// <summary>
    /// Gets a block by its label
    /// </summary>
    public Block? GetBlock(string label)
    {
        return _blocks.FirstOrDefault(b => b.Label == label);
    }

    /// <summary>
    /// Adds raw byte data to the program (e.g., lookup tables)
    /// </summary>
    public void AddRawData(byte[] data, string? label = null)
    {
        if (label != null)
        {
            // We'll define the label when resolving addresses
            // For now, store the intent
        }
        _rawData.Add(data);
        _addressesValid = false;
    }

    /// <summary>
    /// Recalculates all addresses and resolves labels.
    /// Call this before emitting bytes or after modifying blocks.
    /// </summary>
    public void ResolveAddresses()
    {
        _labels.Clear();
        
        // Restore external labels
        foreach (var kvp in _externalLabels)
            _labels.Define(kvp.Key, kvp.Value);

        ushort currentAddress = BaseAddress;

        foreach (var block in _blocks)
        {
            // Define block label
            if (block.Label != null)
                _labels.DefineOrUpdate(block.Label, currentAddress);

            // Define instruction labels and advance address
            foreach (var (instruction, label) in block.InstructionsWithLabels)
            {
                if (label != null)
                    _labels.DefineOrUpdate(label, currentAddress);
                currentAddress += (ushort)instruction.Size;
            }
        }

        // Raw data comes after all blocks
        foreach (var data in _rawData)
        {
            currentAddress += (ushort)data.Length;
        }

        _addressesValid = true;
    }

    /// <summary>
    /// Emits the program to a byte array
    /// </summary>
    public byte[] ToBytes()
    {
        if (!_addressesValid)
            ResolveAddresses();

        var ms = new MemoryStream(TotalSize);
        ushort currentAddress = BaseAddress;

        foreach (var block in _blocks)
        {
            foreach (var entry in block.InstructionsWithLabels)
            {
                var bytes = entry.Instruction.ToBytes(currentAddress, _labels);
                ms.Write(bytes, 0, bytes.Length);
                currentAddress += (ushort)bytes.Length;
            }
        }

        foreach (var data in _rawData)
        {
            ms.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Emits the program to a stream
    /// </summary>
    public void WriteTo(Stream stream)
    {
        var bytes = ToBytes();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Finds the instruction at a specific address
    /// </summary>
    public Instruction? FindInstructionAt(ushort targetAddress)
    {
        if (!_addressesValid)
            ResolveAddresses();

        ushort currentAddress = BaseAddress;

        foreach (var block in _blocks)
        {
            foreach (var (instruction, _) in block.InstructionsWithLabels)
            {
                if (currentAddress == targetAddress)
                    return instruction;
                currentAddress += (ushort)instruction.Size;
                if (currentAddress > targetAddress)
                    return null; // Past the target, instruction not at exact address
            }
        }

        return null;
    }

    /// <summary>
    /// Finds all instructions that reference a given label
    /// </summary>
    public IEnumerable<(Block Block, int Index, Instruction Instruction)> FindReferencesTo(string label)
    {
        foreach (var block in _blocks)
        {
            for (int i = 0; i < block.Count; i++)
            {
                var instr = block[i];
                if (instr.Operand is LabelOperand lo && lo.Label == label)
                    yield return (block, i, instr);
                else if (instr.Operand is RelativeOperand ro && ro.Label == label)
                    yield return (block, i, instr);
            }
        }
    }

    /// <summary>
    /// Gets the address of a block
    /// </summary>
    public ushort GetBlockAddress(Block block)
    {
        if (!_addressesValid)
            ResolveAddresses();

        ushort currentAddress = BaseAddress;

        foreach (var b in _blocks)
        {
            if (b == block)
                return currentAddress;
            currentAddress += (ushort)b.Size;
        }

        throw new ArgumentException("Block not found in program", nameof(block));
    }

    /// <summary>
    /// Gets the address of an instruction by block and index
    /// </summary>
    public ushort GetInstructionAddress(Block block, int index)
    {
        if (!_addressesValid)
            ResolveAddresses();

        ushort blockAddress = GetBlockAddress(block);
        return (ushort)(blockAddress + block.GetOffsetAt(index));
    }

    /// <summary>
    /// Validates that all label references can be resolved
    /// </summary>
    /// <returns>List of unresolved labels, empty if all resolved</returns>
    public IReadOnlyList<string> Validate()
    {
        ResolveAddresses();

        var unresolved = new List<string>();

        foreach (var block in _blocks)
        {
            foreach (var (instruction, _) in block.InstructionsWithLabels)
            {
                if (instruction.Operand is LabelOperand lo && !_labels.IsDefined(lo.Label))
                    unresolved.Add(lo.Label);
                else if (instruction.Operand is RelativeOperand ro && !_labels.IsDefined(ro.Label))
                    unresolved.Add(ro.Label);
            }
        }

        return unresolved.Distinct().ToList();
    }

    /// <summary>
    /// Returns a disassembly of the program
    /// </summary>
    public string Disassemble()
    {
        if (!_addressesValid)
            ResolveAddresses();

        var sb = new System.Text.StringBuilder();
        ushort address = BaseAddress;

        foreach (var block in _blocks)
        {
            if (block.Label != null)
                sb.AppendLine($"{block.Label}:");

            foreach (var (instr, label) in block.InstructionsWithLabels)
            {
                if (label != null)
                    sb.AppendLine($"  {label}:");

                sb.AppendLine($"    ${address:X4}  {instr}");
                address += (ushort)instr.Size;
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Invalidates cached addresses, forcing recalculation on next access
    /// </summary>
    public void InvalidateAddresses()
    {
        _addressesValid = false;
    }
}
