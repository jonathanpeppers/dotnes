using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

/// <summary>
/// Adapter that provides a NESWriter-compatible API while building a Program6502 internally.
/// This allows gradual migration from stream-based to object model-based code generation.
/// </summary>
class Program6502Writer : IDisposable
{
    private readonly Program6502 _program;
    private Block _currentBlock;
    private readonly ILogger _logger;
    
    // Track the last instruction for optimization patterns (like consecutive LDA)
    private bool _lastLDA;

    #region NES Constants (copied from NESWriter for compatibility)

    protected const int ZP_START = 0x00;
    protected const int STARTUP = 0x01;
    protected const int NES_PRG_BANKS = 0x02;
    protected const int VRAM_UPDATE = 0x03;
    protected const int NAME_UPD_ADR = 0x04;
    protected const int NAME_UPD_ENABLE = 0x06;
    protected const int PAL_UPDATE = 0x07;
    protected const int PAL_BG_PTR = 0x08;
    protected const int PAL_SPR_PTR = 0x0A;
    protected const int SCROLL_X = 0x0C;
    protected const int SCROLL_Y = 0x0D;
    protected const int TEMP = 0x17;
    protected const int sp = 0x22;
    protected const int ptr1 = 0x2A;
    protected const int ptr2 = 0x2C;
    protected const int tmp1 = 0x32;
    protected const int PRG_FILEOFFS = 0x10;
    protected const int PPU_MASK_VAR = 0x12;
    protected const ushort OAM_BUF = 0x0200;
    protected const ushort PAL_BUF = 0x01C0;
    protected const ushort condes = 0x0300;
    protected const ushort PPU_CTRL = 0x2000;
    protected const ushort PPU_MASK = 0x2001;
    protected const ushort PPU_STATUS = 0x2002;
    protected const ushort PPU_OAM_ADDR = 0x2003;
    protected const ushort PPU_OAM_DATA = 0x2004;
    protected const ushort PPU_SCROLL = 0x2005;
    protected const ushort PPU_ADDR = 0x2006;
    protected const ushort PPU_DATA = 0x2007;
    protected const ushort DMC_FREQ = 0x4010;
    protected const ushort PPU_OAM_DMA = 0x4014;
    protected const ushort PPU_FRAMECNT = 0x4017;

    protected const ushort BaseAddress = 0x8000;

    #endregion

    public Program6502Writer(ushort baseAddress = 0x8000, ILogger? logger = null)
    {
        _program = new Program6502 { BaseAddress = baseAddress };
        _currentBlock = _program.CreateBlock("main");
        _logger = logger ?? new NullLogger();
    }

    /// <summary>
    /// Gets the underlying Program6502 object
    /// </summary>
    public Program6502 Program => _program;

    /// <summary>
    /// Gets the current block being written to
    /// </summary>
    public Block CurrentBlock => _currentBlock;

    /// <summary>
    /// Label table for external/library subroutines
    /// </summary>
    public LabelTable Labels => _program.Labels;

    /// <summary>
    /// Whether the last instruction was LDA (for optimization patterns)
    /// </summary>
    public bool LastLDA => _lastLDA;

    /// <summary>
    /// Current size in bytes of all emitted code
    /// </summary>
    public int CurrentSize => _program.TotalSize;

    #region Block Management

    /// <summary>
    /// Creates a new block and makes it the current block
    /// </summary>
    public Block CreateBlock(string? label = null)
    {
        _currentBlock = _program.CreateBlock(label);
        return _currentBlock;
    }

    /// <summary>
    /// Sets the current block to write to
    /// </summary>
    public void SetCurrentBlock(Block block)
    {
        _currentBlock = block;
    }

    /// <summary>
    /// Defines a label at the current position within the current block
    /// </summary>
    public void DefineLabel(string name)
    {
        // The label will point to the next instruction added
        // We'll emit a "marker" by setting the label on the next emit
        _pendingLabel = name;
    }

    private string? _pendingLabel;

    /// <summary>
    /// Defines an external label (e.g., library subroutine addresses)
    /// </summary>
    public void DefineExternalLabel(string name, ushort address)
    {
        _program.DefineExternalLabel(name, address);
    }

    #endregion

    #region NESInstruction-Compatible Write Methods

    /// <summary>
    /// Writes an implied instruction (no operand)
    /// </summary>
    public void Write(NESInstruction i)
    {
        var (opcode, mode) = ConvertNESInstruction(i);
        var instruction = new Instruction(opcode, mode);
        EmitInstruction(instruction);
        
        _lastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X})");
    }

    /// <summary>
    /// Writes an instruction with a single byte operand
    /// </summary>
    public void Write(NESInstruction i, byte value)
    {
        var (opcode, mode) = ConvertNESInstruction(i);
        var operand = new ImmediateOperand(value);
        var instruction = new Instruction(opcode, mode, operand);
        EmitInstruction(instruction);

        _lastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {value:X}");
    }

    /// <summary>
    /// Writes an instruction with an address operand (2 bytes)
    /// </summary>
    public void Write(NESInstruction i, ushort address)
    {
        var (opcode, mode) = ConvertNESInstruction(i);
        var operand = new AbsoluteOperand(address);
        var instruction = new Instruction(opcode, mode, operand);
        EmitInstruction(instruction);

        _lastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {address:X}");
    }

    /// <summary>
    /// Writes an instruction with a label reference
    /// </summary>
    public void WriteWithLabel(NESInstruction i, string label)
    {
        var (opcode, mode) = ConvertNESInstruction(i);
        Operand operand;
        
        if (mode == AddressMode.Relative)
        {
            operand = new RelativeOperand(label);
        }
        else
        {
            operand = new LabelOperand(label, OperandSize.Word);
        }
        
        var instruction = new Instruction(opcode, mode, operand);
        EmitInstruction(instruction);

        _lastLDA = i == NESInstruction.LDA;
        _logger.WriteLine($"{i}({(int)i:X}) {label}");
    }

    private void EmitInstruction(Instruction instruction)
    {
        _currentBlock.Emit(instruction, _pendingLabel);
        _pendingLabel = null;
    }

    #endregion

    #region Fluent API Methods

    /// <summary>
    /// Emits using the new Asm fluent API directly
    /// </summary>
    public Program6502Writer Emit(Instruction instruction, string? label = null)
    {
        if (_pendingLabel != null)
        {
            _currentBlock.Emit(instruction, _pendingLabel);
            _pendingLabel = null;
        }
        else
        {
            _currentBlock.Emit(instruction, label);
        }

        _lastLDA = instruction.Opcode == Opcode.LDA;
        return this;
    }

    #endregion

    #region SeekBack Replacement

    /// <summary>
    /// Removes the last N instructions from the current block.
    /// This replaces the old SeekBack() pattern.
    /// </summary>
    public void RemoveLastInstructions(int count = 1)
    {
        _currentBlock.RemoveLast(count);
        _logger.WriteLine($"RemoveLast({count})");
    }

    /// <summary>
    /// Gets the byte size of the last N instructions.
    /// Useful for calculating branch offsets without writing/removing instructions.
    /// </summary>
    public int GetSizeOfLastInstructions(int count)
    {
        int size = 0;
        int startIndex = Math.Max(0, _currentBlock.Count - count);
        for (int i = startIndex; i < _currentBlock.Count; i++)
        {
            size += _currentBlock[i].Size;
        }
        return size;
    }

    #endregion

    #region Output Generation

    /// <summary>
    /// Resolves all labels and returns the program as a byte array
    /// </summary>
    public byte[] ToBytes()
    {
        return _program.ToBytes();
    }

    /// <summary>
    /// Writes the program bytes to a stream
    /// </summary>
    public void WriteTo(Stream stream)
    {
        _program.WriteTo(stream);
    }

    /// <summary>
    /// Validates all label references
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        return _program.Validate();
    }

    /// <summary>
    /// Returns a disassembly of the program
    /// </summary>
    public string Disassemble()
    {
        return _program.Disassemble();
    }

    #endregion

    #region Raw Data

    /// <summary>
    /// Writes raw byte data (e.g., lookup tables)
    /// </summary>
    public void WriteRawData(byte[] data, string? label = null)
    {
        _program.AddRawData(data, label);
    }

    #endregion

    #region NESInstruction to Opcode/AddressMode Conversion

    /// <summary>
    /// Converts the legacy NESInstruction enum to the new Opcode + AddressMode.
    /// Note: Only maps instructions that exist in the NESInstruction enum.
    /// </summary>
    private static (Opcode opcode, AddressMode mode) ConvertNESInstruction(NESInstruction i)
    {
        // The NESInstruction enum encodes both the opcode and addressing mode
        // We need to map it to our separate Opcode + AddressMode enums
        return i switch
        {
            // ADC
            NESInstruction.ADC => (Opcode.ADC, AddressMode.Immediate),
            NESInstruction.ADC_X_ind => (Opcode.ADC, AddressMode.IndexedIndirect),
            NESInstruction.ADC_X_zpg => (Opcode.ADC, AddressMode.ZeroPage),
            NESInstruction.ADC_abs => (Opcode.ADC, AddressMode.Absolute),

            // AND
            NESInstruction.AND => (Opcode.AND, AddressMode.Immediate),
            NESInstruction.AND_zpg => (Opcode.AND, AddressMode.ZeroPage),
            NESInstruction.AND_abs => (Opcode.AND, AddressMode.Absolute),
            NESInstruction.AND_X_ind => (Opcode.AND, AddressMode.IndexedIndirect),
            NESInstruction.AND_Y_ind => (Opcode.AND, AddressMode.IndirectIndexed),
            NESInstruction.AND_Y_abs => (Opcode.AND, AddressMode.AbsoluteY),

            // ASL
            NESInstruction.ASL_A => (Opcode.ASL, AddressMode.Accumulator),
            NESInstruction.ASL_zpg => (Opcode.ASL, AddressMode.ZeroPage),
            NESInstruction.ASL_zpg_X => (Opcode.ASL, AddressMode.ZeroPageX),
            NESInstruction.ASL_abs => (Opcode.ASL, AddressMode.Absolute),
            NESInstruction.ASL_abs_X => (Opcode.ASL, AddressMode.AbsoluteX),

            // Branches
            NESInstruction.BCC => (Opcode.BCC, AddressMode.Relative),
            NESInstruction.BCS => (Opcode.BCS, AddressMode.Relative),
            NESInstruction.BEQ_rel => (Opcode.BEQ, AddressMode.Relative),
            NESInstruction.BMI => (Opcode.BMI, AddressMode.Relative),
            NESInstruction.BNE_rel => (Opcode.BNE, AddressMode.Relative),
            NESInstruction.BPL => (Opcode.BPL, AddressMode.Relative),

            // BIT
            NESInstruction.BIT_zpg => (Opcode.BIT, AddressMode.ZeroPage),
            NESInstruction.BIT_abs => (Opcode.BIT, AddressMode.Absolute),

            // BRK
            NESInstruction.BRK => (Opcode.BRK, AddressMode.Implied),

            // Clear flags
            NESInstruction.CLC => (Opcode.CLC, AddressMode.Implied),
            NESInstruction.CLD_impl => (Opcode.CLD, AddressMode.Implied),

            // CMP
            NESInstruction.CMP => (Opcode.CMP, AddressMode.Immediate),
            NESInstruction.CMP_zpg => (Opcode.CMP, AddressMode.ZeroPage),
            NESInstruction.CMP_zpg_X => (Opcode.CMP, AddressMode.ZeroPageX),
            NESInstruction.CMP_ind_Y => (Opcode.CMP, AddressMode.IndirectIndexed),
            NESInstruction.CMP_abs_X => (Opcode.CMP, AddressMode.AbsoluteX),
            NESInstruction.CMP_abs_Y => (Opcode.CMP, AddressMode.AbsoluteY),

            // CPX
            NESInstruction.CPX => (Opcode.CPX, AddressMode.Immediate),

            // CPY
            NESInstruction.CPY => (Opcode.CPY, AddressMode.Immediate),

            // DEC
            NESInstruction.DEC_zpg => (Opcode.DEC, AddressMode.ZeroPage),
            NESInstruction.DEC_zpg_X => (Opcode.DEC, AddressMode.ZeroPageX),
            NESInstruction.DEC_abs => (Opcode.DEC, AddressMode.Absolute),
            NESInstruction.DEC_abs_X => (Opcode.DEC, AddressMode.AbsoluteX),

            // DEX, DEY
            NESInstruction.DEX_impl => (Opcode.DEX, AddressMode.Implied),
            NESInstruction.DEY_impl => (Opcode.DEY, AddressMode.Implied),

            // EOR
            NESInstruction.EOR_imm => (Opcode.EOR, AddressMode.Immediate),
            NESInstruction.EOR_zpg => (Opcode.EOR, AddressMode.ZeroPage),
            NESInstruction.EOR_zpg_X => (Opcode.EOR, AddressMode.ZeroPageX),
            NESInstruction.EOR_abs => (Opcode.EOR, AddressMode.Absolute),
            NESInstruction.EOR_X => (Opcode.EOR, AddressMode.AbsoluteX),
            NESInstruction.EOR_Y_abs => (Opcode.EOR, AddressMode.AbsoluteY),
            NESInstruction.EOR_Y_ind => (Opcode.EOR, AddressMode.IndirectIndexed),

            // INC
            NESInstruction.INC_zpg => (Opcode.INC, AddressMode.ZeroPage),
            NESInstruction.INC_abs => (Opcode.INC, AddressMode.Absolute),

            // INX, INY
            NESInstruction.INX_impl => (Opcode.INX, AddressMode.Implied),
            NESInstruction.INY_impl => (Opcode.INY, AddressMode.Implied),

            // JMP
            NESInstruction.JMP_abs => (Opcode.JMP, AddressMode.Absolute),
            NESInstruction.JMP_ind => (Opcode.JMP, AddressMode.Indirect),

            // JSR
            NESInstruction.JSR => (Opcode.JSR, AddressMode.Absolute),

            // LDA
            NESInstruction.LDA => (Opcode.LDA, AddressMode.Immediate),
            NESInstruction.LDA_zpg => (Opcode.LDA, AddressMode.ZeroPage),
            NESInstruction.LDA_abs => (Opcode.LDA, AddressMode.Absolute),
            NESInstruction.LDA_abs_X => (Opcode.LDA, AddressMode.AbsoluteX),
            NESInstruction.LDA_abs_y => (Opcode.LDA, AddressMode.AbsoluteY),
            NESInstruction.LDA_X_ind => (Opcode.LDA, AddressMode.IndexedIndirect),
            NESInstruction.LDA_ind_Y => (Opcode.LDA, AddressMode.IndirectIndexed),

            // LDX
            NESInstruction.LDX => (Opcode.LDX, AddressMode.Immediate),
            NESInstruction.LDX_zpg => (Opcode.LDX, AddressMode.ZeroPage),
            NESInstruction.LDX_abs => (Opcode.LDX, AddressMode.Absolute),

            // LDY
            NESInstruction.LDY => (Opcode.LDY, AddressMode.Immediate),
            NESInstruction.LDY_zpg => (Opcode.LDY, AddressMode.ZeroPage),
            NESInstruction.LDY_abs => (Opcode.LDY, AddressMode.Absolute),

            // LSR
            NESInstruction.LSR_impl => (Opcode.LSR, AddressMode.Accumulator),
            NESInstruction.LSR_zpg => (Opcode.LSR, AddressMode.ZeroPage),
            NESInstruction.LSR_zpg_X => (Opcode.LSR, AddressMode.ZeroPageX),
            NESInstruction.LSR_abs => (Opcode.LSR, AddressMode.Absolute),
            NESInstruction.LSR_abs_X => (Opcode.LSR, AddressMode.AbsoluteX),

            // ORA
            NESInstruction.ORA => (Opcode.ORA, AddressMode.Immediate),
            NESInstruction.ORA_zpg => (Opcode.ORA, AddressMode.ZeroPage),
            NESInstruction.ORA_abs => (Opcode.ORA, AddressMode.Absolute),
            NESInstruction.ORA_X_ind => (Opcode.ORA, AddressMode.IndexedIndirect),

            // Stack
            NESInstruction.PHA_impl => (Opcode.PHA, AddressMode.Implied),
            NESInstruction.PHP_impl => (Opcode.PHP, AddressMode.Implied),
            NESInstruction.PLA_impl => (Opcode.PLA, AddressMode.Implied),
            NESInstruction.PLP_impl => (Opcode.PLP, AddressMode.Implied),

            // ROL
            NESInstruction.ROL_A => (Opcode.ROL, AddressMode.Accumulator),
            NESInstruction.ROL_zpg => (Opcode.ROL, AddressMode.ZeroPage),
            NESInstruction.ROL_abs => (Opcode.ROL, AddressMode.Absolute),

            // ROR
            NESInstruction.ROR_A => (Opcode.ROR, AddressMode.Accumulator),
            NESInstruction.ROR_zpg => (Opcode.ROR, AddressMode.ZeroPage),
            NESInstruction.ROR_abs => (Opcode.ROR, AddressMode.Absolute),
            NESInstruction.ROR_X_zpg => (Opcode.ROR, AddressMode.ZeroPageX),
            NESInstruction.ROR_X_abs => (Opcode.ROR, AddressMode.AbsoluteX),

            // RTI, RTS
            NESInstruction.RTI_impl => (Opcode.RTI, AddressMode.Implied),
            NESInstruction.RTS_impl => (Opcode.RTS, AddressMode.Implied),

            // SBC
            NESInstruction.SBC => (Opcode.SBC, AddressMode.Immediate),

            // Set flags
            NESInstruction.SEC_impl => (Opcode.SEC, AddressMode.Implied),
            NESInstruction.SEI_impl => (Opcode.SEI, AddressMode.Implied),

            // STA
            NESInstruction.STA_zpg => (Opcode.STA, AddressMode.ZeroPage),
            NESInstruction.STA_zpg_X => (Opcode.STA, AddressMode.ZeroPageX),
            NESInstruction.STA_abs => (Opcode.STA, AddressMode.Absolute),
            NESInstruction.STA_abs_X => (Opcode.STA, AddressMode.AbsoluteX),
            NESInstruction.STA_abs_Y => (Opcode.STA, AddressMode.AbsoluteY),
            NESInstruction.STA_X_ind => (Opcode.STA, AddressMode.IndexedIndirect),
            NESInstruction.STA_ind_Y => (Opcode.STA, AddressMode.IndirectIndexed),

            // STX
            NESInstruction.STX_zpg => (Opcode.STX, AddressMode.ZeroPage),
            NESInstruction.STX_abs => (Opcode.STX, AddressMode.Absolute),

            // STY
            NESInstruction.STY_zpg => (Opcode.STY, AddressMode.ZeroPage),
            NESInstruction.STY_abs => (Opcode.STY, AddressMode.Absolute),

            // Transfers
            NESInstruction.TAX_impl => (Opcode.TAX, AddressMode.Implied),
            NESInstruction.TAY_impl => (Opcode.TAY, AddressMode.Implied),
            NESInstruction.TXA_impl => (Opcode.TXA, AddressMode.Implied),
            NESInstruction.TXS_impl => (Opcode.TXS, AddressMode.Implied),
            NESInstruction.TYA_impl => (Opcode.TYA, AddressMode.Implied),

            _ => throw new ArgumentException($"Unknown NESInstruction: {i}", nameof(i))
        };
    }

    #endregion

    public void Dispose()
    {
        // Nothing to dispose - we don't own a stream
    }
}
