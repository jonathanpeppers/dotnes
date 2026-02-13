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
    }

    /// <summary>
    /// The local evaluation stack
    /// </summary>
    internal readonly Stack<int> Stack = new();
    
    /// <summary>
    /// Dictionary of local variables
    /// </summary>
    readonly Dictionary<int, Local> Locals = new();
    /// <summary>
    /// List of byte[] data (accessible for Program6502 building)
    /// </summary>
    public IReadOnlyList<ImmutableArray<byte>> ByteArrays => _byteArrays;
    readonly List<ImmutableArray<byte>> _byteArrays = new();
    /// <summary>
    /// Named ushort[] data extracted from the IL (note tables, etc.)
    /// Key is the label prefix (e.g. "note_table_pulse"), value is raw bytes (2 bytes per ushort, LE).
    /// </summary>
    public IReadOnlyDictionary<string, ImmutableArray<byte>> UShortArrays => _ushortArrays;
    readonly Dictionary<string, ImmutableArray<byte>> _ushortArrays = new();
    readonly ushort local = 0x325;
    readonly ushort tempVar = 0x327; // Temp variable for pad_poll result storage
    readonly ReflectionCache _reflectionCache = new();
    ILOpCode previous;
    string? _pendingArrayType;
    ImmutableArray<byte>? _pendingUShortArray;
    
    /// <summary>
    /// Tracks if pad_poll was called and the result is available in A or tempVar.
    /// When true, AND operations should emit actual 6502 AND instruction.
    /// </summary>
    bool _padPollResultAvailable;

    /// <summary>
    /// Tracks if this is the first AND after pad_poll (A still has value) or
    /// subsequent AND (need to reload from tempVar first).
    /// </summary>
    bool _firstAndAfterPadPoll;

    /// <summary>
    /// Tracks when we're in an increment/decrement pattern.
    /// Set by Add/Sub when operand is 1 and we need runtime arithmetic.
    /// </summary>
    int? _pendingIncDecLocal;

    /// <summary>
    /// Tracks the local index that was just loaded by Ldloc_N.
    /// Used to detect inc/dec patterns.
    /// </summary>
    int? _lastLoadedLocalIndex;

    /// <summary>
    /// Tracks the immediate value currently in the A register (for redundant LDA elimination).
    /// Cleared when A is modified by JSR, LDA absolute, AND, etc.
    /// </summary>
    byte? _immediateInA;

    /// <summary>
    /// Tracks the last value stored by poke() — used to skip redundant LDA across consecutive pokes.
    /// Separate from _immediateInA because poke's RemoveLastInstructions makes _immediateInA unreliable.
    /// </summary>
    byte? _pokeLastValue;

    /// <summary>
    /// True when the byte array from Ldtoken needs its address explicitly loaded at
    /// the Call point. This only happens in the new pattern (movingsprite-like) where
    /// Stloc_0 does NOT take the Ldtoken path.
    /// </summary>
    bool _needsByteArrayLoadInCall;

    /// <summary>
    /// True when Stloc_0 took the Ldtoken path (stored a byte array reference).
    /// In this case, the old code generation pattern is used where WriteStloc trailing
    /// bytes serve as the byte array address.
    /// </summary>
    bool _stloc0IsLdtokenPath;

    /// <summary>
    /// True when using new code generation pattern. Set when a byte-path WriteStloc
    /// is called while the byte array is deferred AND Stloc_0 did NOT take the Ldtoken path.
    /// </summary>
    bool DeferredByteArrayMode => _lastByteArrayLabel != null && !_byteArrayAddressEmitted && !_stloc0IsLdtokenPath;

    /// <summary>
    /// Set to true when the Ldtoken handler already emitted LDA/LDX for the byte array address.
    /// </summary>
    bool _byteArrayAddressEmitted;

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
    /// Tracks the buffered block instruction count at the START of processing each IL instruction.
    /// Key = IL instruction offset, Value = block instruction count before processing.
    /// Used by EmitOamSprDecsp4 to remove previously emitted argument instructions.
    /// </summary>
    readonly Dictionary<int, int> _blockCountAtILOffset = new();

    /// <summary>
    /// Emits a JSR to a label reference.
    /// </summary>
    void EmitJSR(string labelName) => EmitWithLabel(Opcode.JSR, AddressMode.Absolute, labelName);

    /// <summary>
    /// Records the current block instruction count for an IL instruction offset.
    /// Called before processing each IL instruction.
    /// </summary>
    public void RecordBlockCount(int ilOffset)
    {
        if (_bufferedBlock != null)
            _blockCountAtILOffset[ilOffset] = GetBufferedBlockCount();
    }

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
                if (_pendingIncDecLocal == 0)
                {
                    // INC/DEC was already emitted, just update tracking
                    Locals[0] = new Local(Stack.Pop(), Locals[0].Address);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken)
                {
                    // Capture label for byte array reference
                    Locals[0] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    _stloc0IsLdtokenPath = true;
                    Stack.Pop(); // Discard marker
                }
                else
                {
                    var addr = Locals.TryGetValue(0, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[0] = new Local(Stack.Pop(), addr));
                }
                break;
            case ILOpCode.Stloc_1:
                if (_pendingIncDecLocal == 1)
                {
                    Locals[1] = new Local(Stack.Pop(), Locals[1].Address);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken)
                {
                    Locals[1] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else
                {
                    var addr = Locals.TryGetValue(1, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[1] = new Local(Stack.Pop(), addr));
                }
                break;
            case ILOpCode.Stloc_2:
                if (_pendingIncDecLocal == 2)
                {
                    Locals[2] = new Local(Stack.Pop(), Locals[2].Address);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken)
                {
                    Locals[2] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else
                {
                    var addr = Locals.TryGetValue(2, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[2] = new Local(Stack.Pop(), addr));
                }
                break;
            case ILOpCode.Stloc_3:
                if (_pendingIncDecLocal == 3)
                {
                    Locals[3] = new Local(Stack.Pop(), Locals[3].Address);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken)
                {
                    Locals[3] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    Stack.Pop();
                }
                else
                {
                    var addr = Locals.TryGetValue(3, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[3] = new Local(Stack.Pop(), addr));
                }
                break;
            case ILOpCode.Ldloc_0:
                WriteLdloc(Locals[0]);
                _lastLoadedLocalIndex = 0;
                break;
            case ILOpCode.Ldloc_1:
                WriteLdloc(Locals[1]);
                _lastLoadedLocalIndex = 1;
                break;
            case ILOpCode.Ldloc_2:
                WriteLdloc(Locals[2]);
                _lastLoadedLocalIndex = 2;
                break;
            case ILOpCode.Ldloc_3:
                WriteLdloc(Locals[3]);
                _lastLoadedLocalIndex = 3;
                break;
            case ILOpCode.Conv_u1:
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_u4:
            case ILOpCode.Conv_u8:
                // Do nothing
                break;
            case ILOpCode.Stelem_i1:
            case ILOpCode.Stelem_i2:
            case ILOpCode.Stelem_i4:
            case ILOpCode.Stelem_i8:
                // stelem.i*(stack: arrayref, index, value → stack: )
                // TODO: Implement proper address calculation and store operation.
                // For now, we consume the IL stack operands to maintain consistency
                // and prevent subsequent operations from seeing incorrect stack state.
                if (Stack.Count >= 3)
                {
                    Stack.Pop(); // value
                    Stack.Pop(); // index
                    Stack.Pop(); // arrayref
                }
                else
                {
                    // In case of an inconsistent tracked stack, reset to a safe state.
                    Stack.Clear();
                }
                break;
            case ILOpCode.Add:
                HandleAddSub(isAdd: true);
                break;
            case ILOpCode.Sub:
                HandleAddSub(isAdd: false);
                break;
            case ILOpCode.Mul:
                Stack.Push(Stack.Pop() * Stack.Pop());
                break;
            case ILOpCode.Div:
                Stack.Push(Stack.Pop() / Stack.Pop());
                break;
            case ILOpCode.And:
                {
                    int mask = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    // If pad_poll result is available, emit actual AND instruction
                    if (_padPollResultAvailable)
                    {
                        // Remove the LDA #mask that was emitted by Ldc_i4*
                        if (previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        // If not first AND, need to reload pad value from temp
                        if (!_firstAndAfterPadPoll)
                        {
                            Emit(Opcode.LDA, AddressMode.Absolute, tempVar);
                        }
                        _firstAndAfterPadPoll = false;

                        Emit(Opcode.AND, AddressMode.Immediate, checked((byte)mask));
                    }

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
                if (previous == ILOpCode.Ldc_i4_s || previous == ILOpCode.Ldc_i4)
                {
                    // Remove the previous LDA (and LDX for ushort-sized values)
                    int toRemove = Stack.Count > 0 && Stack.Peek() > byte.MaxValue ? 2 : 1;
                    RemoveLastInstructions(toRemove);
                }
                // Track the array element type so the next Ldtoken can handle non-byte arrays
                _pendingArrayType = instruction.String;
                if (_pendingArrayType != null && _pendingArrayType != "Byte")
                {
                    // Non-byte array: pop the array size that Ldc pushed
                    if (Stack.Count > 0)
                        Stack.Pop();
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
                // Branch if value is zero/false (after AND test)
                // The AND result is in A and sets the zero flag, so use BEQ
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
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
                        // pad_poll returns result in A - emit JSR then store to tempVar for multiple tests
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        Emit(Opcode.STA, AddressMode.Absolute, tempVar);
                        _padPollResultAvailable = true;
                        _firstAndAfterPadPoll = true;
                        _immediateInA = null;
                        if (DeferredByteArrayMode)
                            LocalCount += 1; // tempVar counts as a local for zerobss
                        break;
                    case "oam_spr":
                        if (DeferredByteArrayMode)
                        {
                            EmitOamSprDecsp4();
                        }
                        else
                        {
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            _immediateInA = null;
                        }
                        break;
                    case nameof(NESLib.set_music_pulse_table):
                    case nameof(NESLib.set_music_triangle_table):
                        // Store the pending ushort[] with the appropriate label
                        if (_pendingUShortArray != null)
                        {
                            string label = operand == nameof(NESLib.set_music_pulse_table)
                                ? "note_table_pulse"
                                : "note_table_triangle";
                            _ushortArrays[label] = _pendingUShortArray.Value;
                            _pendingUShortArray = null;
                        }
                        // These are transpiler-only directives; no 6502 code emitted
                        break;
                    case nameof(NESLib.start_music):
                        // start_music expects address in A/X (it pushes internally).
                        // Emit the byte array address from the deferred label.
                        if (_lastByteArrayLabel != null)
                        {
                            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, _lastByteArrayLabel);
                            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, _lastByteArrayLabel);
                        }
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        _immediateInA = null;
                        break;
                    case nameof(NESLib.poke):
                        {
                            // poke(ushort addr, byte value) -> LDA #value, STA abs addr
                            if (Stack.Count >= 2)
                            {
                                int value = Stack.Pop();
                                int addr = Stack.Pop();
                                // Remove previously emitted instructions:
                                // LDX #hi, LDA #lo, JSR pusha, LDA #value = 4 instructions
                                RemoveLastInstructions(4);
                                // After removal, _immediateInA may be stale; only trust
                                // the value if the PREVIOUS poke set it (STA doesn't change A)
                                if (_pokeLastValue != (byte)value)
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)value);
                                }
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr);
                                _pokeLastValue = (byte)value;
                                _immediateInA = (byte)value;
                            }
                        }
                        break;
                    default:
                        if (_needsByteArrayLoadInCall && _lastByteArrayLabel != null 
                            && previous != ILOpCode.Ldtoken)
                        {
                            // Load deferred byte array address into AX before calling the function
                            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, _lastByteArrayLabel);
                            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, _lastByteArrayLabel);
                            _needsByteArrayLoadInCall = false; // Only emit once
                        }
                        // Emit JSR to built-in method
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        _immediateInA = null;
                        break;
                }
                // Pop N times
                int args = _reflectionCache.GetNumberOfArguments(operand);
                for (int i = 0; i < args; i++)
                {
                    if (Stack.Count > 0)
                        Stack.Pop();
                }
                // Clear byte array label if it was consumed by Ldtoken→Call path
                if (_lastByteArrayLabel != null && previous == ILOpCode.Ldtoken)
                    _lastByteArrayLabel = null;
                // Return value handling
                if (_reflectionCache.HasReturnValue(operand))
                {
                    // For pad_poll, push the return value placeholder after args are popped
                    if (operand == "pad_poll")
                    {
                        Stack.Push(0);
                    }
                    else if (Stack.Count > 0)
                    {
                        // Other methods: dup for now might be fine?
                        Stack.Push(Stack.Peek());
                    }
                }
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
                // Non-byte arrays (e.g. ushort[]) are collected separately and not emitted as code
                if (_pendingArrayType != null && _pendingArrayType != "Byte")
                {
                    _pendingUShortArray = operand;
                    _pendingArrayType = null;
                    break;
                }
                _pendingArrayType = null;

                // Use labels for byte arrays (resolved during address resolution)
                string byteArrayLabel = $"bytearray_{_byteArrayLabelIndex}";
                _byteArrayLabelIndex++;
                
                // HACK: write these if next instruction is Call
                _byteArrayAddressEmitted = false;
                if (Instructions is not null && Instructions[Index + 1].OpCode == ILOpCode.Call)
                {
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, byteArrayLabel);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, byteArrayLabel);
                    _byteArrayAddressEmitted = true;
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

    /// <summary>
    /// Handles Add and Sub operations. For patterns like x++ or x--, emits optimized INC/DEC.
    /// For other arithmetic, performs compile-time calculation.
    /// </summary>
    void HandleAddSub(bool isAdd)
    {
        int operand = Stack.Pop(); // The value being added/subtracted (e.g., 1)
        int baseValue = Stack.Pop(); // The base value (from local variable)

        // Check if this is an x++ or x-- pattern:
        // Ldloc_N, Ldc_i4_1, Add/Sub, Conv_u1, Stloc_N (where the two N's match)
        if (_lastLoadedLocalIndex.HasValue && operand == 1 && 
            Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var loadedLocal) && loadedLocal.Address.HasValue &&
            Instructions is not null && Index + 2 < Instructions.Length)
        {
            var next1 = Instructions[Index + 1];
            var next2 = Instructions[Index + 2];
            
            int? storeLocalIndex = GetStlocIndex(next2.OpCode);
            
            if ((next1.OpCode == ILOpCode.Conv_u1 || next1.OpCode == ILOpCode.Conv_u2 ||
                 next1.OpCode == ILOpCode.Conv_u4 || next1.OpCode == ILOpCode.Conv_u8) &&
                storeLocalIndex == _lastLoadedLocalIndex)
            {
                // This is an x++ or x-- pattern!
                // Remove the previously emitted instructions:
                // WriteLdloc emits: LDA $addr; JSR pusha (2 instructions)
                // WriteLdc(byte) emits: LDA #imm (1 instruction)
                // Total: 3 instructions to remove
                RemoveLastInstructions(3);
                
                // Emit optimized INC or DEC
                ushort addr = (ushort)loadedLocal.Address.Value;
                if (isAdd)
                {
                    Emit(Opcode.INC, AddressMode.Absolute, addr);
                }
                else
                {
                    Emit(Opcode.DEC, AddressMode.Absolute, addr);
                }
                
                // Signal that Stloc should be skipped
                _pendingIncDecLocal = _lastLoadedLocalIndex;
                
                // Push the updated value (for tracking)
                int result = isAdd ? baseValue + 1 : baseValue - 1;
                Stack.Push(result);
                _lastLoadedLocalIndex = null;
                return;
            }
        }
        
        // Default: compile-time calculation only
        int compileTimeResult = isAdd ? baseValue + operand : baseValue - operand;
        Stack.Push(compileTimeResult);
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Gets the local index for a Stloc opcode, or null if not a Stloc.
    /// </summary>
    static int? GetStlocIndex(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Stloc_0 => 0,
        ILOpCode.Stloc_1 => 1,
        ILOpCode.Stloc_2 => 2,
        ILOpCode.Stloc_3 => 3,
        ILOpCode.Stloc => null, // Would need operand
        ILOpCode.Stloc_s => null, // Would need operand  
        _ => null
    };

    void WriteStloc(Local local)
    {
        if (local.Address is null)
            throw new ArgumentNullException(nameof(local.Address));

        if (local.Value < byte.MaxValue)
        {
            LocalCount += 1;
            if (DeferredByteArrayMode)
            {
                // New pattern: just emit STA (keep the LDA from WriteLdc)
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                // STA doesn't change A, so _immediateInA stays valid
                _needsByteArrayLoadInCall = true;
            }
            else
            {
                // Old pattern: full sequence with trailing LDA/LDX
                RemoveLastInstructions(1);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.LDA, AddressMode.Immediate, 0x22);
                Emit(Opcode.LDX, AddressMode.Immediate, 0x86);
                _immediateInA = null;
            }
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
            Emit(Opcode.LDA, AddressMode.Immediate, 0x28);
            Emit(Opcode.LDX, AddressMode.Immediate, 0x86);
            _immediateInA = null;
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
        if (LastLDA)
        {
            EmitJSR("pusha");
            _immediateInA = null;
        }
        if (DeferredByteArrayMode && _immediateInA.HasValue && _immediateInA.Value == operand)
        {
            // A already has this value, skip the LDA
            Stack.Push(operand);
            return;
        }
        Emit(Opcode.LDA, AddressMode.Immediate, operand);
        _immediateInA = operand;
        Stack.Push(operand);
    }

    void WriteLdloc(Local local)
    {
        if (local.LabelName is not null)
        {
            // This local holds a byte array label reference
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, local.LabelName);
            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, local.LabelName);
            EmitJSR("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value); // Size of array
            _immediateInA = (byte)local.Value;
        }
        else if (local.Address is not null)
        {
            // This is actually a local variable
            if (local.Value < byte.MaxValue)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                EmitJSR("pusha");
                _immediateInA = null;
            }
            else if (local.Value < ushort.MaxValue)
            {
                EmitJSR("pusha");
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(local.Address + 1));
                _immediateInA = null;
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
            _immediateInA = 0x40;
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

    /// <summary>
    /// Emits oam_spr call using decsp4 + inline STA ($22),Y pattern.
    /// Used when arguments include runtime variables (deferred byte array mode).
    /// </summary>
    void EmitOamSprDecsp4()
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamSprDecsp4 requires Instructions");

        // oam_spr has 5 args: x, y, tile, attr, id
        // Walk back through IL to find the 5 argument-producing IL instructions
        // and determine the first argument's IL offset for instruction removal
        var argInfos = new List<(bool isLocal, int localIndex, int constValue, bool hasAdd, int addValue)>();
        int ilIdx = Index - 1;
        int needed = 5;
        int firstArgIlIdx = -1;
        int? pendingAddValue = null; // Tracks Ldc value that's consumed by Add (part of Ldloc+N expression)

        while (needed > 0 && ilIdx >= 0)
        {
            var il = Instructions[ilIdx];
            switch (il.OpCode)
            {
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                    int locIdx = il.OpCode - ILOpCode.Ldloc_0;
                    if (pendingAddValue != null)
                    {
                        argInfos.Add((true, locIdx, 0, true, pendingAddValue.Value));
                        pendingAddValue = null;
                    }
                    else
                    {
                        argInfos.Add((true, locIdx, 0, false, 0));
                    }
                    needed--;
                    firstArgIlIdx = ilIdx;
                    break;
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3: case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7: case ILOpCode.Ldc_i4_8:
                {
                    int val = il.OpCode - ILOpCode.Ldc_i4_0;
                    if (IsConsumedByAdd(ilIdx))
                    {
                        pendingAddValue = val;
                    }
                    else
                    {
                        argInfos.Add((false, 0, val, false, 0));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                {
                    int val = il.Integer ?? 0;
                    if (IsConsumedByAdd(ilIdx))
                    {
                        pendingAddValue = val;
                    }
                    else
                    {
                        argInfos.Add((false, 0, val, false, 0));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Add: case ILOpCode.Sub:
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2:
                case ILOpCode.Pop:
                    break;
                default:
                    break;
            }
            ilIdx--;
        }

        argInfos.Reverse();

        // Remove all previously emitted instructions for these args
        if (firstArgIlIdx >= 0)
        {
            int firstArgIlOffset = Instructions[firstArgIlIdx].Offset;
            if (_blockCountAtILOffset.TryGetValue(firstArgIlOffset, out int blockCountAtFirstArg))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountAtFirstArg;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }
        }

        // Mark decsp4 as used
        UsedMethods?.Add("decsp4");
        UsedMethods?.Add("pad_trigger");
        UsedMethods?.Add("pad_state");

        // Emit: JSR decsp4
        EmitJSR("decsp4");

        // First 4 args go to stack via STA ($22),Y
        for (int i = 0; i < 4 && i < argInfos.Count; i++)
        {
            var arg = argInfos[i];
            if (arg.isLocal)
            {
                var loc = Locals[arg.localIndex];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc.Address!);
                if (arg.hasAdd)
                {
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)arg.addValue));
                }
            }
            else
            {
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)arg.constValue));
            }

            if (i == 0)
                Emit(Opcode.LDY, AddressMode.Immediate, 0x03);
            else
                Emit(Opcode.DEY, AddressMode.Implied);

            Emit(Opcode.STA, AddressMode.IndirectIndexed, (byte)0x22);
        }

        // 5th arg (id) stays in A - check if we can skip LDA (STA doesn't change A)
        if (argInfos.Count >= 5)
        {
            var idArg = argInfos[4];
            byte idVal = checked((byte)idArg.constValue);
            if (argInfos.Count >= 4)
            {
                var attrArg = argInfos[3];
                if (!attrArg.isLocal && !idArg.isLocal && attrArg.constValue == idArg.constValue)
                {
                    // A already has the right value from attr
                }
                else
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, idVal);
                }
            }
            else
            {
                Emit(Opcode.LDA, AddressMode.Immediate, idVal);
            }
        }

        // JSR oam_spr
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, "oam_spr");
        _immediateInA = null;
    }

    /// <summary>
    /// Checks if a Ldc instruction at the given index is consumed by a following Add/Sub
    /// (making it part of a compound expression like Ldloc + Ldc + Add).
    /// </summary>
    bool IsConsumedByAdd(int idx)
    {
        if (Instructions is null) return false;
        for (int scan = idx + 1; scan < Index; scan++)
        {
            var op = Instructions[scan].OpCode;
            if (op == ILOpCode.Add || op == ILOpCode.Sub)
                return true;
            if (op is ILOpCode.Conv_u1 or ILOpCode.Conv_u2)
                continue;
            break;
        }
        return false;
    }
}
