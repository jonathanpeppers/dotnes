using System.Collections.Immutable;
using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null)
        : base(stream, leaveOpen, logger)
    {
        _nextLocalAddress = local;
    }

    /// <summary>
    /// The local evaluation stack
    /// </summary>
    readonly Stack<int> Stack = new();
    /// <summary>
    /// Dictionary of local varaiables
    /// </summary>
    readonly Dictionary<int, Local> Locals = new();
    /// <summary>
    /// List of byte[] data (accessible for Program6502 building)
    /// </summary>
    public IReadOnlyList<ImmutableArray<byte>> ByteArrays => _byteArrays;
    readonly List<ImmutableArray<byte>> _byteArrays = new();
    readonly ushort local = 0x325; // cc65 compatibility: locals start at $0325
    readonly ushort tempVar = 0x327; // cc65 compatibility: temp variable at $0327 (after x=$0325, y=$0326)
    readonly ReflectionCache _reflectionCache = new();
    ILOpCode previous;
    
    /// <summary>
    /// Tracks the last byte value loaded into A for optimization (e.g., consecutive stores)
    /// </summary>
    int? _lastLoadedByteValue;
    
    /// <summary>
    /// Tracks if a value has been stored to the temp variable and needs to be reloaded before use
    /// </summary>
    bool _tempVarHasValue;
    
    /// <summary>
    /// Tracks if this is the first use of the temp var value (A still has it, no reload needed)
    /// </summary>
    bool _tempVarFirstUse;
    
    /// <summary>
    /// Tracks the next available memory address for local variables.
    /// Byte arrays don't consume memory addresses (they're in ROM).
    /// </summary>
    ushort _nextLocalAddress;

    /// <summary>
    /// Tracks pending increment/decrement operations for pattern matching
    /// Value: local index to increment/decrement, null = none pending
    /// </summary>
    int? _pendingIncLocalIndex;
    int? _pendingDecLocalIndex;

    /// <summary>
    /// NOTE: may not be exactly correct, this is the instructions inside zerobss:
    /// A925           LDA #$25                      ; zerobss
    /// 852A STA ptr1                      
    /// A903            LDA #$03                      
    /// 852B STA ptr1+1                    
    /// A900            LDA #$00                      
    /// A8              TAY                           
    /// A200 LDX #$00                      
    /// F00A BEQ $85DE                     
    /// 912A STA(ptr1),y                  
    /// C8 INY                           
    /// D0FB BNE $85D4                     
    /// E62B INC ptr1+1                    
    /// CA              DEX                           
    /// D0F6            BNE $85D4                     
    /// C002            CPY #$02
    /// ...
    /// A program with 0 locals has C000
    /// </summary>
    public int LocalCount { get; private set; }

    record Local(int Value, int? Address = null, string? LabelName = null);

    /// <summary>
    /// Emits a JSR to a label reference.
    /// </summary>
    void EmitJSR(string labelName) => EmitWithLabel(Opcode.JSR, AddressMode.Absolute, labelName);

    /// <summary>
    /// Emits a JMP to a label reference.
    /// </summary>
    void EmitJMP(string labelName) => EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);

    public void Write(ILInstruction instruction)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Dup:
                if (Stack.Count > 0)
                    Stack.Push(Stack.Peek());
                break;
            case ILOpCode.Pop:
                if (Stack.Count > 0)
                    Stack.Pop();
                break;
            case ILOpCode.Ldc_i4_0:
                WriteLdc(0);
                break;
            case ILOpCode.Ldc_i4_1:
                WriteLdc(1);
                break;
            case ILOpCode.Ldc_i4_2:
                WriteLdc(2);
                break;
            case ILOpCode.Ldc_i4_3:
                WriteLdc(3);
                break;
            case ILOpCode.Ldc_i4_4:
                WriteLdc(4);
                break;
            case ILOpCode.Ldc_i4_5:
                WriteLdc(5);
                break;
            case ILOpCode.Ldc_i4_6:
                WriteLdc(6);
                break;
            case ILOpCode.Ldc_i4_7:
                WriteLdc(7);
                break;
            case ILOpCode.Ldc_i4_8:
                WriteLdc(8);
                break;
            case ILOpCode.Stloc_0:
                if (previous == ILOpCode.Ldtoken)
                {
                    // Capture label for byte array reference - no memory address used
                    Locals[0] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop(); // Discard marker
                }
                else if (_pendingDecLocalIndex == 0 && Locals.TryGetValue(0, out var loc0) && loc0.Address is not null)
                {
                    // Decrement pattern with known address
                    Stack.Pop();
                    Emit(Opcode.DEC, AddressMode.Absolute, (ushort)loc0.Address);
                    Locals[0] = new Local(loc0.Value - 1, loc0.Address);
                    _pendingDecLocalIndex = null;
                }
                else if (_pendingIncLocalIndex == 0 && Locals.TryGetValue(0, out var loc0i) && loc0i.Address is not null)
                {
                    // Increment pattern with known address
                    Stack.Pop();
                    Emit(Opcode.INC, AddressMode.Absolute, (ushort)loc0i.Address);
                    Locals[0] = new Local(loc0i.Value + 1, loc0i.Address);
                    _pendingIncLocalIndex = null;
                }
                else
                {
                    // First time storing to this local - allocate address
                    var value = Stack.Pop();
                    var address = _nextLocalAddress;
                    _nextLocalAddress += (ushort)(value < byte.MaxValue ? 1 : 2);
                    WriteStloc(Locals[0] = new Local(value, address));
                }
                break;
            case ILOpCode.Stloc_1:
                if (previous == ILOpCode.Ldtoken)
                {
                    Locals[1] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else if (_pendingDecLocalIndex == 1 && Locals.TryGetValue(1, out var loc1) && loc1.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.DEC, AddressMode.Absolute, (ushort)loc1.Address);
                    Locals[1] = new Local(loc1.Value - 1, loc1.Address);
                    _pendingDecLocalIndex = null;
                }
                else if (_pendingIncLocalIndex == 1 && Locals.TryGetValue(1, out var loc1i) && loc1i.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.INC, AddressMode.Absolute, (ushort)loc1i.Address);
                    Locals[1] = new Local(loc1i.Value + 1, loc1i.Address);
                    _pendingIncLocalIndex = null;
                }
                else
                {
                    var value = Stack.Pop();
                    var address = _nextLocalAddress;
                    _nextLocalAddress += (ushort)(value < byte.MaxValue ? 1 : 2);
                    WriteStloc(Locals[1] = new Local(value, address));
                }
                break;
            case ILOpCode.Stloc_2:
                if (previous == ILOpCode.Ldtoken)
                {
                    Locals[2] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else if (_pendingDecLocalIndex == 2 && Locals.TryGetValue(2, out var loc2) && loc2.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.DEC, AddressMode.Absolute, (ushort)loc2.Address);
                    Locals[2] = new Local(loc2.Value - 1, loc2.Address);
                    _pendingDecLocalIndex = null;
                }
                else if (_pendingIncLocalIndex == 2 && Locals.TryGetValue(2, out var loc2i) && loc2i.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.INC, AddressMode.Absolute, (ushort)loc2i.Address);
                    Locals[2] = new Local(loc2i.Value + 1, loc2i.Address);
                    _pendingIncLocalIndex = null;
                }
                else
                {
                    var value = Stack.Pop();
                    var address = _nextLocalAddress;
                    _nextLocalAddress += (ushort)(value < byte.MaxValue ? 1 : 2);
                    WriteStloc(Locals[2] = new Local(value, address));
                }
                break;
            case ILOpCode.Stloc_3:
                if (previous == ILOpCode.Ldtoken)
                {
                    Locals[3] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else if (_pendingDecLocalIndex == 3 && Locals.TryGetValue(3, out var loc3) && loc3.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.DEC, AddressMode.Absolute, (ushort)loc3.Address);
                    Locals[3] = new Local(loc3.Value - 1, loc3.Address);
                    _pendingDecLocalIndex = null;
                }
                else if (_pendingIncLocalIndex == 3 && Locals.TryGetValue(3, out var loc3i) && loc3i.Address is not null)
                {
                    Stack.Pop();
                    Emit(Opcode.INC, AddressMode.Absolute, (ushort)loc3i.Address);
                    Locals[3] = new Local(loc3i.Value + 1, loc3i.Address);
                    _pendingIncLocalIndex = null;
                }
                else
                {
                    var value = Stack.Pop();
                    var address = _nextLocalAddress;
                    _nextLocalAddress += (ushort)(value < byte.MaxValue ? 1 : 2);
                    WriteStloc(Locals[3] = new Local(value, address));
                }
                break;
            case ILOpCode.Ldloc_0:
                WriteLdloc(Locals[0], 0);
                break;
            case ILOpCode.Ldloc_1:
                WriteLdloc(Locals[1], 1);
                break;
            case ILOpCode.Ldloc_2:
                WriteLdloc(Locals[2], 2);
                break;
            case ILOpCode.Ldloc_3:
                WriteLdloc(Locals[3], 3);
                break;
            case ILOpCode.Conv_u1:
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_u4:
            case ILOpCode.Conv_u8:
                // Do nothing - keep any pending inc/dec intact
                break;
            case ILOpCode.Add:
                {
                    int b = Stack.Pop();
                    int a = Stack.Pop();
                    Stack.Push(a + b);
                    // Check for increment pattern: Ldloc_N, Ldc_i4_1, Add
                    // If previous was Ldc_i4_1 and we had a pending local load, this is x++
                    if (previous == ILOpCode.Ldc_i4_1 && _pendingDecLocalIndex is not null)
                    {
                        // Convert dec to inc (we used Dec field to track the local index)
                        _pendingIncLocalIndex = _pendingDecLocalIndex;
                        _pendingDecLocalIndex = null;
                    }
                }
                break;
            case ILOpCode.Sub:
                {
                    int b = Stack.Pop();
                    int a = Stack.Pop();
                    Stack.Push(a - b);
                    // Check for decrement pattern: Ldloc_N, Ldc_i4_1, Sub
                    // _pendingDecLocalIndex is set by Ldloc when followed by Ldc_i4_1
                }
                break;
            case ILOpCode.Mul:
                Stack.Push(Stack.Pop() * Stack.Pop());
                break;
            case ILOpCode.Div:
                Stack.Push(Stack.Pop() / Stack.Pop());
                break;
            case ILOpCode.And:
                {
                    // AND the value in A with the constant from the stack
                    int mask = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;
                    
                    // If the previous instruction was Ldc_i4*, it emitted LDA #imm which we need to remove.
                    // The value we want to AND is already in A from before the Ldc.
                    if (previous is ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 
                        or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 
                        or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8
                        or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4)
                    {
                        // Remove the LDA #imm instruction that was just emitted
                        RemoveLastInstructions(1);
                    }
                    
                    // If we have a value stored in temp var, reload it before AND
                    // This handles the dup+and pattern for multiple button tests
                    // Skip reload on first use since A still has the value
                    if (_tempVarHasValue && !_tempVarFirstUse)
                    {
                        Emit(Opcode.LDA, AddressMode.Absolute, tempVar);
                    }
                    _tempVarFirstUse = false; // Subsequent uses need reload
                    
                    Emit(Opcode.AND, AddressMode.Immediate, checked((byte)mask));
                    Stack.Push(value & mask);
                }
                break;
            case ILOpCode.Or:
                Stack.Push(Stack.Pop() | Stack.Pop());
                break;
            case ILOpCode.Xor:
                Stack.Push(Stack.Pop() ^ Stack.Pop());
                break;
            default:
                throw new NotImplementedException($"OpCode {instruction.OpCode} with no operands is not implemented!");
        }
        previous = instruction.OpCode;
    }

    public void Write(ILInstruction instruction, int operand)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldc_i4:
            case ILOpCode.Ldc_i4_s:
                if (operand > ushort.MaxValue)
                {
                    throw new NotImplementedException($"{instruction.OpCode} not implemented for value larger than ushort: {operand}");
                }
                else if (operand > byte.MaxValue)
                {
                    WriteLdc(checked((ushort)operand));
                }
                else
                {
                    WriteLdc((byte)operand);
                }
                break;
            case ILOpCode.Br_s:
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitJMP(labelName);
                }
                break;
            case ILOpCode.Br:
                // Long form unconditional branch (32-bit offset)
                {
                    var labelName = $"instruction_{instruction.Offset + operand + 5:X2}";
                    EmitJMP(labelName);
                }
                break;
            case ILOpCode.Newarr:
                if (previous == ILOpCode.Ldc_i4_s)
                {
                    // Remove the previous LDA instruction (1 instruction = 2 bytes)
                    RemoveLastInstructions(1);
                }
                break;
            case ILOpCode.Stloc_s:
                Locals[operand] = new Local(Stack.Pop());
                break;
            case ILOpCode.Ldloc_s:
                WriteLdloc(Locals[operand]);
                break;
            case ILOpCode.Bne_un_s:
                // Remove the previous comparison value loading
                // This is typically JSR pusha (3 bytes) + LDA #imm (2 bytes) = 5 bytes, 2 instructions
                RemoveLastInstructions(2);
                Emit(Opcode.CMP, AddressMode.Immediate, checked((byte)Stack.Pop()));
                Emit(Opcode.BNE, AddressMode.Relative, NumberOfInstructionsForBranch(instruction.Offset + operand + 2));
                break;
            case ILOpCode.Brfalse_s:
                // Branch if value is zero/false
                // The previous AND/comparison result is in A register
                // Use BEQ (branch if equal/zero) to jump if result is 0
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                }
                break;
            case ILOpCode.Brtrue_s:
                // Branch if value is non-zero/true
                // The previous AND/comparison result is in A register
                // Use BNE (branch if not equal/not zero) to jump if result is non-0
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                }
                break;
            default:
                throw new NotImplementedException($"OpCode {instruction.OpCode} with Int32 operand is not implemented!");
        }
        previous = instruction.OpCode;
    }

    public ILInstruction[]? Instructions { get; set; }

    public int Index { get; set; }

    byte NumberOfInstructionsForBranch(int stopAt)
    {
        _logger.WriteLine($"Reading forward until IL_{stopAt:x4}...");

        if (Instructions is null)
            throw new ArgumentNullException(nameof(Instructions));

        // Use block size for measurement
        if (_bufferedBlock == null)
            throw new InvalidOperationException("NumberOfInstructionsForBranch requires block buffering mode");
        
        int startSize = GetBufferedBlockSize();
        int startCount = GetBufferedBlockCount();
        
        for (int i = Index + 1; ; i++)
        {
            var instruction = Instructions[i];
            if (instruction.Integer != null)
            {
                Write(instruction, instruction.Integer.Value);
            }
            else if (instruction.String != null)
            {
                Write(instruction, instruction.String);
            }
            else if (instruction.Bytes != null)
            {
                Write(instruction, instruction.Bytes.Value);
            }
            else
            {
                Write(instruction);
            }
            if (instruction.Offset >= stopAt)
                break;
        }
        
        // Calculate from block size
        byte numberOfBytes = checked((byte)(GetBufferedBlockSize() - startSize));
        // Remove the instructions we just added
        int instructionsAdded = GetBufferedBlockCount() - startCount;
        RemoveLastInstructions(instructionsAdded);
        return numberOfBytes;
    }

    public void Write(ILInstruction instruction, string operand)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldstr:
                //TODO: hardcoded until string table figured out
                Emit(Opcode.LDA, AddressMode.Immediate, 0xF1);
                Emit(Opcode.LDX, AddressMode.Immediate, 0x85);
                EmitJSR("pushax");
                Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                if (operand.Length > ushort.MaxValue)
                {
                    throw new NotImplementedException($"{instruction.OpCode} not implemented for value larger than ushort: {operand}");
                }
                else if (operand.Length > byte.MaxValue)
                {
                    WriteLdc(checked((ushort)operand.Length));
                }
                else
                {
                    WriteLdc((byte)operand.Length);
                }
                break;
            case ILOpCode.Call:
                switch (operand)
                {
                    case nameof(NTADR_A):
                    case nameof(NTADR_B):
                    case nameof(NTADR_C):
                    case nameof(NTADR_D):
                        if (Stack.Count < 2)
                        {
                            throw new InvalidOperationException($"{operand} was called with less than 2 on the stack.");
                        }
                        var address = operand switch
                        {
                            nameof(NTADR_A) => NTADR_A(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_B) => NTADR_B(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_C) => NTADR_C(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            nameof(NTADR_D) => NTADR_D(checked((byte)Stack.Pop()), checked((byte)Stack.Pop())),
                            _ => throw new InvalidOperationException($"Address lookup of {operand} not implemented!"),
                        };
                        // Remove the two constants that were loaded
                        // Typically: LDA #imm (2 bytes) + JSR pusha (3 bytes) + LDA #imm (2 bytes) = 7 bytes, 3 instructions
                        RemoveLastInstructions(3);
                        //TODO: these are hardcoded until I figure this out
                        Emit(Opcode.LDX, AddressMode.Immediate, 0x20);
                        Emit(Opcode.LDA, AddressMode.Immediate, 0x42);
                        Stack.Push(address);
                        break;
                    case "pad_poll":
                        // pad_poll returns value in A - emit JSR then store to temp for multiple tests
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        Emit(Opcode.STA, AddressMode.Absolute, tempVar); // Store result to $0327
                        _tempVarHasValue = true;
                        _tempVarFirstUse = true; // First AND can skip reload
                        _lastLoadedByteValue = null;
                        break;
                    default:
                        // Check if there's a byte array argument on the stack
                        // Byte arrays are marked with negative values (marker = -labelIndex)
                        if (Stack.Count > 0 && Stack.Peek() < 0)
                        {
                            // This is a byte array reference - load its address
                            int marker = -Stack.Peek(); // Convert back to positive index
                            string byteArrayLabel = $"bytearray_{marker - 1}"; // Index is 1-based
                            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, byteArrayLabel);
                            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, byteArrayLabel);
                            _lastLoadedByteValue = null; // Invalidate
                        }
                        // Emit JSR to built-in method
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        break;
                }
                // Pop N times
                int args = _reflectionCache.GetNumberOfArguments(operand);
                for (int i = 0; i < args; i++)
                {
                    if (Stack.Count > 0)
                        Stack.Pop();
                }
                // Return value, dup for now might be fine?
                if (_reflectionCache.HasReturnValue(operand) && Stack.Count > 0)
                    Stack.Push(Stack.Peek());
                break;
            default:
                throw new NotImplementedException($"OpCode {instruction.OpCode} with String operand is not implemented!");
        }
        previous = instruction.OpCode;
    }

    /// <summary>
    /// Counter for generating unique byte array labels
    /// </summary>
    int _byteArrayLabelIndex = 0;
    
    /// <summary>
    /// Track the last byte array label for Stloc handling
    /// </summary>
    string? _lastByteArrayLabel = null;
    int _lastByteArraySize = 0;

    public void Write(ILInstruction instruction, ImmutableArray<byte> operand)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldtoken:
                // Use labels for byte arrays (resolved during address resolution)
                string byteArrayLabel = $"bytearray_{_byteArrayLabelIndex}";
                _byteArrayLabelIndex++;
                
                // HACK: write these if next instruction is Call
                if (Instructions is not null && Instructions[Index + 1].OpCode == ILOpCode.Call)
                {
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, byteArrayLabel);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, byteArrayLabel);
                }
                _byteArrays.Add(operand);
                
                // Push a marker value and track the label for later Stloc
                // Use negative value as marker that this is a label reference
                _lastByteArrayLabel = byteArrayLabel;
                _lastByteArraySize = operand.Length;
                Stack.Push(-(_byteArrayLabelIndex)); // Negative marker
                break;
            default:
                throw new NotImplementedException($"OpCode {instruction.OpCode} with byte[] operand is not implemented!");
        }
        previous = instruction.OpCode;
    }

    void WriteStloc(Local local)
    {
        if (local.Address is null)
            throw new ArgumentNullException(nameof(local.Address));

        if (local.Value < byte.MaxValue)
        {
            LocalCount += 1;
            // Remove the previous LDA instruction (1 instruction = 2 bytes)
            RemoveLastInstructions(1);
            
            // Optimization: if we're storing the same byte value as last time, 
            // skip the LDA since A already has the value
            if (_lastLoadedByteValue != local.Value)
            {
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value);
                _lastLoadedByteValue = local.Value;
            }
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
        }
        else if (local.Value < ushort.MaxValue)
        {
            LocalCount += 2;
            // Remove the previous LDX + LDA instructions (2 instructions = 4 bytes)
            RemoveLastInstructions(2);
            Emit(Opcode.LDX, AddressMode.Immediate, 0x03);
            Emit(Opcode.LDA, AddressMode.Immediate, 0xC0);
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.STX, AddressMode.Absolute, (ushort)(local.Address + 1));
            _lastLoadedByteValue = null; // Invalidate since we modified A
        }
        else
        {
            throw new NotImplementedException($"{nameof(WriteStloc)} not implemented for value larger than ushort: {local.Value}");
        }
    }

    void WriteLdc(ushort operand)
    {
        if (LastLDA)
        {
            EmitJSR("pusha");
        }
        Emit(Opcode.LDX, AddressMode.Immediate, checked((byte)(operand >> 8)));
        Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)(operand & 0xff)));
        Stack.Push(operand);
    }

    void WriteLdc(byte operand)
    {
        // If we just loaded a local var, the next instructions could be inc/dec pattern
        // Don't call pusha yet - wait to see if this is a simple inc/dec
        if (operand == 1 && _pendingDecLocalIndex is not null)
        {
            // This might be an inc/dec pattern - don't emit the LDA yet
            // The pattern will be detected in Add/Sub and handled in Stloc
            Stack.Push(operand);
            return;
        }
        
        if (LastLDA)
        {
            EmitJSR("pusha");
        }
        Emit(Opcode.LDA, AddressMode.Immediate, operand);
        Stack.Push(operand);
    }

    void WriteLdloc(Local local, int localIndex = -1)
    {
        // Track potential inc/dec pattern
        if (localIndex >= 0 && local.Address is not null && local.Value < byte.MaxValue)
        {
            _pendingDecLocalIndex = localIndex;
            // Don't emit LDA+JSR pusha yet - it might be an inc/dec pattern
            Stack.Push(local.Value);
            return;
        }
        
        _pendingDecLocalIndex = null;
        _pendingIncLocalIndex = null;
        
        if (local.LabelName is not null)
        {
            // This local holds a byte array label reference
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, local.LabelName);
            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, local.LabelName);
            EmitJSR("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value); // Size of array
        }
        else if (local.Address is not null)
        {
            // This is actually a local variable
            if (local.Value < byte.MaxValue)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                EmitJSR("pusha");
            }
            else if (local.Value < ushort.MaxValue)
            {
                EmitJSR("pusha");
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(local.Address + 1));
            }
            else
            {
                throw new NotImplementedException($"{nameof(WriteLdloc)} not implemented for value larger than ushort: {local.Value}");
            }
        }
        else
        {
            // This is more like an inline constant value
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(local.Value & 0xff));
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(local.Value >> 8));
            EmitJSR("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            Emit(Opcode.LDA, AddressMode.Immediate, 0x40);
        }
        Stack.Push(local.Value);
    }

    /// <summary>
    /// Flushes the buffered block to the stream and stops block buffering.
    /// Wrapper for FlushBufferedBlock() for backward compatibility.
    /// </summary>
    public void FlushMainBlock() => FlushBufferedBlock();

    /// <summary>
    /// Gets the main program as a Block without flushing it to the stream.
    /// The block can then be added to a Program6502 for analysis or manipulation.
    /// Call this INSTEAD OF FlushMainBlock() when building a full Program6502.
    /// </summary>
    /// <param name="label">Optional label for the main block (e.g., "main")</param>
    /// <returns>The main program block, or null if not in block buffering mode</returns>
    public Block? GetMainBlock(string? label = "main")
    {
        if (_bufferedBlock == null)
            return null;

        var block = _bufferedBlock;
        block.Label = label;
        _bufferedBlock = null;
        return block;
    }

    /// <summary>
    /// Gets the current block being buffered (read-only access).
    /// Returns null if not in block buffering mode.
    /// </summary>
    public Block? CurrentBlock => _bufferedBlock;
}
