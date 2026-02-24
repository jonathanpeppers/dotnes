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
    /// Total size in bytes of all blocks (including data blocks)
    /// </summary>
    public int TotalSize
    {
        get
        {
            int size = 0;
            foreach (var block in _blocks)
                size += block.Size;
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
    /// Gets the bytes for a specific block (e.g., "main").
    /// Requires that addresses have been resolved via ResolveAddresses().
    /// </summary>
    public byte[] GetMainBlock(string label = "main")
    {
        if (!_addressesValid)
            ResolveAddresses();

        var block = GetBlock(label);
        if (block == null)
            return Array.Empty<byte>();

        // Calculate the starting address for this block
        ushort currentAddress = BaseAddress;
        foreach (var b in _blocks)
        {
            if (b == block)
                break;
            currentAddress += (ushort)b.Size;
        }

        // Emit the block's instructions
        var ms = new MemoryStream(block.Size);
        if (block.IsDataBlock && block.RawData != null)
        {
            ms.Write(block.RawData, 0, block.RawData.Length);
        }
        else
        {
            _labels.CurrentScope = block.Label;
            foreach (var entry in block.InstructionsWithLabels)
            {
                var bytes = entry.Instruction.ToBytes(currentAddress, _labels);
                ms.Write(bytes, 0, bytes.Length);
                currentAddress += (ushort)bytes.Length;
            }
            _labels.CurrentScope = null;
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Adds raw byte data as an inline data block (e.g., lookup tables).
    /// Data blocks are written in order with other blocks.
    /// </summary>
    public void AddRawData(byte[] data, string? label = null)
    {
        var dataBlock = Block.FromRawData(data, label);
        _blocks.Add(dataBlock);
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
            // Define block label (accounting for any label offset)
            if (block.Label != null)
                _labels.DefineOrUpdate(block.Label, (ushort)(currentAddress + block.LabelOffset));

            // Define additional labels (aliases) that point to the same block address
            // Format: "aliasName" = same as block label, or "aliasName=targetLabel" for instruction-level aliases
            if (block.AdditionalLabels != null)
            {
                foreach (var alias in block.AdditionalLabels)
                {
                    int eqIdx = alias.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        // Instruction-level alias: resolve after instruction labels are defined
                        // (handled below after instruction labels)
                    }
                    else
                    {
                        _labels.DefineOrUpdate(alias, (ushort)(currentAddress + block.LabelOffset));
                    }
                }
            }

            if (block.IsDataBlock)
            {
                // Data blocks just advance the address by their size
                currentAddress += (ushort)block.Size;
            }
            else
            {
                // Define instruction labels and advance address
                foreach (var (instruction, label) in block.InstructionsWithLabels)
                {
                    if (label != null)
                    {
                        _labels.DefineOrUpdate(ScopeLabel(label, block), currentAddress);
                    }
                    currentAddress += (ushort)instruction.Size;
                }
                
                // Resolve label aliases (for IL instructions that don't emit code)
                foreach (var kvp in block.LabelAliases)
                {
                    if (_labels.TryResolve(ScopeLabel(kvp.Value, block), out ushort address))
                    {
                        _labels.DefineOrUpdate(ScopeLabel(kvp.Key, block), address);
                    }
                }

                // Resolve instruction-level additional labels (e.g., _famitone_init=FamiToneInit)
                if (block.AdditionalLabels != null)
                {
                    foreach (var alias in block.AdditionalLabels)
                    {
                        int eqIdx = alias.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            var aliasName = alias.Substring(0, eqIdx);
                            var targetName = alias.Substring(eqIdx + 1);
                            if (_labels.TryResolve(targetName, out ushort address))
                                _labels.DefineOrUpdate(aliasName, address);
                        }
                    }
                }
            }
        }

        _addressesValid = true;
    }

    /// <summary>
    /// Scopes a local label (starting with @) by prefixing it with the block's label.
    /// Non-local labels are returned unchanged.
    /// </summary>
    static string ScopeLabel(string label, Block block)
    {
        if (label.StartsWith("@") && block.Label != null)
        {
            return $"{block.Label}:{label}";
        }
        return label;
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
            _labels.CurrentScope = block.Label;
            if (block.IsDataBlock && block.RawData != null)
            {
                // Write raw data bytes directly
                ms.Write(block.RawData, 0, block.RawData.Length);

                // Patch relocations: resolve label references to absolute addresses
                if (block.Relocations != null)
                {
                    long savedPos = ms.Position;
                    foreach (var (offset, label) in block.Relocations)
                    {
                        if (_labels.TryResolve(label, out ushort addr))
                        {
                            ms.Position = savedPos - block.RawData.Length + offset;
                            ms.WriteByte((byte)(addr & 0xFF));
                            ms.WriteByte((byte)(addr >> 8));
                        }
                    }
                    ms.Position = savedPos;
                }

                currentAddress += (ushort)block.RawData.Length;
            }
            else
            {
                // Write instructions
                foreach (var entry in block.InstructionsWithLabels)
                {
                    var bytes = entry.Instruction.ToBytes(currentAddress, _labels);
                    ms.Write(bytes, 0, bytes.Length);
                    currentAddress += (ushort)bytes.Length;
                }
            }
        }
        _labels.CurrentScope = null;

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

    /// <summary>
    /// Gets the labels dictionary for compatibility with NESWriter.
    /// Returns a copy of the current label addresses.
    /// </summary>
    public Dictionary<string, ushort> GetLabels()
    {
        if (!_addressesValid)
            ResolveAddresses();

        var result = new Dictionary<string, ushort>();
        foreach (var kvp in _labels.Labels)
            result[kvp.Key] = kvp.Value;
        return result;
    }

    /// <summary>
    /// Creates a Program6502 with all standard NES built-in subroutines.
    /// Note: Forward references (popa, popax, etc.) are initialized to 0.
    /// Call AddFinalBuiltIns() after adding main program to set actual addresses.
    /// </summary>
    public static Program6502 CreateWithBuiltIns()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        // Pre-define forward references with placeholder addresses (0)
        // These will be updated when AddFinalBuiltIns() is called
        program.DefineExternalLabel("popa", 0);
        program.DefineExternalLabel("popax", 0);
        program.DefineExternalLabel("pusha", 0);
        program.DefineExternalLabel("pushax", 0);
        program.DefineExternalLabel("zerobss", 0);
        program.DefineExternalLabel("copydata", 0);
        program.DefineExternalLabel("main", 0);
        program.DefineExternalLabel("updName", NESConstants.updName);

        // Add all standard built-in subroutines (same order as NESWriter.WriteBuiltIns)
        program.AddBlock(BuiltInSubroutines.Exit());
        program.AddBlock(BuiltInSubroutines.InitPPU());
        program.AddBlock(BuiltInSubroutines.ClearPalette());
        program.AddBlock(BuiltInSubroutines.ClearVRAM());
        program.AddBlock(BuiltInSubroutines.ClearRAM());
        program.AddBlock(BuiltInSubroutines.WaitSync3());
        program.AddBlock(BuiltInSubroutines.DetectNTSC());
        program.AddBlock(BuiltInSubroutines.Nmi());
        program.AddBlock(BuiltInSubroutines.DoUpdate());
        program.AddBlock(BuiltInSubroutines.UpdPal());
        program.AddBlock(BuiltInSubroutines.UpdVRAM());
        program.AddBlock(BuiltInSubroutines.SkipUpd());
        program.AddBlock(BuiltInSubroutines.SkipAll());
        program.AddBlock(BuiltInSubroutines.SkipNtsc());
        program.AddBlock(BuiltInSubroutines.Irq());
        program.AddBlock(BuiltInSubroutines.NmiSetCallback());
        program.AddBlock(BuiltInSubroutines.PalAll());
        program.AddBlock(BuiltInSubroutines.PalCopy());
        program.AddBlock(BuiltInSubroutines.PalBg());
        program.AddBlock(BuiltInSubroutines.PalSpr());
        program.AddBlock(BuiltInSubroutines.PalCol());
        program.AddBlock(BuiltInSubroutines.PalClear());
        program.AddBlock(BuiltInSubroutines.PalSprBright());
        program.AddBlock(BuiltInSubroutines.PalBgBright());
        program.AddBlock(BuiltInSubroutines.PalBright());
        program.AddBlock(BuiltInSubroutines.PpuOff());
        program.AddBlock(BuiltInSubroutines.PpuOnAll());
        program.AddBlock(BuiltInSubroutines.PpuOnOff());
        program.AddBlock(BuiltInSubroutines.PpuOnBg());
        program.AddBlock(BuiltInSubroutines.PpuOnSpr());
        program.AddBlock(BuiltInSubroutines.PpuMask());
        program.AddBlock(BuiltInSubroutines.PpuSystem());
        program.AddBlock(BuiltInSubroutines.GetPpuCtrlVar());
        program.AddBlock(BuiltInSubroutines.SetPpuCtrlVar());
        program.AddBlock(BuiltInSubroutines.OamClear());
        program.AddBlock(BuiltInSubroutines.OamSize());
        program.AddBlock(BuiltInSubroutines.OamHideRest());
        program.AddBlock(BuiltInSubroutines.PpuWaitFrame());
        program.AddBlock(BuiltInSubroutines.PpuWaitNmi());
        program.AddBlock(BuiltInSubroutines.Scroll());
        program.AddBlock(BuiltInSubroutines.BankSpr());
        program.AddBlock(BuiltInSubroutines.BankBg());
        program.AddBlock(BuiltInSubroutines.VramWrite());
        program.AddBlock(BuiltInSubroutines.SetVramUpdate());
        program.AddBlock(BuiltInSubroutines.FlushVramUpdate());
        program.AddBlock(BuiltInSubroutines.VramAdr());
        program.AddBlock(BuiltInSubroutines.VramPut());
        program.AddBlock(BuiltInSubroutines.VramFill());
        program.AddBlock(BuiltInSubroutines.VramInc());
        program.AddBlock(BuiltInSubroutines.NesClock());
        program.AddBlock(BuiltInSubroutines.Delay());

        // Add palette brightness tables as raw data
        program.AddRawData(NESLib.palBrightTableL);
        program.AddRawData(NESLib.palBrightTable0);
        program.AddRawData(NESLib.palBrightTable1);
        program.AddRawData(NESLib.palBrightTable2);
        program.AddRawData(NESLib.palBrightTable3);
        program.AddRawData(NESLib.palBrightTable4);
        program.AddRawData(NESLib.palBrightTable5);
        program.AddRawData(NESLib.palBrightTable6);
        program.AddRawData(NESLib.palBrightTable7);
        program.AddRawData(NESLib.palBrightTable8);

        program.AddBlock(BuiltInSubroutines.Initlib());

        return program;
    }

    /// <summary>
    /// Pre-calculates the total byte size of all final built-in subroutines.
    /// Must match the blocks added by AddFinalBuiltIns.
    /// </summary>
    public static int CalculateFinalBuiltInsSize(byte locals, HashSet<string>? usedMethods = null)
    {
        int size = 0;
        bool needsDecsp4 = usedMethods != null && usedMethods.Contains("decsp4");

        size += BuiltInSubroutines.Donelib(0).ByteSize;
        size += BuiltInSubroutines.Copydata(0).ByteSize;
        if (needsDecsp4) size += BuiltInSubroutines.Decsp4().ByteSize;
        size += BuiltInSubroutines.Popax().ByteSize;
        size += BuiltInSubroutines.Incsp2().ByteSize;
        size += BuiltInSubroutines.Popa().ByteSize;
        if (!needsDecsp4)
        {
            size += BuiltInSubroutines.Pusha().ByteSize;
            size += BuiltInSubroutines.Pushax().ByteSize;
        }
        else if (usedMethods != null && (usedMethods.Contains("scroll") || usedMethods.Contains("split")))
        {
            // scroll/split handlers emit JSR pushax for 16-bit argument passing
            size += BuiltInSubroutines.Pusha().ByteSize;
            size += BuiltInSubroutines.Pushax().ByteSize;
        }
        size += BuiltInSubroutines.Zerobss(locals).ByteSize;
        if (usedMethods != null)
        {
            // pad_poll is needed directly or as a dependency of pad_trigger/pad_state
            if (usedMethods.Contains("pad_poll") || usedMethods.Contains("pad_trigger") || usedMethods.Contains("pad_state"))
                size += BuiltInSubroutines.PadPoll().ByteSize;
            // pad_trigger/pad_state: included when directly called OR via oam_spr+pad_poll pattern
            bool includePadTrigger = usedMethods.Contains("pad_trigger") || (needsDecsp4 && usedMethods.Contains("pad_poll"));
            bool includePadState = usedMethods.Contains("pad_state") || (needsDecsp4 && usedMethods.Contains("pad_poll"));
            if (includePadTrigger)
                size += BuiltInSubroutines.PadTrigger().ByteSize;
            if (includePadState)
                size += BuiltInSubroutines.PadState().ByteSize;
            if (usedMethods.Contains("oam_spr"))
                size += BuiltInSubroutines.OamSpr().ByteSize;
            if (usedMethods.Contains("rand8"))
                size += BuiltInSubroutines.Rand8().ByteSize;
            if (usedMethods.Contains("rand8") || usedMethods.Contains("set_rand"))
                size += BuiltInSubroutines.SetRand().ByteSize;
            if (usedMethods.Contains("oam_meta_spr"))
                size += BuiltInSubroutines.OamMetaSpr().ByteSize;
            if (usedMethods.Contains("oam_meta_spr_pal"))
                size += BuiltInSubroutines.OamMetaSprPal().ByteSize;
            if (usedMethods.Contains("apu_init"))
                size += BuiltInSubroutines.ApuInit().ByteSize;
            if (usedMethods.Contains("vram_unrle"))
                size += BuiltInSubroutines.VramUnrle().ByteSize;
            if (usedMethods.Contains("split"))
                size += BuiltInSubroutines.Split().ByteSize;
            if (usedMethods.Contains("vrambuf_clear"))
                size += BuiltInSubroutines.VrambufClear().ByteSize;
            if (usedMethods.Contains("vrambuf_put"))
                size += BuiltInSubroutines.VrambufPut().ByteSize;
            if (usedMethods.Contains("vrambuf_end"))
                size += BuiltInSubroutines.VrambufEnd().ByteSize;
            if (usedMethods.Contains("vrambuf_flush"))
                size += BuiltInSubroutines.VrambufFlush().ByteSize;
            if (usedMethods.Contains("nametable_a"))
                size += BuiltInSubroutines.NametableA().ByteSize;
            if (usedMethods.Contains("nametable_b"))
                size += BuiltInSubroutines.NametableB().ByteSize;
            if (usedMethods.Contains("nametable_c"))
                size += BuiltInSubroutines.NametableC().ByteSize;
            if (usedMethods.Contains("nametable_d"))
                size += BuiltInSubroutines.NametableD().ByteSize;
            if (usedMethods.Contains("incsp1"))
                size += BuiltInSubroutines.Incsp1().ByteSize;
            if (usedMethods.Contains("addysp"))
                size += BuiltInSubroutines.Addysp().ByteSize;
            if (usedMethods.Contains("bcd_add"))
                size += BuiltInSubroutines.BcdAdd().ByteSize;
        }
        return size;
    }

    /// <summary>
    /// Adds the final built-in subroutines that come after main().
    /// </summary>
    public void AddFinalBuiltIns(ushort totalSize, byte locals, HashSet<string>? usedMethods = null)
    {
        AddBlock(BuiltInSubroutines.Donelib(totalSize));
        AddBlock(BuiltInSubroutines.Copydata(totalSize));

        bool needsDecsp4 = usedMethods != null && usedMethods.Contains("decsp4");

        if (needsDecsp4)
            AddBlock(BuiltInSubroutines.Decsp4());

        AddBlock(BuiltInSubroutines.Popax());
        AddBlock(BuiltInSubroutines.Incsp2());
        AddBlock(BuiltInSubroutines.Popa());

        // Pusha/Pushax needed for standard calling convention (not decsp4),
        // but also needed when scroll/split handlers emit JSR pushax
        bool needsPushaPushax = !needsDecsp4
            || (usedMethods != null && (usedMethods.Contains("scroll") || usedMethods.Contains("split")));
        if (needsPushaPushax)
        {
            AddBlock(BuiltInSubroutines.Pusha());
            AddBlock(BuiltInSubroutines.Pushax());
        }
        AddBlock(BuiltInSubroutines.Zerobss(locals));

        // Optional methods
        if (usedMethods != null)
        {
            // pad_poll is needed directly or as a dependency of pad_trigger/pad_state
            if (usedMethods.Contains("pad_poll") || usedMethods.Contains("pad_trigger") || usedMethods.Contains("pad_state"))
                AddBlock(BuiltInSubroutines.PadPoll());
            // pad_trigger/pad_state: included when directly called OR via oam_spr+pad_poll pattern
            bool includePadTrigger = usedMethods.Contains("pad_trigger") || (needsDecsp4 && usedMethods.Contains("pad_poll"));
            bool includePadState = usedMethods.Contains("pad_state") || (needsDecsp4 && usedMethods.Contains("pad_poll"));
            if (includePadTrigger)
                AddBlock(BuiltInSubroutines.PadTrigger());
            if (includePadState)
                AddBlock(BuiltInSubroutines.PadState());
            if (usedMethods.Contains("oam_spr"))
                AddBlock(BuiltInSubroutines.OamSpr());
            if (usedMethods.Contains("rand8"))
                AddBlock(BuiltInSubroutines.Rand8());
            if (usedMethods.Contains("rand8") || usedMethods.Contains("set_rand"))
                AddBlock(BuiltInSubroutines.SetRand());
            if (usedMethods.Contains("oam_meta_spr"))
                AddBlock(BuiltInSubroutines.OamMetaSpr());
            if (usedMethods.Contains("oam_meta_spr_pal"))
                AddBlock(BuiltInSubroutines.OamMetaSprPal());
            if (usedMethods.Contains("apu_init"))
                AddBlock(BuiltInSubroutines.ApuInit());
            if (usedMethods.Contains("vram_unrle"))
                AddBlock(BuiltInSubroutines.VramUnrle());
            if (usedMethods.Contains("split"))
                AddBlock(BuiltInSubroutines.Split());
            if (usedMethods.Contains("vrambuf_clear"))
                AddBlock(BuiltInSubroutines.VrambufClear());
            if (usedMethods.Contains("vrambuf_put"))
                AddBlock(BuiltInSubroutines.VrambufPut());
            if (usedMethods.Contains("vrambuf_end"))
                AddBlock(BuiltInSubroutines.VrambufEnd());
            if (usedMethods.Contains("vrambuf_flush"))
                AddBlock(BuiltInSubroutines.VrambufFlush());
            if (usedMethods.Contains("nametable_a"))
                AddBlock(BuiltInSubroutines.NametableA());
            if (usedMethods.Contains("nametable_b"))
                AddBlock(BuiltInSubroutines.NametableB());
            if (usedMethods.Contains("nametable_c"))
                AddBlock(BuiltInSubroutines.NametableC());
            if (usedMethods.Contains("nametable_d"))
                AddBlock(BuiltInSubroutines.NametableD());
            if (usedMethods.Contains("incsp1"))
                AddBlock(BuiltInSubroutines.Incsp1());
            if (usedMethods.Contains("addysp"))
                AddBlock(BuiltInSubroutines.Addysp());
            if (usedMethods.Contains("bcd_add"))
                AddBlock(BuiltInSubroutines.BcdAdd());
        }
    }

    /// <summary>
    /// Calculates the size of music subroutines (play_music + start_music).
    /// These are emitted before main() to match cc65's ROM layout.
    /// </summary>
    public static int CalculateMusicSubroutinesSize(HashSet<string>? usedMethods = null)
    {
        int size = 0;
        if (usedMethods != null)
        {
            if (usedMethods.Contains("play_music"))
                size += BuiltInSubroutines.PlayMusic().ByteSize;
            if (usedMethods.Contains("start_music"))
                size += BuiltInSubroutines.StartMusic().ByteSize;
        }
        return size;
    }

    /// <summary>
    /// Adds music subroutines (play_music + start_music) before main().
    /// Matches cc65's ROM layout where music code precedes main().
    /// </summary>
    public void AddMusicSubroutines(HashSet<string>? usedMethods = null)
    {
        if (usedMethods != null)
        {
            if (usedMethods.Contains("play_music"))
                AddBlock(BuiltInSubroutines.PlayMusic());
            if (usedMethods.Contains("start_music"))
                AddBlock(BuiltInSubroutines.StartMusic());
        }
    }

    /// <summary>
    /// Adds the main program block (user's transpiled code).
    /// Should be called after CreateWithBuiltIns() and before AddFinalBuiltIns().
    /// </summary>
    /// <param name="mainBlock">The block containing the transpiled main() function</param>
    public void AddMainProgram(Block mainBlock)
    {
        AddBlock(mainBlock);
    }

    /// <summary>
    /// Adds raw data (byte arrays from the program, string tables, etc.).
    /// Should be called after AddMainProgram() and before AddFinalBuiltIns().
    /// </summary>
    /// <param name="data">The raw data bytes</param>
    /// <param name="label">Optional label for the data</param>
    public void AddProgramData(byte[] data, string? label = null)
    {
        AddRawData(data, label);
    }

    /// <summary>
    /// Adds the destructor table block.
    /// </summary>
    public void AddDestructorTable()
    {
        AddBlock(BuiltInSubroutines.DestructorTable());
    }

    /// <summary>
    /// Gets labels for all built-in subroutines without actually emitting bytes.
    /// This is useful for early label resolution in the transpiler.
    /// </summary>
    /// <returns>Dictionary mapping label names to their addresses</returns>
    public static Dictionary<string, ushort> GetBuiltInLabels()
    {
        var program = CreateWithBuiltIns();
        program.ResolveAddresses();
        return program.GetLabels();
    }

    /// <summary>
    /// Calculates the total size of all built-in subroutines (without main program).
    /// </summary>
    public static int GetBuiltInSize()
    {
        var program = CreateWithBuiltIns();
        return program.TotalSize;
    }
}
