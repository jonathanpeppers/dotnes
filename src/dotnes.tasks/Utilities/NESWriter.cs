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
    /// Block to buffer emitted instructions for deferred writing.
    /// When non-null, Emit methods add instructions here instead of writing to stream.
    /// </summary>
    protected Block? _bufferedBlock;
    
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
        // Write brightness tables as data blocks
        WriteBlock(Block.FromRawData(NESLib.palBrightTableL));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable0));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable1));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable2));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable3));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable4));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable5));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable6));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable7));
        WriteBlock(Block.FromRawData(NESLib.palBrightTable8));
        WriteBlock(BuiltInSubroutines.Initlib());
    }

    public void WriteDestructorTable()
    {
        WriteBlock(BuiltInSubroutines.DestructorTable());
    }

    /// <summary>
    /// These are any subroutines after our `static void main()` method.
    /// Uses Program6502.AddFinalBuiltIns to centralize the block definitions.
    /// </summary>
    public void WriteFinalBuiltIns(ushort totalSize, byte locals)
    {
        // Create a temporary Program6502 with final built-ins
        var finalBuiltIns = new Program6502();
        finalBuiltIns.AddFinalBuiltIns(totalSize, locals, UsedMethods);

        // Write each block using WriteBlock (which handles label resolution)
        foreach (var block in finalBuiltIns.Blocks)
        {
            WriteBlock(block);
        }
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
        // Skip setting if labels are preset AND the label already has a non-zero value
        // (allows updating forward references that were initialized to 0)
        if (_hasPresetLabels && Labels.TryGetValue(name, out var existing) && existing != 0)
            return;
        Labels[name] = address;
    }

    /// <summary>
    /// Writes a Block to the stream, resolving any label references using the Labels dictionary.
    /// Supports both instruction blocks and data blocks.
    /// </summary>
    public void WriteBlock(Block block)
    {
        ushort currentAddress = CurrentAddress;
        
        // Set label for this block if it has one (with optional offset for prefix instructions)
        if (block.Label != null)
        {
            SetLabel(block.Label, (ushort)(currentAddress + block.LabelOffset));
        }

        // Handle data blocks (raw bytes)
        if (block.IsDataBlock && block.RawData != null)
        {
            _writer.Write(block.RawData);
            LastLDA = false;
            return;
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
                    
                    case LowByteOperand lowOp:
                        // Resolve to low byte of label address
                        if (localLabels.TryGetValue(lowOp.Label, out ushort lowAddr))
                        {
                            _writer.Write((byte)(lowAddr & 0xFF));
                        }
                        else if (Labels.TryGetValue(lowOp.Label, out lowAddr))
                        {
                            _writer.Write((byte)(lowAddr & 0xFF));
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label for low byte: {lowOp.Label}");
                        }
                        break;
                    
                    case HighByteOperand highOp:
                        // Resolve to high byte of label address
                        if (localLabels.TryGetValue(highOp.Label, out ushort highAddr))
                        {
                            _writer.Write((byte)(highAddr >> 8));
                        }
                        else if (Labels.TryGetValue(highOp.Label, out highAddr))
                        {
                            _writer.Write((byte)(highAddr >> 8));
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unresolved label for high byte: {highOp.Label}");
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

    /// <summary>
    /// Starts buffering instructions to a block instead of writing directly to stream.
    /// </summary>
    public void StartBlockBuffering()
    {
        _bufferedBlock = new Block();
    }

    /// <summary>
    /// Flushes the buffered block to the stream and stops block buffering.
    /// </summary>
    public void FlushBufferedBlock()
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("FlushBufferedBlock called but not in block buffering mode");
        
        WriteBlock(_bufferedBlock);
        _bufferedBlock = null;
    }

    /// <summary>
    /// Gets the current size of the buffered block in bytes.
    /// </summary>
    protected int GetBufferedBlockSize() => _bufferedBlock?.Size ?? 0;

    /// <summary>
    /// Removes the last N instructions from the buffered block.
    /// </summary>
    protected void RemoveLastInstructions(int count)
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("RemoveLastInstructions called but not in block buffering mode");
        
        LastLDA = false;
        _logger.WriteLine($"Removing last {count} instruction(s) from block");
        _bufferedBlock.RemoveLast(count);
    }

    /// <summary>
    /// Emits an implied instruction to the buffered block.
    /// </summary>
    protected void Emit(Opcode opcode, AddressMode mode = AddressMode.Implied)
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("Emit requires block buffering mode. Call StartBlockBuffering() first.");
        
        _bufferedBlock.Emit(new Instruction(opcode, mode));
        LastLDA = opcode == Opcode.LDA && mode == AddressMode.Immediate;
    }

    /// <summary>
    /// Emits an instruction with a byte operand to the buffered block.
    /// </summary>
    protected void Emit(Opcode opcode, AddressMode mode, byte operand)
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("Emit requires block buffering mode. Call StartBlockBuffering() first.");
        
        Operand op = mode switch
        {
            AddressMode.Immediate => new ImmediateOperand(operand),
            AddressMode.ZeroPage => new ImmediateOperand(operand),
            AddressMode.ZeroPageX => new ImmediateOperand(operand),
            AddressMode.ZeroPageY => new ImmediateOperand(operand),
            AddressMode.Relative => new RelativeByteOperand((sbyte)operand),
            _ => new ImmediateOperand(operand),
        };
        _bufferedBlock.Emit(new Instruction(opcode, mode, op));
        LastLDA = opcode == Opcode.LDA && mode == AddressMode.Immediate;
    }

    /// <summary>
    /// Emits an instruction with an address operand to the buffered block.
    /// </summary>
    protected void Emit(Opcode opcode, AddressMode mode, ushort operand)
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("Emit requires block buffering mode. Call StartBlockBuffering() first.");
        
        _bufferedBlock.Emit(new Instruction(opcode, mode, new AbsoluteOperand(operand)));
        LastLDA = opcode == Opcode.LDA && mode == AddressMode.Immediate;
    }

    /// <summary>
    /// Emits an instruction with a label reference operand to the buffered block.
    /// The label will be resolved to an address when the block is written.
    /// </summary>
    protected void EmitWithLabel(Opcode opcode, AddressMode mode, string label)
    {
        if (_bufferedBlock == null)
            throw new InvalidOperationException("EmitWithLabel requires block buffering mode. Call StartBlockBuffering() first.");
        
        Operand operand = mode switch
        {
            AddressMode.Immediate_LowByte => new LowByteOperand(label),
            AddressMode.Immediate_HighByte => new HighByteOperand(label),
            AddressMode.Relative => new RelativeOperand(label),
            _ => new LabelOperand(label, OperandSize.Word),
        };
        
        _bufferedBlock.Emit(new Instruction(opcode, mode, operand));
        LastLDA = opcode == Opcode.LDA && (mode == AddressMode.Immediate || mode == AddressMode.Immediate_LowByte || mode == AddressMode.Immediate_HighByte);
    }

    /// <summary>
    /// Gets the current count of instructions in the buffered block.
    /// </summary>
    protected int GetBufferedBlockCount() => _bufferedBlock?.Count ?? 0;

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
