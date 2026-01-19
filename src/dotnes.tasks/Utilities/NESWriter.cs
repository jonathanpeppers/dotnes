using System.Buffers;
using System.Text;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class NESWriter : IDisposable
{
    public static readonly Encoding Encoding = Encoding.ASCII;

    public NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null)
    {
        _writer = new(stream, Encoding, leaveOpen);
        _logger = logger ?? new NullLogger();

        // Pre-initialize labels that are referenced before their blocks are written
        // These are forward references used by IL2NESWriter and BuiltInSubroutines
        Labels["popa"] = 0;
        Labels["popax"] = 0;
        Labels["pusha"] = 0;
        Labels["pushax"] = 0;
        Labels["zerobss"] = 0;
        Labels["copydata"] = 0;
        // Fixed address label (referenced by FlushVramUpdate)
        Labels["updName"] = NESConstants.updName;
    }

    /// <summary>
    /// PRG_ROM is in 16 KB units
    /// </summary>
    public const int PRG_ROM_BLOCK_SIZE = 16384;
    /// <summary>
    /// CHR ROM in in 8 KB units
    /// </summary>
    public const int CHR_ROM_BLOCK_SIZE = 8192;

    protected const ushort BaseAddress = 0x8000;

    protected readonly BinaryWriter _writer;
    protected readonly ILogger _logger;

    public bool LastLDA { get; protected set; }

    public Stream BaseStream => _writer.BaseStream;

    /// <summary>
    /// Trainer, if present (0 or 512 bytes)
    /// </summary>
    public byte[]? Trainer { get; set; }

    /// <summary>
    /// PRG ROM data (16384 * x bytes)
    /// </summary>
    public byte[]? PRG_ROM { get; set; }

    /// <summary>
    /// CHR ROM data, if present (8192 * y bytes)
    /// </summary>
    public byte[]? CHR_ROM { get; set; }

    /// <summary>
    /// PlayChoice INST-ROM, if present (0 or 8192 bytes)
    /// </summary>
    public byte[]? INST_ROM { get; set; }

    /// <summary>
    /// Mapper, mirroring, battery, trainer
    /// </summary>
    public byte Flags6 { get; set; }

    /// <summary>
    /// Mapper, VS/Playchoice, NES 2.0
    /// </summary>
    public byte Flags7 { get; set; }

    /// <summary>
    /// PRG-RAM size (rarely used extension)
    /// </summary>
    public byte Flags8 { get; set; }

    /// <summary>
    /// TV system (rarely used extension)
    /// </summary>
    public byte Flags9 { get; set; }

    /// <summary>
    /// TV system, PRG-RAM presence (unofficial, rarely used extension)
    /// </summary>
    public byte Flags10 { get; set; }

    public long Length => _writer.BaseStream.Length;

    public Dictionary<string, ushort> Labels { get; private set; } = new();
    private bool _hasPresetLabels = false;
    
    /// <summary>
    /// Stream offset where code begins. Used to calculate correct addresses when
    /// a header has been written before code. In the first pass (no header), this is 0.
    /// In the second pass (after header), this should be set to the header size (16).
    /// </summary>
    public long CodeBaseOffset { get; set; } = 0;

    public void SetLabels(Dictionary<string, ushort> labels)
    {
        Labels = labels;
        _hasPresetLabels = true;
    }

    /// <summary>
    /// A list of methods that were found to be used in the IL code
    /// </summary>
    public HashSet<string>? UsedMethods { get; set; }

    public void WriteHeader(byte PRG_ROM_SIZE = 0, byte CHR_ROM_SIZE = 0)
    {
        _writer.Write('N');
        _writer.Write('E');
        _writer.Write('S');
        _writer.Write('\x1A');
        // Size of PRG ROM in 16 KB units
        if (PRG_ROM != null)
            _writer.Write(checked ((byte)(PRG_ROM.Length / PRG_ROM_BLOCK_SIZE)));
        else
            _writer.Write(PRG_ROM_SIZE);
        // Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
        if (CHR_ROM != null)
            _writer.Write(checked((byte)(CHR_ROM.Length / CHR_ROM_BLOCK_SIZE)));
        else
            _writer.Write(CHR_ROM_SIZE);
        _writer.Write(Flags6);
        _writer.Write(Flags7);
        _writer.Write(Flags8);
        _writer.Write(Flags9);
        _writer.Write(Flags10);
        // 5 bytes of padding
        WriteZeroes(5);
    }

    /// <summary>
    /// Writes N zero-d bytes
    /// </summary>
    public void WriteZeroes(long length)
    {
        for (long i = 0; i < length; i++)
        {
            _writer.Write((byte)0);
        }
    }

    public void Write(byte[] buffer)
    {
        LastLDA = false;
        _writer.Write(buffer);
    }

    public void Write(ushort[] buffer)
    {
        LastLDA = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            _writer.Write(buffer[i]);
        }
    }

    public void Write(byte[] buffer, int index, int count)
    {
        LastLDA = false;
        _writer.Write(buffer, index, count);
    }

    /// <summary>
    /// Writes a string in ASCI form, including a trailing \0
    /// </summary>
    public void WriteString(string text)
    {
        LastLDA = false;
        int length = Encoding.GetByteCount(text);
        var bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            length = Encoding.GetBytes(text, 0, text.Length, bytes, 0);
            _writer.Write(bytes, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
        //TODO: I don't know if there is a 0 between each string, or if this denotes the end of the table
        _writer.Write((byte)0);
    }

    /// <summary>
    /// Writes all the built-in methods from NESLib
    /// </summary>
    public void WriteBuiltIns()
    {
        WriteBlock(BuiltInSubroutines.Exit());
        WriteBlock(BuiltInSubroutines.InitPPU());
        WriteBlock(BuiltInSubroutines.ClearPalette());
        WriteBlock(BuiltInSubroutines.ClearVRAM());
        WriteBlock(BuiltInSubroutines.ClearRAM());
        WriteBlock(BuiltInSubroutines.WaitSync3());
        WriteBlock(BuiltInSubroutines.DetectNTSC());
        WriteBlock(BuiltInSubroutines.Nmi());
        WriteBlock(BuiltInSubroutines.DoUpdate());
        WriteBlock(BuiltInSubroutines.UpdPal());
        WriteBlock(BuiltInSubroutines.UpdVRAM());
        WriteBlock(BuiltInSubroutines.SkipUpd());
        WriteBlock(BuiltInSubroutines.SkipAll());
        WriteBlock(BuiltInSubroutines.SkipNtsc());
        WriteBlock(BuiltInSubroutines.Irq());
        WriteBlock(BuiltInSubroutines.NmiSetCallback());
        WriteBlock(BuiltInSubroutines.PalAll());
        WriteBlock(BuiltInSubroutines.PalCopy());
        WriteBlock(BuiltInSubroutines.PalBg());
        WriteBlock(BuiltInSubroutines.PalSpr());
        WriteBlock(BuiltInSubroutines.PalCol());
        WriteBlock(BuiltInSubroutines.PalClear());
        WriteBlock(BuiltInSubroutines.PalSprBright());
        WriteBlock(BuiltInSubroutines.PalBgBright());
        WriteBlock(BuiltInSubroutines.PalBright());
        WriteBlock(BuiltInSubroutines.PpuOff());
        WriteBlock(BuiltInSubroutines.PpuOnAll());
        WriteBlock(BuiltInSubroutines.PpuOnOff());
        WriteBlock(BuiltInSubroutines.PpuOnBg());
        WriteBlock(BuiltInSubroutines.PpuOnSpr());
        WriteBlock(BuiltInSubroutines.PpuMask());
        WriteBlock(BuiltInSubroutines.PpuSystem());
        WriteBlock(BuiltInSubroutines.GetPpuCtrlVar());
        WriteBlock(BuiltInSubroutines.SetPpuCtrlVar());
        WriteBlock(BuiltInSubroutines.OamClear());
        WriteBlock(BuiltInSubroutines.OamSize());
        WriteBlock(BuiltInSubroutines.OamHideRest());
        WriteBlock(BuiltInSubroutines.PpuWaitFrame());
        WriteBlock(BuiltInSubroutines.PpuWaitNmi());
        WriteBlock(BuiltInSubroutines.Scroll());
        WriteBlock(BuiltInSubroutines.BankSpr());
        WriteBlock(BuiltInSubroutines.BankBg());
        WriteBlock(BuiltInSubroutines.VramWrite());
        WriteBlock(BuiltInSubroutines.SetVramUpdate());
        WriteBlock(BuiltInSubroutines.FlushVramUpdate());
        WriteBlock(BuiltInSubroutines.VramAdr());
        WriteBlock(BuiltInSubroutines.VramPut());
        WriteBlock(BuiltInSubroutines.VramFill());
        WriteBlock(BuiltInSubroutines.VramInc());
        WriteBlock(BuiltInSubroutines.NesClock());
        WriteBlock(BuiltInSubroutines.Delay());
        Write(NESLib.palBrightTableL);
        Write(NESLib.palBrightTable0);
        Write(NESLib.palBrightTable1);
        Write(NESLib.palBrightTable2);
        Write(NESLib.palBrightTable3);
        Write(NESLib.palBrightTable4);
        Write(NESLib.palBrightTable5);
        Write(NESLib.palBrightTable6);
        Write(NESLib.palBrightTable7);
        Write(NESLib.palBrightTable8);
        WriteBlock(BuiltInSubroutines.Initlib());
    }

    public void WriteDestructorTable()
    {
        WriteBlock(BuiltInSubroutines.DestructorTable());
    }

    /// <summary>
    /// These are any subroutines after our `static void main()` method
    /// </summary>
    public void WriteFinalBuiltIns(ushort totalSize, byte locals)
    {
        WriteBlock(BuiltInSubroutines.Donelib(totalSize));
        WriteBlock(BuiltInSubroutines.Copydata(totalSize));
        WriteBlock(BuiltInSubroutines.Popax());
        WriteBlock(BuiltInSubroutines.Incsp2());
        WriteBlock(BuiltInSubroutines.Popa());
        WriteBlock(BuiltInSubroutines.Pusha());
        WriteBlock(BuiltInSubroutines.Pushax());
        WriteBlock(BuiltInSubroutines.Zerobss(locals));

        // List of optional methods at the end
        if (UsedMethods is not null)
        {
            if (UsedMethods.Contains(nameof(NESLib.oam_spr)))
            {
                WriteBlock(BuiltInSubroutines.OamSpr());
            }
            if (UsedMethods.Contains(nameof(NESLib.pad_poll)))
            {
                WriteBlock(BuiltInSubroutines.PadPoll());
            }
        }
    }

    /// <summary>
    /// Writes an "implied" instruction that has no argument
    /// </summary>
    public void Write(NESInstruction i)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X})");
        _writer.Write((byte)i);
    }

    /// <summary>
    /// Writes an instruction with a single byte argument
    /// </summary>
    public void Write (NESInstruction i, byte @byte)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {@byte:X}");
        _writer.Write((byte)i);
        _writer.Write(@byte);
    }

    /// <summary>
    /// Writes an instruction with an address argument (2 bytes)
    /// </summary>
    public void Write(NESInstruction i, ushort address)
    {
        LastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {address:X}");
        _writer.Write((byte)i);
        _writer.Write(address);
    }

    public void Write()
    {
        WriteHeader();
        if (PRG_ROM != null)
            _writer.Write(PRG_ROM);
        if (CHR_ROM != null)
            _writer.Write(CHR_ROM);
        if (Trainer != null)
            _writer.Write(Trainer);
        if (INST_ROM != null)
            _writer.Write(INST_ROM);
    }

    /// <summary>
    /// Calculates the current ROM address accounting for any header offset.
    /// </summary>
    private ushort CurrentAddress => (ushort)(_writer.BaseStream.Position - CodeBaseOffset + BaseAddress);

    private void SetLabel(string name, ushort address)
    {
        if (_hasPresetLabels)
            return;
        Labels[name] = address;
    }

    /// <summary>
    /// Writes a Block to the stream, resolving any label references using the Labels dictionary.
    /// </summary>
    public void WriteBlock(Block block)
    {
        ushort currentAddress = CurrentAddress;
        
        // Set label for this block if it has one (with optional offset for prefix instructions)
        if (block.Label != null)
        {
            SetLabel(block.Label, (ushort)(currentAddress + block.LabelOffset));
        }
        
        // Build a local label table for intra-block labels
        var localLabels = new Dictionary<string, ushort>();
        ushort addr = currentAddress;
        
        // First pass: calculate addresses for local labels
        foreach (var (instruction, label) in block.InstructionsWithLabels)
        {
            if (label != null)
            {
                localLabels[label] = addr;
            }
            addr += (ushort)instruction.Size;
        }
        
        // Second pass: emit bytes
        addr = currentAddress;
        foreach (var (instruction, _) in block.InstructionsWithLabels)
        {
            byte opcode = OpcodeTable.Encode(instruction.Opcode, instruction.Mode);
            _writer.Write(opcode);
            
            if (instruction.Operand != null)
            {
                switch (instruction.Operand)
                {
                    case ImmediateOperand imm:
                        _writer.Write(imm.Value);
                        break;
                        
                    case AbsoluteOperand abs:
                        _writer.Write(abs.Address);
                        break;
                        
                    case LabelOperand labelOp:
                        // Try local labels first, then global Labels dictionary
                        if (localLabels.TryGetValue(labelOp.Label, out ushort labelAddr))
                        {
                            _writer.Write(labelAddr);
                        }
                        else if (Labels.TryGetValue(labelOp.Label, out labelAddr))
                        {
                            _writer.Write(labelAddr);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label: {labelOp.Label}");
                        }
                        break;
                        
                    case RelativeOperand relOp:
                        // Resolve relative branch to label
                        ushort targetAddr;
                        if (localLabels.TryGetValue(relOp.Label, out targetAddr))
                        {
                            // Calculate relative offset from instruction following this one
                            int offset = targetAddr - (addr + 2); // +2 for opcode + operand
                            if (offset < -128 || offset > 127)
                                throw new InvalidOperationException($"Branch to {relOp.Label} out of range: {offset}");
                            _writer.Write((byte)(sbyte)offset);
                        }
                        else if (Labels.TryGetValue(relOp.Label, out targetAddr))
                        {
                            int offset = targetAddr - (addr + 2);
                            if (offset < -128 || offset > 127)
                                throw new InvalidOperationException($"Branch to {relOp.Label} out of range: {offset}");
                            _writer.Write((byte)(sbyte)offset);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label: {relOp.Label}");
                        }
                        break;
                        
                    case RelativeByteOperand relByte:
                        _writer.Write((byte)(sbyte)relByte.Offset);
                        break;
                }
            }
            
            addr += (ushort)instruction.Size;
        }
        
        // Track LastLDA for optimization patterns
        if (block.Count > 0)
        {
            var lastInstr = block[block.Count - 1];
            LastLDA = lastInstr.Opcode == Opcode.LDA && lastInstr.Mode == AddressMode.Immediate;
        }
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
