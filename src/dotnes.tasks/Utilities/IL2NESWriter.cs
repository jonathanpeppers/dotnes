using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
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
    /// <summary>
    /// Ordered list of (label, ASCII bytes) for string literals encountered in IL.
    /// </summary>
    public IReadOnlyList<(string Label, byte[] Data)> StringTable => _stringTable;
    readonly List<(string Label, byte[] Data)> _stringTable = new();
    readonly Dictionary<string, string> _stringLabelMap = new();
    int _stringLabelIndex;

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
    /// True when A holds a runtime-computed value (from Ldelem, function return, etc.)
    /// that cannot be known at compile time. Used by HandleAddSub to emit runtime CLC+ADC/SEC+SBC.
    /// </summary>
    bool _runtimeValueInA;

    /// <summary>
    /// True when a runtime value was saved to TEMP ($17) because a subsequent Ldloc needed to clobber A.
    /// Used by HandleAddSub to know the first operand is in TEMP and second in A.
    /// </summary>
    bool _savedRuntimeToTemp;

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

    record Local(int Value, int? Address = null, string? LabelName = null, int ArraySize = 0);

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
                else if (previous == ILOpCode.Newarr)
                {
                    int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
                    ushort arrayAddr = (ushort)(local + LocalCount);
                    LocalCount += arraySize;
                    Locals[1] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
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
                else if (previous == ILOpCode.Newarr)
                {
                    int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
                    ushort arrayAddr = (ushort)(local + LocalCount);
                    LocalCount += arraySize;
                    Locals[2] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
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
                else if (previous == ILOpCode.Newarr)
                {
                    int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
                    ushort arrayAddr = (ushort)(local + LocalCount);
                    LocalCount += arraySize;
                    Locals[3] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
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
                HandleStelemI1();
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

                    // Emit runtime AND if A has a runtime value
                    if (_padPollResultAvailable || _runtimeValueInA)
                    {
                        // Remove the LDA #mask that was emitted by Ldc_i4*
                        if (previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        // If not first AND after pad_poll, need to reload pad value from temp
                        if (_padPollResultAvailable && !_firstAndAfterPadPoll)
                        {
                            Emit(Opcode.LDA, AddressMode.Absolute, tempVar);
                        }
                        _firstAndAfterPadPoll = false;

                        Emit(Opcode.AND, AddressMode.Immediate, checked((byte)mask));
                        _runtimeValueInA = true; // AND result is still runtime
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(value & mask);
                    }
                }
                break;
            case ILOpCode.Or:
                Stack.Push(Stack.Pop() | Stack.Pop());
                break;
            case ILOpCode.Xor:
                Stack.Push(Stack.Pop() ^ Stack.Pop());
                break;
            case ILOpCode.Ldelem_u1:
                // ldelem.u1: pop array ref and index, push array[index]
                // Pattern: Ldloc_N (array), Ldloc_M (index), Ldelem_u1
                HandleLdelemU1();
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
                {
                    int localIdx = operand;
                    if (_pendingIncDecLocal == localIdx)
                    {
                        // INC/DEC was already emitted, just update tracking
                        Locals[localIdx] = new Local(Stack.Pop(), Locals[localIdx].Address);
                        _pendingIncDecLocal = null;
                    }
                    else if (previous == ILOpCode.Newarr)
                    {
                        // Runtime array allocation: new byte[N]
                        int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
                        ushort arrayAddr = (ushort)(local + LocalCount);
                        LocalCount += arraySize;
                        Locals[localIdx] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
                    }
                    else if (previous == ILOpCode.Ldtoken)
                    {
                        // Initialized byte array from Ldtoken
                        Locals[localIdx] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                        _lastByteArrayLabel = null;
                        Stack.Pop();
                    }
                    else
                    {
                        // Regular byte local — allocate address and store
                        var addr = Locals.TryGetValue(localIdx, out var existing) && existing.Address is not null
                            ? (ushort)existing.Address : (ushort)(local + LocalCount);
                        WriteStloc(Locals[localIdx] = new Local(Stack.Pop(), addr));
                    }
                }
                break;
            case ILOpCode.Ldloc_s:
                WriteLdloc(Locals[operand]);
                _lastLoadedLocalIndex = operand;
                break;
            case ILOpCode.Bne_un_s:
                // Remove the previous comparison value loading
                // This is typically JSR pusha (3 bytes) + LDA #imm (2 bytes) = 5 bytes, 2 instructions
                RemoveLastInstructions(2);
                Emit(Opcode.CMP, AddressMode.Immediate, checked((byte)Stack.Pop()));
                Emit(Opcode.BNE, AddressMode.Relative, NumberOfInstructionsForBranch(instruction.Offset + operand + 2));
                _runtimeValueInA = false;
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
                    _runtimeValueInA = false; // Branch taken/not taken — A state is indeterminate
                }
                break;
            case ILOpCode.Blt_s:
                // Branch if less than (signed): value1 < value2
                // IL stack: ..., value1, value2 → ...
                // Pattern: Ldloc → LDA $addr, then Ldc → LDA #imm
                // Remove the LDA #imm, emit CMP #imm + BCC (unsigned less than)
                {
                    operand = (sbyte)(byte)operand;
                    byte cmpValue = checked((byte)Stack.Pop()); // value2 (comparison target)
                    if (Stack.Count > 0) Stack.Pop(); // value1
                    RemoveLastInstructions(1); // Remove LDA #imm from Ldc
                    Emit(Opcode.CMP, AddressMode.Immediate, cmpValue);
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
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
                // Deduplicate: reuse label if same string was already seen
                if (!_stringLabelMap.TryGetValue(operand, out string? stringLabel))
                {
                    stringLabel = $"string_{_stringLabelIndex}";
                    _stringLabelIndex++;
                    byte[] asciiBytes = Encoding.ASCII.GetBytes(operand);
                    byte[] withNull = new byte[asciiBytes.Length + 1];
                    asciiBytes.CopyTo(withNull, 0);
                    _stringTable.Add((stringLabel, withNull));
                    _stringLabelMap[operand] = stringLabel;
                }

                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, stringLabel);
                EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, stringLabel);
                EmitJSR("pushax");
                Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                if (operand.Length > byte.MaxValue)
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
                        byte y = checked((byte)Stack.Pop());
                        byte x = checked((byte)Stack.Pop());
                        var address = operand switch
                        {
                            nameof(NTADR_A) => NTADR_A(x, y),
                            nameof(NTADR_B) => NTADR_B(x, y),
                            nameof(NTADR_C) => NTADR_C(x, y),
                            nameof(NTADR_D) => NTADR_D(x, y),
                            _ => throw new InvalidOperationException($"Address lookup of {operand} not implemented!"),
                        };
                        // Remove the two constants that were loaded
                        // Typically: LDA #imm (2 bytes) + JSR pusha (3 bytes) + LDA #imm (2 bytes) = 7 bytes, 3 instructions
                        RemoveLastInstructions(3);
                        Emit(Opcode.LDX, AddressMode.Immediate, checked((byte)(address >> 8)));
                        Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)(address & 0xFF)));
                        Stack.Push(address);
                        _runtimeValueInA = false; // Value is compile-time constant
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
                    case nameof(NESLib.oam_meta_spr):
                        EmitOamMetaSpr();
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
                    case "scroll":
                        // scroll() takes unsigned int params, which use popax (2-byte pop).
                        // The preceding instructions are: JSR pusha, LDA $addr.
                        // Replace pusha (1-byte push) with LDX #$00 + pushax (2-byte push).
                        {
                            var block = CurrentBlock!;
                            var ldaInstr = block[block.Count - 1]; // LDA $addr (scroll_y)
                            RemoveLastInstructions(2); // Remove JSR pusha + LDA $addr
                            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                            EmitJSR("pushax");
                            block.Emit(ldaInstr); // Re-emit LDA $addr
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            _immediateInA = null;
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
                    // Only set _runtimeValueInA for methods that produce true runtime values
                    // Skip: NTADR_* (compile-time computed), pad_poll (has its own mechanism)
                    if (operand is not (nameof(NTADR_A) or nameof(NTADR_B) or nameof(NTADR_C) or nameof(NTADR_D))
                        && operand != "pad_poll")
                    {
                        _runtimeValueInA = true;
                    }
                    // Push return value placeholder (except for NTADR which already pushed)
                    if (operand is not (nameof(NTADR_A) or nameof(NTADR_B) or nameof(NTADR_C) or nameof(NTADR_D)))
                    {
                        Stack.Push(0);
                    }
                }
                else
                {
                    // Void method: JSR clobbers A, clear runtime tracking
                    _runtimeValueInA = false;
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
            // For Stloc_s, the local index is in the instruction's operand
            if (storeLocalIndex is null && next2.OpCode == ILOpCode.Stloc_s)
                storeLocalIndex = next2.Integer;
            
            if ((next1.OpCode == ILOpCode.Conv_u1 || next1.OpCode == ILOpCode.Conv_u2 ||
                 next1.OpCode == ILOpCode.Conv_u4 || next1.OpCode == ILOpCode.Conv_u8) &&
                storeLocalIndex == _lastLoadedLocalIndex)
            {
                // This is an x++ or x-- pattern!
                // Remove the previously emitted instructions:
                // WriteLdloc emits: LDA $addr (1 instruction)
                // WriteLdc(byte) emits: LDA #imm (1 instruction)
                // Total: 2 instructions to remove
                RemoveLastInstructions(2);
                
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
        
        // Default: if we have runtime values, emit actual arithmetic
        if (_runtimeValueInA)
        {
            if (isAdd)
            {
                if (_savedRuntimeToTemp)
                {
                    // Two runtime values: first in TEMP, second in A
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                }
                else
                {
                    // Runtime value in A + compile-time constant
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)operand));
                }
            }
            else
            {
                if (_savedRuntimeToTemp)
                {
                    // Two runtime values: first in TEMP, second in A — need TEMP - A
                    // Swap: save A, load TEMP, subtract saved value
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.TEMP + 1));
                    Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                    Emit(Opcode.SEC, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.ZeroPage, (byte)(NESConstants.TEMP + 1));
                }
                else
                {
                    // Runtime value in A - compile-time constant
                    Emit(Opcode.SEC, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.Immediate, checked((byte)operand));
                }
            }
            Stack.Push(0); // Placeholder
            _lastLoadedLocalIndex = null;
            _savedRuntimeToTemp = false;
            return;
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

    /// <summary>
    /// Gets the local index from a Ldloc instruction.
    /// </summary>
    static int? GetLdlocIndex(ILInstruction instr) => instr.OpCode switch
    {
        ILOpCode.Ldloc_0 => 0,
        ILOpCode.Ldloc_1 => 1,
        ILOpCode.Ldloc_2 => 2,
        ILOpCode.Ldloc_3 => 3,
        ILOpCode.Ldloc_s => instr.Integer,
        _ => null
    };

    void WriteStloc(Local local)
    {
        if (local.Address is null)
            throw new ArgumentNullException(nameof(local.Address));

        if (_runtimeValueInA)
        {
            // A has the runtime value — just store it
            LocalCount += 1;
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            _immediateInA = null;
        }
        else if (local.Value < byte.MaxValue)
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
        if (_runtimeValueInA)
        {
            // Don't emit LDA — the runtime value in A must be preserved.
            // The constant is tracked on the Stack for the next operation (AND, ADD, SUB, etc.)
            Stack.Push(operand);
            return;
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
                if (LastLDA)
                {
                    EmitJSR("pusha");
                }
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
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
    /// Emits oam_meta_spr call with proper argument setup.
    /// IL pattern: ldloc(arr_x), ldloc_s(i), ldelem_u1, ldloc(arr_y), ldloc_s(i), ldelem_u1,
    ///             ldloc_s(sprid), ldloc(metasprite), call oam_meta_spr
    /// 
    /// Sets up: TEMP = x, TEMP2 = y, PTR = data pointer, A = sprid
    /// </summary>
    void EmitOamMetaSpr()
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamMetaSpr requires Instructions");

        // Scan backward to find all 4 argument sources
        // Args: x (byte), y (byte), sprid (byte), data (byte[])
        // In IL, they appear in order: x_source, y_source, sprid_source, data_source

        // Find argument-producing instructions by walking backward
        int? xArrayIdx = null, yArrayIdx = null, indexIdx = null, spridIdx = null;
        string? dataLabel = null;
        int firstArgILOffset = -1;

        // The last arg (data/metasprite) is ldloc_0 which has a LabelName
        // The sprid is ldloc_s (a byte local)
        // x and y come from ldelem_u1 (array[index])
        
        int scan = Index - 1;
        
        // Find data source (should be a local with LabelName)
        if (scan >= 0)
        {
            var dataInstr = Instructions[scan];
            var dataLocIdx = GetLdlocIndex(dataInstr);
            if (dataLocIdx != null && Locals.TryGetValue(dataLocIdx.Value, out var dataLocal) && dataLocal.LabelName != null)
            {
                dataLabel = dataLocal.LabelName;
                scan--;
            }
        }

        // Find sprid source
        if (scan >= 0)
        {
            var spridInstr = Instructions[scan];
            var spridLocIdx = GetLdlocIndex(spridInstr);
            if (spridLocIdx != null && Locals.TryGetValue(spridLocIdx.Value, out var spridLocal) && spridLocal.Address != null)
            {
                spridIdx = spridLocIdx.Value;
                scan--;
            }
        }

        // Find y source (ldelem_u1 preceded by ldloc(arr) + ldloc(idx))
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            if (scan >= 0)
            {
                var yIdxInstr = Instructions[scan];
                indexIdx = GetLdlocIndex(yIdxInstr);
                scan--;
            }
            if (scan >= 0)
            {
                var yArrInstr = Instructions[scan];
                yArrayIdx = GetLdlocIndex(yArrInstr);
                scan--;
            }
        }

        // Find x source (ldelem_u1 preceded by ldloc(arr) + ldloc(idx))
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--; // skip ldelem
            scan--; // skip index ldloc (same index)
            if (scan >= 0)
            {
                var xArrInstr = Instructions[scan];
                xArrayIdx = GetLdlocIndex(xArrInstr);
                firstArgILOffset = xArrInstr.Offset;
            }
        }

        // Remove all previously emitted instructions for these arguments
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit the proper code
        // 1. Load x coordinate into TEMP
        if (xArrayIdx != null && indexIdx != null)
        {
            var xArr = Locals[xArrayIdx.Value];
            var idx = Locals[indexIdx.Value];
            if (idx.Address != null)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idx.Address);
            if (xArr.ArraySize > 0 && xArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)xArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }

        // 2. Load y coordinate into TEMP2
        if (yArrayIdx != null && indexIdx != null)
        {
            var yArr = Locals[yArrayIdx.Value];
            // X already has the index from step 1
            if (yArr.ArraySize > 0 && yArr.Address != null)
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)yArr.Address);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }

        // 3. Load data pointer into PTR
        if (dataLabel != null)
        {
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }

        // 4. Load sprid into A
        if (spridIdx != null)
        {
            var spridLocal = Locals[spridIdx.Value];
            if (spridLocal.Address != null)
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)spridLocal.Address);
        }

        // 5. Call oam_meta_spr
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.oam_meta_spr));
        _immediateInA = null;
        _runtimeValueInA = true; // Return value in A
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

    /// <summary>
    /// Handles ldelem.u1: loads a byte from an array at a runtime index.
    /// Pattern: Ldloc_N (array), Ldloc_M (index), Ldelem_u1
    /// Emits: LDX index_addr; LDA array_base,X (for RAM arrays)
    ///    or: LDX index_addr; LDA label,X (for ROM arrays)
    /// </summary>
    void HandleLdelemU1()
    {
        if (Instructions is null || Index < 2)
            throw new InvalidOperationException("HandleLdelemU1 requires at least 2 previous instructions");

        int indexIdx = Stack.Count > 0 ? Stack.Pop() : 0; // index value
        int arrayRef = Stack.Count > 0 ? Stack.Pop() : 0; // array ref

        // Find the two Ldloc instructions that loaded array and index
        var indexInstr = Instructions[Index - 1];
        var arrayInstr = Instructions[Index - 2];

        int? indexLocalIdx = GetLdlocIndex(indexInstr);
        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);

        if (indexLocalIdx == null || arrayLocalIdx == null)
            throw new NotImplementedException("Ldelem_u1 only supports Ldloc patterns for array and index");

        var indexLocal = Locals[indexLocalIdx.Value];
        var arrayLocal = Locals[arrayLocalIdx.Value];

        // Remove the previously emitted instructions from WriteLdloc calls
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit: LDX index_addr; LDA array_base,X
        if (indexLocal.Address is not null)
        {
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)indexLocal.Address);
        }

        if (arrayLocal.ArraySize > 0 && arrayLocal.Address is not null)
        {
            // RAM array: use absolute,X
            Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)arrayLocal.Address);
        }
        else if (arrayLocal.LabelName is not null)
        {
            // ROM array: use label,X
            EmitWithLabel(Opcode.LDA, AddressMode.AbsoluteX, arrayLocal.LabelName);
        }
        else
        {
            throw new NotImplementedException("Ldelem_u1: array local has no address or label");
        }

        Stack.Push(0); // Push a placeholder value
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = true;
    }

    /// <summary>
    /// Handles stelem.i1: stores a byte to an array at a runtime index.
    /// Scans backward through IL to identify the full pattern, removes previously emitted
    /// instructions, and generates correct 6502 code from scratch.
    /// 
    /// Supported patterns:
    /// 1. arr[i] = call()        → call; LDX idx; STA arr,X
    /// 2. arr[i] = (call() &amp; N) - M → call; AND #N; SEC; SBC #M; LDX idx; STA arr,X
    /// 3. arr[i] = arr[i] + arr2[i] → LDX idx; LDA arr,X; CLC; ADC arr2,X; STA arr,X
    /// </summary>
    void HandleStelemI1()
    {
        if (Instructions is null)
            throw new InvalidOperationException("HandleStelemI1 requires Instructions");

        if (Stack.Count >= 3) { Stack.Pop(); Stack.Pop(); Stack.Pop(); }
        else Stack.Clear();

        // Analyze the IL pattern by scanning backward to find:
        // - The target array and index (from the first two ldlocs in the stelem sequence)
        // - The value expression type
        int targetArrayLocalIdx = -1;
        int targetIndexLocalIdx = -1;
        int targetArrayILOffset = -1;

        // Collect the value expression info
        var valueOps = new List<ILInstruction>();

        // Walk backward from stelem to find all components
        // The pattern is: ldloc(arr), ldloc(idx), <value_expression>, stelem_i1
        // where <value_expression> can be:
        //   call rand8
        //   call rand8, ldc 7, and, ldc 3, sub, conv_u1
        //   ldloc(arr), ldloc(idx), ldelem_u1, ldloc(arr2), ldloc(idx), ldelem_u1, add, conv_u1

        // Find the boundary between target (arr, idx) and value expression
        // by counting stack depth
        int depth = 0; // We need 3 values consumed (val, idx, arr)
        int valueStart = -1;
        
        for (int i = Index - 1; i >= 0; i--)
        {
            var il = Instructions[i];
            
            // Categorize by stack effect
            int push = 0, pop = 0;
            switch (il.OpCode)
            {
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1: case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                case ILOpCode.Ldloc_s:
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                    push = 1; break;
                case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Xor:
                    pop = 2; push = 1; break;
                case ILOpCode.Ldelem_u1:
                    pop = 2; push = 1; break;
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2: case ILOpCode.Conv_u4: case ILOpCode.Conv_u8:
                    break; // no net change
                case ILOpCode.Call:
                    // Determine how many args the call takes
                    int args = il.String != null ? _reflectionCache.GetNumberOfArguments(il.String) : 0;
                    pop = args;
                    push = (il.String != null && _reflectionCache.HasReturnValue(il.String)) ? 1 : 0;
                    break;
                default:
                    break;
            }
            
            depth += push - pop;
            
            // When depth reaches 3, we've found the start of the stelem args
            // (arr push=1, idx push=1, value push=1, = 3 total pushes for stelem's 3 pops)
            if (depth >= 3)
            {
                // This instruction is the array ldloc (first push)
                var locIdx = GetLdlocIndex(il);
                if (locIdx != null)
                {
                    targetArrayLocalIdx = locIdx.Value;
                    targetArrayILOffset = il.Offset;
                    
                    // The next instruction should be the index ldloc
                    if (i + 1 < Index)
                    {
                        var nextIl = Instructions[i + 1];
                        var nextLocIdx = GetLdlocIndex(nextIl);
                        if (nextLocIdx != null)
                            targetIndexLocalIdx = nextLocIdx.Value;
                    }
                    
                    valueStart = i + 2; // Value expression starts after arr + idx
                }
                break;
            }
        }

        if (targetArrayLocalIdx < 0 || targetIndexLocalIdx < 0)
        {
            // Constant-index stelem (from array initialization) — no runtime code needed
            return;
        }

        var targetArray = Locals[targetArrayLocalIdx];
        var targetIndex = Locals[targetIndexLocalIdx];

        if (targetIndex.Address is null)
            throw new InvalidOperationException("Stelem_i1: index local has no address");

        // Remove ALL previously emitted instructions for this stelem sequence
        if (_blockCountAtILOffset.TryGetValue(targetArrayILOffset, out int blockCountAtTarget))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtTarget;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Now re-emit the entire sequence from scratch
        // Analyze the value expression
        bool hasCall = false;
        string? callName = null;
        bool hasAnd = false;
        int andMask = 0;
        bool hasSub = false;
        int subValue = 0;
        bool hasAdd = false;
        bool hasTwoLdelems = false;
        int sourceArray1Idx = -1;
        int sourceArray2Idx = -1;

        for (int i = valueStart; i < Index; i++)
        {
            var il = Instructions[i];
            switch (il.OpCode)
            {
                case ILOpCode.Call:
                    hasCall = true;
                    callName = il.String;
                    break;
                case ILOpCode.And:
                    hasAnd = true;
                    break;
                case ILOpCode.Sub:
                    hasSub = true;
                    break;
                case ILOpCode.Add:
                    hasAdd = true;
                    break;
                case ILOpCode.Ldelem_u1:
                    if (sourceArray1Idx < 0)
                    {
                        // Find the array local for this ldelem
                        for (int j = i - 1; j >= valueStart; j--)
                        {
                            var arrIdx = GetLdlocIndex(Instructions[j]);
                            if (arrIdx != null && Locals.TryGetValue(arrIdx.Value, out var loc) && loc.ArraySize > 0)
                            {
                                sourceArray1Idx = arrIdx.Value;
                                break;
                            }
                        }
                    }
                    else
                    {
                        hasTwoLdelems = true;
                        for (int j = i - 1; j >= valueStart; j--)
                        {
                            var arrIdx = GetLdlocIndex(Instructions[j]);
                            if (arrIdx != null && Locals.TryGetValue(arrIdx.Value, out var loc) && loc.ArraySize > 0 
                                && arrIdx.Value != sourceArray1Idx)
                            {
                                sourceArray2Idx = arrIdx.Value;
                                break;
                            }
                        }
                    }
                    break;
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                    {
                        int val = il.OpCode - ILOpCode.Ldc_i4_0;
                        // Check what operation consumes this constant
                        for (int j = i + 1; j < Index; j++)
                        {
                            if (Instructions[j].OpCode == ILOpCode.And) { andMask = val; break; }
                            if (Instructions[j].OpCode == ILOpCode.Sub) { subValue = val; break; }
                            break;
                        }
                    }
                    break;
                case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                    {
                        int val = il.Integer ?? 0;
                        for (int j = i + 1; j < Index; j++)
                        {
                            if (Instructions[j].OpCode == ILOpCode.And) { andMask = val; break; }
                            if (Instructions[j].OpCode == ILOpCode.Sub) { subValue = val; break; }
                            break;
                        }
                    }
                    break;
            }
        }

        // Generate 6502 code based on the pattern
        if (hasTwoLdelems && hasAdd)
        {
            // Pattern: arr[i] = arr1[i] + arr2[i]
            var src1 = Locals[sourceArray1Idx];
            var src2 = Locals[sourceArray2Idx >= 0 ? sourceArray2Idx : sourceArray1Idx];
            
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)targetIndex.Address!);
            Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)src1.Address!);
            Emit(Opcode.CLC, AddressMode.Implied);
            Emit(Opcode.ADC, AddressMode.AbsoluteX, (ushort)src2.Address!);
        }
        else if (hasCall && callName != null)
        {
            // Pattern: arr[i] = call() or arr[i] = (call() & N) - M
            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, callName);
            UsedMethods?.Add(callName);
            
            if (hasAnd)
            {
                Emit(Opcode.AND, AddressMode.Immediate, checked((byte)andMask));
            }
            if (hasSub)
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Immediate, checked((byte)subValue));
            }
        }
        else
        {
            // Unknown pattern — the value should already be in A from previous code
            // This shouldn't happen for our patterns
        }

        // Store to target array
        if (!hasTwoLdelems || !hasAdd)
        {
            // Need to load X with index (wasn't loaded yet for call patterns)
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)targetIndex.Address!);
        }
        
        if (targetArray.ArraySize > 0 && targetArray.Address is not null)
        {
            Emit(Opcode.STA, AddressMode.AbsoluteX, (ushort)targetArray.Address);
        }
        else if (targetArray.LabelName is not null)
        {
            EmitWithLabel(Opcode.STA, AddressMode.AbsoluteX, targetArray.LabelName);
        }
        else
        {
            throw new NotImplementedException($"Stelem_i1: array local {targetArrayLocalIdx} has no address or label. ArraySize={targetArray.ArraySize}, Address={targetArray.Address}, LabelName={targetArray.LabelName}, Value={targetArray.Value}. Known locals: {string.Join(", ", Locals.Select(kv => $"[{kv.Key}]=(V={kv.Value.Value}, A={kv.Value.Address}, AS={kv.Value.ArraySize}, L={kv.Value.LabelName})"))}");
        }

        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = false;
        _savedRuntimeToTemp = false;
    }
}