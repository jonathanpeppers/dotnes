using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;
using static dotnes.NESConstants;

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

    protected const ushort BaseAddress = 0x8000;

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

    #region Write Methods

    /// <summary>
    /// Writes an implied instruction (no operand)
    /// </summary>
    public void Write(Opcode opcode, AddressMode mode = AddressMode.Implied)
    {
        var instruction = new Instruction(opcode, mode);
        EmitInstruction(instruction);
        
        _lastLDA = opcode == Opcode.LDA;
        _logger.WriteLine($"{opcode}({(int)opcode:X}) [{mode}]");
    }

    /// <summary>
    /// Writes an instruction with a single byte operand
    /// </summary>
    public void Write(Opcode opcode, AddressMode mode, byte value)
    {
        var operand = new ImmediateOperand(value);
        var instruction = new Instruction(opcode, mode, operand);
        EmitInstruction(instruction);

        _lastLDA = opcode == Opcode.LDA;
        _logger.WriteLine($"{opcode}({(int)opcode:X}) [{mode}] {value:X}");
    }

    /// <summary>
    /// Writes an instruction with an address operand (2 bytes)
    /// </summary>
    public void Write(Opcode opcode, AddressMode mode, ushort address)
    {
        var operand = new AbsoluteOperand(address);
        var instruction = new Instruction(opcode, mode, operand);
        EmitInstruction(instruction);

        _lastLDA = opcode == Opcode.LDA;
        _logger.WriteLine($"{opcode}({(int)opcode:X}) [{mode}] {address:X}");
    }

    /// <summary>
    /// Writes an instruction with a label reference
    /// </summary>
    public void WriteWithLabel(Opcode opcode, AddressMode mode, string label)
    {
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

        _lastLDA = opcode == Opcode.LDA;
        _logger.WriteLine($"{opcode}({(int)opcode:X}) [{mode}] {label}");
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

    public void Dispose()
    {
        // Nothing to dispose - we don't own a stream
    }
}
