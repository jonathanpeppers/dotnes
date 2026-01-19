# 6502 Object Model Specification

## Overview

This document proposes an in-memory object model for 6502 assembly programs that addresses fundamental issues in the current dotnes transpiler. The current implementation writes directly to a binary stream, requiring awkward workarounds like `SeekBack()` to modify previously-emitted code. A proper object model would enable:

- Address-indexable instruction storage
- Easy insertion, deletion, and reordering of instructions
- Automatic address/offset recalculation after modifications
- Support for forward references and label resolution
- Cleaner separation between IL translation and binary emission

## Problem Statement

### Current Architecture Issues

The existing `NESWriter` and `IL2NESWriter` classes write 6502 instructions directly to a `BinaryWriter`. This leads to several problems:

1. **`SeekBack()` Hack**: When the transpiler needs to modify previously-written code (e.g., removing setup instructions after discovering optimizations), it truncates the stream:

   ```csharp
   void SeekBack(int length)
   {
       _writer.BaseStream.SetLength(_writer.BaseStream.Length - length);
   }
   ```

   This is fragile and only works for removing bytes from the end.

2. **Branch Offset Calculation**: `NumberOfInstructionsForBranch()` must write instructions to a temporary location, measure the result, then seek back—effectively doing the work twice:

   ```csharp
   byte NumberOfInstructionsForBranch(int stopAt, ushort sizeOfMain)
   {
       long nesPosition = _writer.BaseStream.Position;
       // ... write instructions temporarily ...
       byte numberOfInstructions = checked((byte)(_writer.BaseStream.Position - nesPosition));
       SeekBack(numberOfInstructions);  // Undo what we just wrote!
       return numberOfInstructions;
   }
   ```

3. **Hardcoded Addresses**: Labels and addresses like `pal_col = 0x823E` must be pre-computed, making it difficult to insert or reorganize code.

4. **No Insertion Support**: There's no way to insert instructions in the middle of a program without rewriting everything after.

## Proposed Architecture

### Core Types

#### `AddressMode` Enum

Represents 6502 addressing modes per the official instruction set:

```csharp
public enum AddressMode
{
    Implied,        // impl:   OPC             (1 byte)
    Accumulator,    // A:      OPC A           (1 byte)
    Immediate,      // #:      OPC #$BB        (2 bytes)
    ZeroPage,       // zpg:    OPC $LL         (2 bytes)
    ZeroPageX,      // zpg,X:  OPC $LL,X       (2 bytes)
    ZeroPageY,      // zpg,Y:  OPC $LL,Y       (2 bytes)
    Absolute,       // abs:    OPC $LLHH       (3 bytes)
    AbsoluteX,      // abs,X:  OPC $LLHH,X     (3 bytes)
    AbsoluteY,      // abs,Y:  OPC $LLHH,Y     (3 bytes)
    Indirect,       // ind:    OPC ($LLHH)     (3 bytes, JMP only)
    IndexedIndirect,// X,ind:  OPC ($LL,X)     (2 bytes)
    IndirectIndexed,// ind,Y:  OPC ($LL),Y     (2 bytes)
    Relative,       // rel:    OPC $BB         (2 bytes, branches)
}
```

#### `Opcode` Enum

Represents 6502 opcodes (similar to existing `NESInstruction`, but without addressing mode encoding):

```csharp
public enum Opcode : byte
{
    ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS,
    CLC, CLD, CLI, CLV, CMP, CPX, CPY, DEC, DEX, DEY, EOR, INC, INX,
    INY, JMP, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP,
    ROL, ROR, RTI, RTS, SBC, SEC, SED, SEI, STA, STX, STY, TAX, TAY,
    TSX, TXA, TXS, TYA
}
```

#### `Operand` Record

Represents an instruction operand which can be a literal value or a label reference:

```csharp
public abstract record Operand
{
    /// <summary>
    /// Size in bytes when encoded (0, 1, or 2)
    /// </summary>
    public abstract int Size { get; }
    
    /// <summary>
    /// Resolves the operand to its byte representation
    /// </summary>
    public abstract byte[] ToBytes(ushort currentAddress, LabelTable labels);
}

public record ImmediateOperand(byte Value) : Operand
{
    public override int Size => 1;
    public override byte[] ToBytes(ushort currentAddress, LabelTable labels) 
        => [Value];
}

public record AbsoluteOperand(ushort Address) : Operand
{
    public override int Size => 2;
    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
        => [(byte)(Address & 0xFF), (byte)(Address >> 8)]; // Little-endian
}

public record LabelOperand(string Label, OperandSize Size) : Operand
{
    public override int Size => Size == OperandSize.Byte ? 1 : 2;
    
    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort address))
            throw new UnresolvedLabelException(Label);
            
        if (Size == OperandSize.Byte)
            return [(byte)address];
        return [(byte)(address & 0xFF), (byte)(address >> 8)];
    }
}

public record RelativeOperand(string Label) : Operand
{
    public override int Size => 1;
    
    public override byte[] ToBytes(ushort currentAddress, LabelTable labels)
    {
        if (!labels.TryResolve(Label, out ushort targetAddress))
            throw new UnresolvedLabelException(Label);
            
        // Relative offset from instruction AFTER this one
        int offset = targetAddress - (currentAddress + 2);
        if (offset < -128 || offset > 127)
            throw new BranchOutOfRangeException(Label, offset);
            
        return [(byte)(sbyte)offset];
    }
}

public enum OperandSize { Byte, Word }
```

#### `Instruction` Record

Represents a single 6502 instruction:

```csharp
public record Instruction(
    Opcode Opcode,
    AddressMode Mode,
    Operand? Operand = null,
    string? Comment = null)
{
    /// <summary>
    /// Total size in bytes (opcode + operand)
    /// </summary>
    public int Size => 1 + (Operand?.Size ?? 0);
    
    /// <summary>
    /// Gets the encoded opcode byte for this instruction + addressing mode
    /// </summary>
    public byte EncodedOpcode => OpcodeTable.Encode(Opcode, Mode);
    
    /// <summary>
    /// Emits the instruction bytes to the given address
    /// </summary>
    public byte[] ToBytes(ushort address, LabelTable labels)
    {
        var result = new byte[Size];
        result[0] = EncodedOpcode;
        if (Operand != null)
        {
            var operandBytes = Operand.ToBytes(address, labels);
            operandBytes.CopyTo(result, 1);
        }
        return result;
    }
}
```

#### `LabelTable` Class

Manages labels and their resolved addresses:

```csharp
public class LabelTable
{
    private readonly Dictionary<string, ushort> _labels = new();
    private readonly HashSet<string> _unresolvedReferences = new();
    
    public void Define(string name, ushort address)
    {
        if (_labels.ContainsKey(name))
            throw new DuplicateLabelException(name);
        _labels[name] = address;
        _unresolvedReferences.Remove(name);
    }
    
    public bool TryResolve(string name, out ushort address)
    {
        if (_labels.TryGetValue(name, out address))
            return true;
        _unresolvedReferences.Add(name);
        return false;
    }
    
    public IReadOnlyCollection<string> UnresolvedReferences => _unresolvedReferences;
    
    public void Clear()
    {
        _labels.Clear();
        _unresolvedReferences.Clear();
    }
}
```

### Main Container: `Program6502`

The central class that holds an entire 6502 program:

```csharp
public class Program6502
{
    private readonly List<Block> _blocks = new();
    private readonly LabelTable _labels = new();
    
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
    /// Total size in bytes
    /// </summary>
    public int TotalSize => _blocks.Sum(b => b.Size);
    
    /// <summary>
    /// Indexer to get/set instruction at a specific address
    /// </summary>
    public Instruction? this[ushort address]
    {
        get => FindInstructionAt(address);
        set => ReplaceInstructionAt(address, value);
    }
    
    /// <summary>
    /// Creates a new named block (subroutine)
    /// </summary>
    public Block CreateBlock(string? label = null)
    {
        var block = new Block(label);
        _blocks.Add(block);
        return block;
    }
    
    /// <summary>
    /// Inserts a block at a specific index
    /// </summary>
    public void InsertBlock(int index, Block block)
    {
        _blocks.Insert(index, block);
        InvalidateAddresses();
    }
    
    /// <summary>
    /// Removes a block
    /// </summary>
    public bool RemoveBlock(Block block)
    {
        bool removed = _blocks.Remove(block);
        if (removed) InvalidateAddresses();
        return removed;
    }
    
    /// <summary>
    /// Recalculates all addresses and resolves labels
    /// </summary>
    public void ResolveAddresses()
    {
        _labels.Clear();
        ushort currentAddress = BaseAddress;
        
        foreach (var block in _blocks)
        {
            if (block.Label != null)
                _labels.Define(block.Label, currentAddress);
                
            foreach (var (instruction, label) in block.InstructionsWithLabels)
            {
                if (label != null)
                    _labels.Define(label, currentAddress);
                currentAddress += (ushort)instruction.Size;
            }
        }
    }
    
    /// <summary>
    /// Emits the program to a byte array
    /// </summary>
    public byte[] ToBytes()
    {
        ResolveAddresses();
        
        using var ms = new MemoryStream(TotalSize);
        ushort currentAddress = BaseAddress;
        
        foreach (var block in _blocks)
        {
            foreach (var (instruction, _) in block.InstructionsWithLabels)
            {
                var bytes = instruction.ToBytes(currentAddress, _labels);
                ms.Write(bytes);
                currentAddress += (ushort)bytes.Length;
            }
        }
        
        return ms.ToArray();
    }
    
    /// <summary>
    /// Finds all instructions that reference a given label
    /// </summary>
    public IEnumerable<(Block block, int index, Instruction instruction)> 
        FindReferencesTo(string label)
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
    
    private void InvalidateAddresses()
    {
        // Mark that addresses need recalculation
    }
}
```

#### `Block` Class

Represents a contiguous sequence of instructions, typically a subroutine:

```csharp
public class Block
{
    private readonly List<(Instruction Instruction, string? Label)> _instructions = new();
    
    public Block(string? label = null)
    {
        Label = label;
    }
    
    /// <summary>
    /// Optional label for this block (e.g., subroutine name)
    /// </summary>
    public string? Label { get; set; }
    
    /// <summary>
    /// Number of instructions
    /// </summary>
    public int Count => _instructions.Count;
    
    /// <summary>
    /// Total size in bytes
    /// </summary>
    public int Size => _instructions.Sum(i => i.Instruction.Size);
    
    /// <summary>
    /// Gets instruction at index
    /// </summary>
    public Instruction this[int index] => _instructions[index].Instruction;
    
    /// <summary>
    /// All instructions with their labels
    /// </summary>
    public IEnumerable<(Instruction Instruction, string? Label)> InstructionsWithLabels
        => _instructions;
    
    /// <summary>
    /// Adds an instruction at the end
    /// </summary>
    public Block Emit(Instruction instruction, string? label = null)
    {
        _instructions.Add((instruction, label));
        return this; // Fluent API
    }
    
    /// <summary>
    /// Inserts an instruction at the specified index
    /// </summary>
    public void Insert(int index, Instruction instruction, string? label = null)
    {
        _instructions.Insert(index, (instruction, label));
    }
    
    /// <summary>
    /// Removes instruction at index
    /// </summary>
    public void RemoveAt(int index)
    {
        _instructions.RemoveAt(index);
    }
    
    /// <summary>
    /// Removes the last N instructions (replaces SeekBack!)
    /// </summary>
    public void RemoveLast(int count = 1)
    {
        for (int i = 0; i < count && _instructions.Count > 0; i++)
            _instructions.RemoveAt(_instructions.Count - 1);
    }
    
    /// <summary>
    /// Replaces instruction at index
    /// </summary>
    public void Replace(int index, Instruction newInstruction)
    {
        var (_, label) = _instructions[index];
        _instructions[index] = (newInstruction, label);
    }
}
```

### Builder API (Fluent Interface)

For convenient instruction emission:

```csharp
public static class InstructionBuilder
{
    // Implied addressing
    public static Instruction BRK() => new(Opcode.BRK, AddressMode.Implied);
    public static Instruction CLC() => new(Opcode.CLC, AddressMode.Implied);
    public static Instruction RTS() => new(Opcode.RTS, AddressMode.Implied);
    public static Instruction NOP() => new(Opcode.NOP, AddressMode.Implied);
    public static Instruction PHA() => new(Opcode.PHA, AddressMode.Implied);
    public static Instruction PLA() => new(Opcode.PLA, AddressMode.Implied);
    public static Instruction TAX() => new(Opcode.TAX, AddressMode.Implied);
    public static Instruction TAY() => new(Opcode.TAY, AddressMode.Implied);
    public static Instruction TXA() => new(Opcode.TXA, AddressMode.Implied);
    public static Instruction TYA() => new(Opcode.TYA, AddressMode.Implied);
    public static Instruction INX() => new(Opcode.INX, AddressMode.Implied);
    public static Instruction INY() => new(Opcode.INY, AddressMode.Implied);
    public static Instruction DEX() => new(Opcode.DEX, AddressMode.Implied);
    public static Instruction DEY() => new(Opcode.DEY, AddressMode.Implied);
    // ... etc
    
    // Immediate addressing
    public static Instruction LDA(byte value) => 
        new(Opcode.LDA, AddressMode.Immediate, new ImmediateOperand(value));
    public static Instruction LDX(byte value) => 
        new(Opcode.LDX, AddressMode.Immediate, new ImmediateOperand(value));
    public static Instruction LDY(byte value) => 
        new(Opcode.LDY, AddressMode.Immediate, new ImmediateOperand(value));
    public static Instruction CMP(byte value) => 
        new(Opcode.CMP, AddressMode.Immediate, new ImmediateOperand(value));
    public static Instruction AND(byte value) => 
        new(Opcode.AND, AddressMode.Immediate, new ImmediateOperand(value));
    // ... etc
    
    // Zero page addressing
    public static Instruction LDA_zpg(byte address) => 
        new(Opcode.LDA, AddressMode.ZeroPage, new ImmediateOperand(address));
    public static Instruction STA_zpg(byte address) => 
        new(Opcode.STA, AddressMode.ZeroPage, new ImmediateOperand(address));
    public static Instruction INC_zpg(byte address) => 
        new(Opcode.INC, AddressMode.ZeroPage, new ImmediateOperand(address));
    // ... etc
    
    // Absolute addressing
    public static Instruction LDA_abs(ushort address) => 
        new(Opcode.LDA, AddressMode.Absolute, new AbsoluteOperand(address));
    public static Instruction STA_abs(ushort address) => 
        new(Opcode.STA, AddressMode.Absolute, new AbsoluteOperand(address));
    public static Instruction JSR(ushort address) => 
        new(Opcode.JSR, AddressMode.Absolute, new AbsoluteOperand(address));
    public static Instruction JMP(ushort address) => 
        new(Opcode.JMP, AddressMode.Absolute, new AbsoluteOperand(address));
    // ... etc
    
    // Label-based addressing
    public static Instruction JSR(string label) => 
        new(Opcode.JSR, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));
    public static Instruction JMP(string label) => 
        new(Opcode.JMP, AddressMode.Absolute, new LabelOperand(label, OperandSize.Word));
    
    // Relative addressing (branches)
    public static Instruction BNE(string label) => 
        new(Opcode.BNE, AddressMode.Relative, new RelativeOperand(label));
    public static Instruction BEQ(string label) => 
        new(Opcode.BEQ, AddressMode.Relative, new RelativeOperand(label));
    public static Instruction BCS(string label) => 
        new(Opcode.BCS, AddressMode.Relative, new RelativeOperand(label));
    public static Instruction BCC(string label) => 
        new(Opcode.BCC, AddressMode.Relative, new RelativeOperand(label));
    public static Instruction BMI(string label) => 
        new(Opcode.BMI, AddressMode.Relative, new RelativeOperand(label));
    public static Instruction BPL(string label) => 
        new(Opcode.BPL, AddressMode.Relative, new RelativeOperand(label));
    // ... etc
}
```

### Opcode Encoding Table

Maps (Opcode, AddressMode) pairs to their byte encoding:

```csharp
public static class OpcodeTable
{
    private static readonly Dictionary<(Opcode, AddressMode), byte> _encodings = new()
    {
        // ADC
        { (Opcode.ADC, AddressMode.Immediate),       0x69 },
        { (Opcode.ADC, AddressMode.ZeroPage),        0x65 },
        { (Opcode.ADC, AddressMode.ZeroPageX),       0x75 },
        { (Opcode.ADC, AddressMode.Absolute),        0x6D },
        { (Opcode.ADC, AddressMode.AbsoluteX),       0x7D },
        { (Opcode.ADC, AddressMode.AbsoluteY),       0x79 },
        { (Opcode.ADC, AddressMode.IndexedIndirect), 0x61 },
        { (Opcode.ADC, AddressMode.IndirectIndexed), 0x71 },
        
        // AND
        { (Opcode.AND, AddressMode.Immediate),       0x29 },
        { (Opcode.AND, AddressMode.ZeroPage),        0x25 },
        { (Opcode.AND, AddressMode.ZeroPageX),       0x35 },
        { (Opcode.AND, AddressMode.Absolute),        0x2D },
        { (Opcode.AND, AddressMode.AbsoluteX),       0x3D },
        { (Opcode.AND, AddressMode.AbsoluteY),       0x39 },
        { (Opcode.AND, AddressMode.IndexedIndirect), 0x21 },
        { (Opcode.AND, AddressMode.IndirectIndexed), 0x31 },
        
        // LDA
        { (Opcode.LDA, AddressMode.Immediate),       0xA9 },
        { (Opcode.LDA, AddressMode.ZeroPage),        0xA5 },
        { (Opcode.LDA, AddressMode.ZeroPageX),       0xB5 },
        { (Opcode.LDA, AddressMode.Absolute),        0xAD },
        { (Opcode.LDA, AddressMode.AbsoluteX),       0xBD },
        { (Opcode.LDA, AddressMode.AbsoluteY),       0xB9 },
        { (Opcode.LDA, AddressMode.IndexedIndirect), 0xA1 },
        { (Opcode.LDA, AddressMode.IndirectIndexed), 0xB1 },
        
        // STA
        { (Opcode.STA, AddressMode.ZeroPage),        0x85 },
        { (Opcode.STA, AddressMode.ZeroPageX),       0x95 },
        { (Opcode.STA, AddressMode.Absolute),        0x8D },
        { (Opcode.STA, AddressMode.AbsoluteX),       0x9D },
        { (Opcode.STA, AddressMode.AbsoluteY),       0x99 },
        { (Opcode.STA, AddressMode.IndexedIndirect), 0x81 },
        { (Opcode.STA, AddressMode.IndirectIndexed), 0x91 },
        
        // Branches (all relative)
        { (Opcode.BCC, AddressMode.Relative), 0x90 },
        { (Opcode.BCS, AddressMode.Relative), 0xB0 },
        { (Opcode.BEQ, AddressMode.Relative), 0xF0 },
        { (Opcode.BMI, AddressMode.Relative), 0x30 },
        { (Opcode.BNE, AddressMode.Relative), 0xD0 },
        { (Opcode.BPL, AddressMode.Relative), 0x10 },
        { (Opcode.BVC, AddressMode.Relative), 0x50 },
        { (Opcode.BVS, AddressMode.Relative), 0x70 },
        
        // Implied
        { (Opcode.BRK, AddressMode.Implied), 0x00 },
        { (Opcode.CLC, AddressMode.Implied), 0x18 },
        { (Opcode.CLD, AddressMode.Implied), 0xD8 },
        { (Opcode.CLI, AddressMode.Implied), 0x58 },
        { (Opcode.CLV, AddressMode.Implied), 0xB8 },
        { (Opcode.DEX, AddressMode.Implied), 0xCA },
        { (Opcode.DEY, AddressMode.Implied), 0x88 },
        { (Opcode.INX, AddressMode.Implied), 0xE8 },
        { (Opcode.INY, AddressMode.Implied), 0xC8 },
        { (Opcode.NOP, AddressMode.Implied), 0xEA },
        { (Opcode.PHA, AddressMode.Implied), 0x48 },
        { (Opcode.PHP, AddressMode.Implied), 0x08 },
        { (Opcode.PLA, AddressMode.Implied), 0x68 },
        { (Opcode.PLP, AddressMode.Implied), 0x28 },
        { (Opcode.RTI, AddressMode.Implied), 0x40 },
        { (Opcode.RTS, AddressMode.Implied), 0x60 },
        { (Opcode.SEC, AddressMode.Implied), 0x38 },
        { (Opcode.SED, AddressMode.Implied), 0xF8 },
        { (Opcode.SEI, AddressMode.Implied), 0x78 },
        { (Opcode.TAX, AddressMode.Implied), 0xAA },
        { (Opcode.TAY, AddressMode.Implied), 0xA8 },
        { (Opcode.TSX, AddressMode.Implied), 0xBA },
        { (Opcode.TXA, AddressMode.Implied), 0x8A },
        { (Opcode.TXS, AddressMode.Implied), 0x9A },
        { (Opcode.TYA, AddressMode.Implied), 0x98 },
        
        // JSR/JMP
        { (Opcode.JSR, AddressMode.Absolute),  0x20 },
        { (Opcode.JMP, AddressMode.Absolute),  0x4C },
        { (Opcode.JMP, AddressMode.Indirect),  0x6C },
        
        // ... complete table for all instructions
    };
    
    public static byte Encode(Opcode opcode, AddressMode mode)
    {
        if (_encodings.TryGetValue((opcode, mode), out byte encoding))
            return encoding;
        throw new InvalidOpcodeAddressModeException(opcode, mode);
    }
    
    public static (Opcode, AddressMode) Decode(byte encoding)
    {
        foreach (var (key, value) in _encodings)
        {
            if (value == encoding)
                return key;
        }
        throw new UnknownOpcodeException(encoding);
    }
}
```

## Usage Examples

### Example 1: Replacing `SeekBack()`

**Before (current code):**
```csharp
case ILOpCode.Newarr:
    if (previous == ILOpCode.Ldc_i4_s)
    {
        SeekBack(2);  // Remove the LDA instruction we just wrote
    }
    break;
```

**After (with object model):**
```csharp
case ILOpCode.Newarr:
    if (previous == ILOpCode.Ldc_i4_s)
    {
        _currentBlock.RemoveLast(1);  // Remove the last instruction
    }
    break;
```

### Example 2: Branch Offset Calculation

**Before (current code):**
```csharp
byte NumberOfInstructionsForBranch(int stopAt, ushort sizeOfMain)
{
    long nesPosition = _writer.BaseStream.Position;
    // ... write instructions temporarily ...
    byte numberOfInstructions = checked((byte)(_writer.BaseStream.Position - nesPosition));
    SeekBack(numberOfInstructions);  // Undo!
    return numberOfInstructions;
}
```

**After (with object model):**
```csharp
// No need to calculate offsets manually!
// Just use label-based branches:
_currentBlock
    .Emit(InstructionBuilder.CMP(value))
    .Emit(InstructionBuilder.BNE("loopStart"), "loopCondition");

// The Program6502.ResolveAddresses() method handles offset calculation
```

### Example 3: Writing `pal_col` Subroutine

**Before (current code):**
```csharp
case nameof(NESLib.pal_col):
    Write(NESInstruction.STA_zpg, TEMP);
    Write(NESInstruction.JSR, popa.GetAddressAfterMain(sizeOfMain));
    Write(NESInstruction.AND, 0x1F);
    Write(NESInstruction.TAX_impl);
    Write(NESInstruction.LDA_zpg, TEMP);
    Write(NESInstruction.STA_abs_X, PAL_BUF);
    Write(NESInstruction.INC_zpg, PAL_UPDATE);
    Write(NESInstruction.RTS_impl);
    break;
```

**After (with object model):**
```csharp
var palCol = program.CreateBlock("pal_col");
palCol
    .Emit(STA_zpg(TEMP))
    .Emit(JSR("popa"))       // Label reference, resolved later
    .Emit(AND(0x1F))
    .Emit(TAX())
    .Emit(LDA_zpg(TEMP))
    .Emit(STA_abs_X(PAL_BUF))
    .Emit(INC_zpg(PAL_UPDATE))
    .Emit(RTS());
```

### Example 4: Inserting Instructions

```csharp
// Find a block and insert a NOP at the beginning
var mainBlock = program.Blocks.First(b => b.Label == "main");
mainBlock.Insert(0, NOP(), "debugBreakpoint");

// Addresses are automatically recalculated when ToBytes() is called
```

### Example 5: Finding and Modifying References

```csharp
// Rename a label and update all references
string oldLabel = "oldSubroutine";
string newLabel = "newSubroutine";

// Find the block
var block = program.Blocks.FirstOrDefault(b => b.Label == oldLabel);
if (block != null)
{
    block.Label = newLabel;
    
    // Update all JSR/JMP instructions that reference it
    foreach (var (refBlock, idx, instr) in program.FindReferencesTo(oldLabel))
    {
        var newOperand = instr.Operand switch
        {
            LabelOperand lo => new LabelOperand(newLabel, lo.Size),
            RelativeOperand _ => new RelativeOperand(newLabel),
            _ => instr.Operand
        };
        refBlock.Replace(idx, instr with { Operand = newOperand });
    }
}
```

## Migration Strategy

### Phase 1: Create Object Model (Non-Breaking) ✅ COMPLETED

1. ✅ Implemented all types in new files under `src/dotnes.tasks/ObjectModel/`:
   - `AddressMode.cs` - 13 addressing mode enum
   - `Opcode.cs` - 56 opcode mnemonic enum
   - `Operand.cs` - Base operand class with ImmediateOperand, AbsoluteOperand, LabelOperand, RelativeOperand, RelativeByteOperand
   - `Instruction.cs` - Immutable instruction record with ToBytes() encoding
   - `LabelTable.cs` - Label definition and resolution
   - `Block.cs` - Instruction container with insert/remove/replace operations
   - `Program6502.cs` - Main container with address resolution and byte emission
   - `OpcodeTable.cs` - Complete encode/decode table for all 151 valid opcode/mode combinations
   - `InstructionBuilder.cs` - Fluent `Asm` static class for creating instructions
   - `Exceptions.cs` - Custom exceptions for error handling
2. ✅ Added comprehensive unit tests in `src/dotnes.tests/ObjectModelTests.cs` (42 tests)
3. ✅ Kept existing `NESWriter` and `IL2NESWriter` unchanged

### Phase 2: Add Adapter Layer ✅ COMPLETED

1. ✅ Created `Program6502Writer` in `src/dotnes.tasks/ObjectModel/Program6502Writer.cs`:
   - Wraps `Program6502` and provides `NESWriter`-compatible API
   - `Write(NESInstruction)` / `Write(NESInstruction, byte)` / `Write(NESInstruction, ushort)` for legacy compatibility
   - `WriteWithLabel()` for label-based addressing (replaces pre-computed offsets)
   - `DefineLabel()` / `DefineExternalLabel()` for label management
   - `RemoveLastInstructions()` replaces `SeekBack()` pattern
   - `Emit()` fluent API for new code using `Asm` builder
   - `CreateBlock()` for subroutine organization
   - `ToBytes()` converts object model to binary output
   - `Disassemble()` for debugging
   - `Validate()` returns list of unresolved labels
   - `ConvertNESInstruction()` maps all ~70 existing `NESInstruction` enum values to `Opcode`/`AddressMode` pairs
2. ✅ Added comprehensive unit tests in `src/dotnes.tests/Program6502WriterTests.cs` (47 tests)
3. ✅ All 151 tests in test suite pass

### Phase 3: Migrate Built-in Subroutines ✅ COMPLETED

1. ✅ Created `BuiltInSubroutines` class in `src/dotnes.tasks/ObjectModel/BuiltInSubroutines.cs`:
   - Factory methods returning `Block` templates for each NESLib built-in subroutine
   - **Core System**: `Exit()`, `InitPPU()`, `ClearPalette()`, `ClearVRAM()`, `WaitSync3()`, `Nmi()`, `Irq()`
   - **Palette**: `PalAll()`, `PalCopy()`, `PalBg()`, `PalSpr()`, `PalCol()`, `PalClear()`, `PalSprBright()`, `PalBgBright()`, `PalBright()`
   - **PPU Control**: `PpuOff()`, `PpuOnAll()`, `PpuOnOff()`, `PpuOnBg()`, `PpuOnSpr()`, `PpuMask()`, `PpuWaitNmi()`, `PpuWaitFrame()`, `PpuSystem()`, `GetPpuCtrlVar()`, `SetPpuCtrlVar()`
   - **OAM**: `OamClear()`, `OamSize()`, `OamHideRest()`, `OamSpr()`
   - **Scroll/Bank**: `Scroll()`, `BankSpr()`, `BankBg()`
   - **VRAM**: `VramAdr()`, `VramPut()`, `VramFill()`, `VramInc()`, `VramWrite()`, `SetVramUpdate()`, `FlushVramUpdate()`
   - **Timing**: `NesClock()`, `Delay()`, `NmiSetCallback()`
   - **Stack**: `Popa()`, `Popax()`, `Pusha()`, `Pushax()`, `Incsp2()`
   - **Init**: `Initlib()`, `Donelib()`, `Copydata()`, `Zerobss()`
   - **Input**: `PadPoll()`
2. ✅ Extended `InstructionBuilder` (`Asm` class) with additional methods:
   - `JMP_abs(string)` for label-based jumps
   - `ROR_X_zpg()`, `EOR_Y_abs()`, `AND_Y_abs()` and other indexed modes
3. ✅ Added `NESConstants.cs` - shared constants between `NESWriter` and object model
4. ✅ Added comprehensive unit tests in `src/dotnes.tests/BuiltInSubroutinesTests.cs` (25 tests)
5. ✅ All 176 tests pass

### Phase 4: Migrate IL Translation

1. Update `IL2NESWriter` to emit to `Block` objects instead of stream
2. Remove `SeekBack()` usage
3. Use label-based branches instead of pre-calculated offsets

### Phase 5: Cleanup

1. Remove old `NESWriter` stream-based instruction emission
2. Consolidate `NESInstruction` enum with new `Opcode` enum
3. Update all documentation

## Benefits

1. **No More `SeekBack()`**: Simply remove instructions from the block
2. **Automatic Address Resolution**: Labels are resolved at emit time
3. **Forward References**: Can reference labels before they're defined
4. **Insertions/Deletions**: Easy to modify programs without rewriting
5. **Testability**: Can inspect program structure without emitting bytes
6. **Optimization Passes**: Can implement peephole optimization on the object model
7. **Debugging**: Can pretty-print disassembly at any time
8. **Reusability**: Built-in subroutines can be stored as templates

## Future Enhancements

### Optimization Passes

```csharp
public interface IOptimizationPass
{
    void Optimize(Program6502 program);
}

public class RedundantLoadElimination : IOptimizationPass
{
    public void Optimize(Program6502 program)
    {
        foreach (var block in program.Blocks)
        {
            for (int i = 1; i < block.Count; i++)
            {
                // Remove LDA $XX immediately followed by another LDA
                if (block[i-1].Opcode == Opcode.LDA && 
                    block[i].Opcode == Opcode.LDA)
                {
                    block.RemoveAt(i - 1);
                    i--;
                }
            }
        }
    }
}
```

### Disassembler

```csharp
public static class Disassembler
{
    public static string Disassemble(Program6502 program)
    {
        var sb = new StringBuilder();
        program.ResolveAddresses();
        
        ushort address = program.BaseAddress;
        foreach (var block in program.Blocks)
        {
            if (block.Label != null)
                sb.AppendLine($"{block.Label}:");
                
            foreach (var (instr, label) in block.InstructionsWithLabels)
            {
                if (label != null)
                    sb.AppendLine($"{label}:");
                    
                sb.AppendLine($"  ${address:X4}  {FormatInstruction(instr)}");
                address += (ushort)instr.Size;
            }
        }
        return sb.ToString();
    }
}
```

## References

- [6502 Instruction Set Reference](https://www.masswerk.at/6502/6502_instruction_set.html)
- [NES Dev Wiki - iNES Format](https://wiki.nesdev.org/w/index.php/INES)
- [8bitworkshop](https://8bitworkshop.com) - Online NES IDE
- Current dotnes source: [IL2NESWriter.cs](../src/dotnes.tasks/Utilities/IL2NESWriter.cs), [NESWriter.cs](../src/dotnes.tasks/Utilities/NESWriter.cs)
