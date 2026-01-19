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

        // NOTE: starting values so they exist in dictionary
        Labels[nameof(copydata)] = 0;
        Labels[nameof(popa)] = 0;
        Labels[nameof(popax)] = 0;
        Labels[nameof(pusha)] = 0;
        Labels[nameof(pushax)] = 0;
        Labels[nameof(zerobss)] = 0;
        Labels[nameof(rodata)] = 0;
        Labels[nameof(donelib)] = 0;
        // Fixed address labels (referenced by blocks but not dynamically positioned)
        Labels[nameof(updName)] = NESConstants.updName;
    }

    /// <summary>
    /// PRG_ROM is in 16 KB units
    /// </summary>
    public const int PRG_ROM_BLOCK_SIZE = 16384;
    /// <summary>
    /// CHR ROM in in 8 KB units
    /// </summary>
    public const int CHR_ROM_BLOCK_SIZE = 8192;

    // Post-main functions (these depend on code layout, keep local)
    protected const ushort copydata = 0x850C;
    protected const ushort popa = 0x854F;
    protected const ushort popax = 0x8539;
    protected const ushort pusha = 0x855F;
    protected const ushort pushax = 0x8575;
    protected const ushort zerobss = 0x858B;
    protected const ushort rodata = 0x85AE;
    protected const ushort donelib = 0x84FD;

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
    public void WriteBuiltIns(ushort sizeOfMain)
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
        WriteBuiltIn(nameof(NESLib.pal_all), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_copy), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_col), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_clear), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_spr_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bg_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.pal_bright), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_off), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_all), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_onoff), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_on_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_mask), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_system), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.get_ppu_ctrl_var), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.set_ppu_ctrl_var), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_clear), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_size), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.oam_hide_rest), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_wait_frame), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.ppu_wait_nmi), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.scroll), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.bank_spr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.bank_bg), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_write), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.set_vram_update), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.flush_vram_update), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_adr), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_put), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_fill), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.vram_inc), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.nesclock), sizeOfMain);
        WriteBuiltIn(nameof(NESLib.delay), sizeOfMain);
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
        SetLabel(nameof(donelib), CurrentAddress);
        WriteBlock(BuiltInSubroutines.Donelib(totalSize));

        SetLabel(nameof(copydata), CurrentAddress);
        WriteBlock(BuiltInSubroutines.Copydata(totalSize));

        SetLabel(nameof(popax), CurrentAddress);
        WriteBlock(BuiltInSubroutines.Popax());

        WriteBlock(BuiltInSubroutines.Incsp2());

        SetLabel(nameof(popa), CurrentAddress);
        WriteBlock(BuiltInSubroutines.Popa());

        // pusha label is at offset +4 (after pusha0sp and pushaysp prefixes)
        SetLabel(nameof(pusha), (ushort)(CurrentAddress + 4));
        WriteBlock(BuiltInSubroutines.Pusha());

        // pushax label is at offset +4 (after push0 and pusha0 prefixes)
        SetLabel(nameof(pushax), (ushort)(CurrentAddress + 4));
        WriteBlock(BuiltInSubroutines.Pushax());

        SetLabel(nameof(zerobss), CurrentAddress);
        WriteBlock(BuiltInSubroutines.Zerobss(locals));

        // List of optional methods at the end
        if (UsedMethods is not null)
        {
            if (UsedMethods.Contains(nameof(NESLib.oam_spr)))
            {
                WriteBuiltIn(nameof(NESLib.oam_spr), totalSize);
            }
            if (UsedMethods.Contains(nameof(NESLib.pad_poll)))
            {
                SetLabel(nameof(NESLib.pad_poll), CurrentAddress);
                WriteBlock(BuiltInSubroutines.PadPoll());
            }
        }
    }

    /// <summary>
    /// Writes a built-in method from NESLib
    /// </summary>
    public void WriteBuiltIn(string name, ushort sizeOfMain)
    {
        SetLabel(name, CurrentAddress);

        switch (name)
        {
            case nameof(NESLib.pal_all):
                WriteBlock(BuiltInSubroutines.PalAll());
                break;
            case nameof (NESLib.pal_copy):
                WriteBlock(BuiltInSubroutines.PalCopy());
                break;
            case nameof(NESLib.pal_bg):
                WriteBlock(BuiltInSubroutines.PalBg());
                break;
            case nameof(NESLib.pal_spr):
                WriteBlock(BuiltInSubroutines.PalSpr());
                break;
            case nameof(NESLib.pal_col):
                WriteBlock(BuiltInSubroutines.PalCol());
                break;
            case nameof(NESLib.pal_clear):
                WriteBlock(BuiltInSubroutines.PalClear());
                break;
            case nameof(NESLib.pal_spr_bright):
                WriteBlock(BuiltInSubroutines.PalSprBright());
                break;
            case nameof(NESLib.pal_bg_bright):
                WriteBlock(BuiltInSubroutines.PalBgBright());
                break;
            case nameof(NESLib.pal_bright):
                WriteBlock(BuiltInSubroutines.PalBright());
                break;
            case nameof(NESLib.ppu_off):
                WriteBlock(BuiltInSubroutines.PpuOff());
                break;
            case nameof(NESLib.ppu_on_all):
                // Falls through to ppu_onoff, no RTS
                WriteBlock(BuiltInSubroutines.PpuOnAll());
                break;
            case nameof(NESLib.ppu_onoff):
                // JMP to ppu_wait_nmi (uses constant address)
                WriteBlock(BuiltInSubroutines.PpuOnOff());
                break;
            case nameof(NESLib.ppu_on_bg):
                // BNE backward to ppu_onoff - label should be in Labels dict
                WriteBlock(BuiltInSubroutines.PpuOnBg());
                break;
            case nameof(NESLib.ppu_on_spr):
                // BNE backward to ppu_onoff - label should be in Labels dict
                WriteBlock(BuiltInSubroutines.PpuOnSpr());
                break;
            case nameof(NESLib.ppu_mask):
                WriteBlock(BuiltInSubroutines.PpuMask());
                break;
            case nameof(NESLib.ppu_system):
                WriteBlock(BuiltInSubroutines.PpuSystem());
                break;
            case nameof(NESLib.get_ppu_ctrl_var):
                WriteBlock(BuiltInSubroutines.GetPpuCtrlVar());
                break;
            case nameof(NESLib.set_ppu_ctrl_var):
                WriteBlock(BuiltInSubroutines.SetPpuCtrlVar());
                break;
            case nameof(NESLib.oam_clear):
                WriteBlock(BuiltInSubroutines.OamClear());
                break;
            case nameof(NESLib.oam_size):
                WriteBlock(BuiltInSubroutines.OamSize());
                break;
            case nameof(NESLib.oam_hide_rest):
                WriteBlock(BuiltInSubroutines.OamHideRest());
                break;
            case nameof(NESLib.ppu_wait_frame):
                WriteBlock(BuiltInSubroutines.PpuWaitFrame());
                break;
            case nameof(NESLib.ppu_wait_nmi):
                WriteBlock(BuiltInSubroutines.PpuWaitNmi());
                break;
            case nameof(NESLib.scroll):
                WriteBlock(BuiltInSubroutines.Scroll());
                break;
            case nameof(NESLib.bank_spr):
                WriteBlock(BuiltInSubroutines.BankSpr());
                break;
            case nameof(NESLib.bank_bg):
                WriteBlock(BuiltInSubroutines.BankBg());
                break;
            case nameof(NESLib.vram_write):
                WriteBlock(BuiltInSubroutines.VramWrite());
                break;
            case nameof(NESLib.set_vram_update):
                WriteBlock(BuiltInSubroutines.SetVramUpdate());
                break;
            case nameof(NESLib.flush_vram_update):
                WriteBlock(BuiltInSubroutines.FlushVramUpdate());
                break;
            case nameof(NESLib.vram_adr):
                WriteBlock(BuiltInSubroutines.VramAdr());
                break;
            case nameof(NESLib.vram_put):
                WriteBlock(BuiltInSubroutines.VramPut());
                break;
            case nameof(NESLib.vram_fill):
                WriteBlock(BuiltInSubroutines.VramFill());
                break;
            case nameof(NESLib.vram_inc):
                WriteBlock(BuiltInSubroutines.VramInc());
                break;
            case nameof(NESLib.nesclock):
                WriteBlock(BuiltInSubroutines.NesClock());
                break;
            case nameof(NESLib.delay):
                WriteBlock(BuiltInSubroutines.Delay());
                break;
            case nameof(NESLib.oam_spr):
                WriteBlock(BuiltInSubroutines.OamSpr());
                break;
            default:
                throw new NotImplementedException($"{name} is not implemented!");
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
    protected void WriteBlock(Block block)
    {
        ushort currentAddress = CurrentAddress;
        
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
