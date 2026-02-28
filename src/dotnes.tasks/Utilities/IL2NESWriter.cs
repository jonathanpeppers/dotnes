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
    public IL2NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null, ReflectionCache? reflectionCache = null)
        : base(stream, leaveOpen, logger)
    {
        if (reflectionCache != null)
            _reflectionCache = reflectionCache;
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
    ushort _padReloadAddress; // Address to reload pad_poll result from (set by stloc after pad_poll)
    readonly ReflectionCache _reflectionCache = new();
    ILOpCode previous;
    string? _pendingArrayType;
    int _pendingStructArrayCount;
    ushort? _pendingStructArrayBase; // Pre-allocated base address from newarr for struct arrays
    ImmutableArray<byte>? _pendingUShortArray;
    
    /// <summary>
    /// Tracks if pad_poll was called and the result is available in A or _padReloadAddress.
    /// When true, AND operations should emit actual 6502 AND instruction.
    /// </summary>
    bool _padPollResultAvailable;

    /// <summary>
    /// Tracks if this is the first AND after pad_poll (A still has value) or
    /// subsequent AND (need to reload from _padReloadAddress first).
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
    /// Set by WriteLdloc when loading a byte array local. The address was pushed via pushax;
    /// fastcall functions (pal_bg, pal_spr, pal_all) need popax to retrieve it into A:X.
    /// </summary>
    string? _ldlocByteArrayLabel;

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
    /// True when a runtime NTADR result (A=lo, X=hi) is available.
    /// Used by vrambuf_put handler to detect runtime vs compile-time address.
    /// </summary>
    bool _ntadrRuntimeResult;

    /// <summary>
    /// True when A and X hold a 16-bit value (A=lo, X=hi) from a ushort load.
    /// Used by WriteLdc to emit pushax instead of pusha.
    /// </summary>
    bool _ushortInAX;

    /// <summary>
    /// Number of parameters for the current user method being transpiled (0 for main).
    /// Used by ldarg handlers to compute cc65 stack offsets.
    /// </summary>
    public int MethodParamCount { get; init; }

    /// <summary>
    /// Tracks extra values pushed to the cc65 stack within a user function body
    /// (via pusha between ldarg calls). Used to adjust ldarg stack offsets.
    /// </summary>
    int _argStackAdjust;

    /// <summary>
    /// Local variable indices that are word-sized (ushort). Detected by pre-scanning
    /// for conv.u2 + stloc patterns in the IL. Word locals get 2 bytes of zero page.
    /// </summary>
    public HashSet<int> WordLocals { get; init; } = new();

    /// <summary>
    /// Struct type layouts: type name → ordered list of (fieldName, fieldSizeInBytes).
    /// Field offsets are cumulative from the first field.
    /// </summary>
    public Dictionary<string, List<(string Name, int Size)>> StructLayouts { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Maps local variable index → struct type name for struct-typed locals.
    /// Set when ldloca.s + stfld/ldfld patterns are detected.
    /// </summary>
    readonly Dictionary<int, string> _structLocalTypes = new();

    /// <summary>
    /// The local index targeted by the most recent ldloca.s instruction.
    /// Used by stfld/ldfld to know which struct local to access.
    /// </summary>
    int? _pendingStructLocal;

    /// <summary>
    /// Computed base address for a struct array element after ldelema with constant index.
    /// Used by stfld/ldfld to know which struct element to access.
    /// </summary>
    ushort? _pendingStructElementBase;

    /// <summary>
    /// The struct type from the most recent ldelema instruction.
    /// </summary>
    string? _pendingStructElementType;

    /// <summary>
    /// When true, X register holds the element byte-offset within the struct array
    /// (index * structSize) after ldelema with a variable index.
    /// stfld/ldfld should use AbsoluteX addressing.
    /// </summary>
    bool _pendingStructArrayRuntimeIndex;

    /// <summary>
    /// Base address of the struct array for variable-index ldelema.
    /// stfld/ldfld computes field address as this + fieldOffset, then uses ,X.
    /// </summary>
    ushort _structArrayBaseForRuntimeIndex;

    /// <summary>
    /// Maps user-defined static field names to their allocated absolute addresses.
    /// Static fields are allocated in the same region as locals ($0325+).
    /// </summary>
    readonly Dictionary<string, ushort> _staticFieldAddresses = new(StringComparer.Ordinal);

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
    public int LocalCount { get; set; }

    /// <summary>
    /// Set of user-defined method names (for detecting user method calls).
    /// </summary>
    public HashSet<string> UserMethodNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Names of extern methods (declared with static extern). Used to emit JSR _name.
    /// </summary>
    public HashSet<string> ExternMethodNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Merges a string table entry from a user method writer into this writer.
    /// </summary>
    public void MergeStringTableEntry(string label, byte[] data)
    {
        if (!_stringTable.Any(s => s.Label == label))
            _stringTable.Add((label, data));
    }

    /// <summary>
    /// Merges a byte array from a user method writer into this writer.
    /// </summary>
    public void MergeByteArray(ImmutableArray<byte> data)
    {
        _byteArrays.Add(data);
        _byteArrayLabelIndex++; // Keep label indices in sync with merged arrays
    }

    record Local(int Value, int? Address = null, string? LabelName = null, int ArraySize = 0, bool IsWord = false, string? StructArrayType = null);

    /// <summary>
    /// Tracks the buffered block instruction count at the START of processing each IL instruction.
    /// Key = IL instruction offset, Value = block instruction count before processing.
    /// Used by EmitOamSprDecsp4 to remove previously emitted argument instructions.
    /// </summary>
    readonly Dictionary<int, int> _blockCountAtILOffset = new();

    /// <summary>
    /// Emits a JSR to a label reference.
    /// </summary>
    internal void EmitJSR(string labelName) => EmitWithLabel(Opcode.JSR, AddressMode.Absolute, labelName);

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
    /// Emits a CMP instruction for branch comparison.
    /// Peeks at the last emitted instruction: if it's LDA with Absolute/ZeroPage mode
    /// (runtime variable), uses CMP $addr. Otherwise removes the LDA #imm and uses CMP #imm.
    /// stackValue is the value popped from IL stack (correct for constants, 0 for runtime).
    /// For <= and > comparisons, pass adjustValue=1 to compare with value+1.
    /// </summary>
    void EmitBranchCompare(int stackValue, int adjustValue = 0)
    {
        var block = CurrentBlock!;
        if (block.Count > 0)
        {
            var lastInstr = block[block.Count - 1];
            if (lastInstr.Opcode == Opcode.LDA
                && lastInstr.Mode is AddressMode.Absolute or AddressMode.ZeroPage
                && lastInstr.Operand is AbsoluteOperand cmpAbsOp)
            {
                // Detect two-runtime-values: either _savedRuntimeToTemp was set,
                // or the preceding instruction is also LDA Absolute/ZeroPage.
                bool twoRuntimeValues = _savedRuntimeToTemp;
                if (!twoRuntimeValues && block.Count >= 2)
                {
                    var prevInstr = block[block.Count - 2];
                    twoRuntimeValues = prevInstr.Opcode == Opcode.LDA
                        && prevInstr.Mode is AddressMode.Absolute or AddressMode.ZeroPage;
                }

                if (twoRuntimeValues)
                {
                    // Two runtime values: remove second LDA (value2), emit CMP $addr.
                    // A retains value1 from the preceding instruction.
                    RemoveLastInstructions(1);
                    if (adjustValue == 0)
                    {
                        Emit(Opcode.CMP, AddressMode.Absolute, cmpAbsOp.Address);
                    }
                    else
                    {
                        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.LDA, AddressMode.Absolute, cmpAbsOp.Address);
                        Emit(Opcode.CLC, AddressMode.Implied);
                        Emit(Opcode.ADC, AddressMode.Immediate, (byte)adjustValue);
                        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(TEMP + 1));
                        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.CMP, AddressMode.ZeroPage, (byte)(TEMP + 1));
                    }
                    _savedRuntimeToTemp = false;
                }
                else
                {
                    // Single runtime value + constant: keep the LDA, emit CMP #constant.
                    Emit(Opcode.CMP, AddressMode.Immediate, checked((byte)(stackValue + adjustValue)));
                }
                return;
            }
        }
        // Constant comparison: remove last LDA #imm, emit CMP #imm
        RemoveLastInstructions(1);
        Emit(Opcode.CMP, AddressMode.Immediate, checked((byte)(stackValue + adjustValue)));
    }

    /// <summary>
    /// Emits a JMP to a label reference.
    /// </summary>
    void EmitJMP(string labelName) => EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);

    public void Write(ILInstruction instruction)
    {
        // Clear ldloc byte array label for non-ldloc instructions
        if (instruction.OpCode is not (ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
            or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s))
            _ldlocByteArrayLabel = null;

        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
            case ILOpCode.Ret:
                // Ret is handled at block level (RTS appended to user method blocks)
                break;
            case ILOpCode.Dup:
                if (Stack.Count > 0)
                    Stack.Push(Stack.Peek());
                break;
            case ILOpCode.Pop:
                if (Stack.Count > 0)
                    Stack.Pop();
                _runtimeValueInA = false;
                break;
            case ILOpCode.Ldc_i4_m1:
                WriteLdc(0xFF); // -1 in two's complement = 0xFF
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
                    Locals[0] = new Local(Stack.Pop(), Locals[0].Address, IsWord: Locals[0].IsWord);
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
                else if (previous == ILOpCode.Newarr)
                {
                    HandleStlocAfterNewarr(0);
                }
                else
                {
                    var addr = Locals.TryGetValue(0, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[0] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(0) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult));
                }
                break;
            case ILOpCode.Stloc_1:
                if (_pendingIncDecLocal == 1)
                {
                    Locals[1] = new Local(Stack.Pop(), Locals[1].Address, IsWord: Locals[1].IsWord);
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
                    HandleStlocAfterNewarr(1);
                }
                else
                {
                    var addr = Locals.TryGetValue(1, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[1] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(1) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult));
                }
                break;
            case ILOpCode.Stloc_2:
                if (_pendingIncDecLocal == 2)
                {
                    Locals[2] = new Local(Stack.Pop(), Locals[2].Address, IsWord: Locals[2].IsWord);
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
                    HandleStlocAfterNewarr(2);
                }
                else
                {
                    var addr = Locals.TryGetValue(2, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[2] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(2) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult));
                }
                break;
            case ILOpCode.Stloc_3:
                if (_pendingIncDecLocal == 3)
                {
                    Locals[3] = new Local(Stack.Pop(), Locals[3].Address, IsWord: Locals[3].IsWord);
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
                    HandleStlocAfterNewarr(3);
                }
                else
                {
                    var addr = Locals.TryGetValue(3, out var existing) && existing.Address is not null
                        ? (ushort)existing.Address : (ushort)(local + LocalCount);
                    WriteStloc(Locals[3] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(3) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult));
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
            case ILOpCode.Ldarg_0:
            case ILOpCode.Ldarg_1:
            case ILOpCode.Ldarg_2:
            case ILOpCode.Ldarg_3:
                {
                    int argIndex = instruction.OpCode - ILOpCode.Ldarg_0;
                    WriteLdarg(argIndex);
                }
                break;
            case ILOpCode.Conv_u1:
                // When truncating from ushort to byte, discard high byte
                if (_ushortInAX)
                    _ushortInAX = false;
                break;
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_u4:
            case ILOpCode.Conv_u8:
            case ILOpCode.Conv_i1:
            case ILOpCode.Conv_i2:
            case ILOpCode.Conv_i4:
                // No-op: sign/zero extension is irrelevant on 8-bit 6502
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
                {
                    int val2 = Stack.Pop();
                    int val1 = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (_runtimeValueInA)
                    {
                        // Runtime multiply: use ASL for power-of-2 constants
                        int constant = val2 >= 0 ? val2 : val1;
                        if (constant > 0 && (constant & (constant - 1)) == 0)
                        {
                            int shifts = 0;
                            int temp = constant;
                            while (temp > 1) { temp >>= 1; shifts++; }
                            // Check if the result needs to be 16-bit: look ahead past Add/Sub/Ldc for Conv_u2
                            bool needs16Bit = false;
                            if (Instructions is not null)
                            {
                                for (int look = Index + 1; look < Instructions.Length; look++)
                                {
                                    var lookOp = Instructions[look].OpCode;
                                    if (lookOp == ILOpCode.Conv_u2 || lookOp == ILOpCode.Conv_i2)
                                    {
                                        needs16Bit = true;
                                        break;
                                    }
                                    if (lookOp is ILOpCode.Add or ILOpCode.Sub
                                        or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                                        or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                                        or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                                        or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                                        continue;
                                    break;
                                }
                            }
                            if (needs16Bit)
                            {
                                // 16-bit shift (ASL A + ROL TEMP) to capture overflow
                                Emit(Opcode.LDX, AddressMode.Immediate, 0);
                                Emit(Opcode.STX, AddressMode.ZeroPage, 0x17); // TEMP = 0 (high byte)
                                for (int s = 0; s < shifts; s++)
                                {
                                    Emit(Opcode.ASL, AddressMode.Accumulator);
                                    Emit(Opcode.ROL, AddressMode.ZeroPage, 0x17);
                                }
                                Emit(Opcode.LDX, AddressMode.ZeroPage, 0x17); // X = high byte
                                _ushortInAX = true;
                            }
                            else
                            {
                                // 8-bit shift only
                                for (int s = 0; s < shifts; s++)
                                    Emit(Opcode.ASL, AddressMode.Accumulator);
                            }
                        }
                        Stack.Push(val1 * val2);
                    }
                    else
                    {
                        Stack.Push(val1 * val2);
                    }
                }
                break;
            case ILOpCode.Div:
                {
                    int divisor = Stack.Pop();
                    int dividend = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool divLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var divLocal) && divLocal.Address != null;

                    if (_runtimeValueInA || divLocalInA)
                    {
                        // Remove the LDA #divisor emitted by WriteLdc
                        if (!_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_m1
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        // Emit LSR A for power-of-2 divisors
                        if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                        {
                            int shifts = 0;
                            int temp = divisor;
                            while (temp > 1) { temp >>= 1; shifts++; }
                            for (int i = 0; i < shifts; i++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                        }
                        else
                        {
                            throw new NotImplementedException($"Runtime division by non-power-of-2 ({divisor}) not supported");
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0);
                    }
                    else
                    {
                        // Compile-time: dividend / divisor (correct operand order)
                        Stack.Push(dividend / divisor);
                    }
                }
                break;
            case ILOpCode.Rem:
                {
                    int divisor = Stack.Pop();
                    int dividend = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool remLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var remLocal) && remLocal.Address != null;

                    if (_runtimeValueInA || remLocalInA)
                    {
                        // Remove the LDA #divisor emitted by WriteLdc
                        if (!_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_m1
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                        {
                            // Power-of-2: x % N == x AND (N-1)
                            Emit(Opcode.AND, AddressMode.Immediate, (byte)(divisor - 1));
                        }
                        else if (_savedRuntimeToTemp)
                        {
                            // Runtime divisor (in A) and runtime dividend (in TEMP)
                            // Save divisor to TEMP2, load dividend from TEMP, then loop
                            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.CMP, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.BCC, AddressMode.Relative, 4);
                            Emit(Opcode.SBC, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-8));
                            _savedRuntimeToTemp = false;
                        }
                        else
                        {
                            // General 8-bit modulo via repeated subtraction
                            // A = dividend (runtime). Result (remainder) left in A.
                            //   SEC           ; 1 byte  (offset 0)
                            //   CMP #divisor  ; 2 bytes (offset 1) ← @loop
                            //   BCC @done     ; 2 bytes (offset 3) → +4 to @done
                            //   SBC #divisor  ; 2 bytes (offset 5)
                            //   BCS @loop     ; 2 bytes (offset 7) → -8 to CMP
                            //   @done:        ;         (offset 9)
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.CMP, AddressMode.Immediate, (byte)divisor);
                            Emit(Opcode.BCC, AddressMode.Relative, 4); // skip SBC+BCS → @done
                            Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-8)); // back to CMP
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0);
                    }
                    else
                    {
                        // Compile-time
                        Stack.Push(dividend % divisor);
                    }
                }
                break;
            case ILOpCode.Shr:
            case ILOpCode.Shr_un:
                {
                    int shiftCount = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool shrLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var shrLocal) && shrLocal.Address != null;

                    if (_runtimeValueInA || shrLocalInA || _ushortInAX)
                    {
                        if (!_runtimeValueInA && !_ushortInAX
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }
                        if (_ushortInAX && shiftCount >= 8)
                        {
                            // ushort >> 8+: high byte to A, then shift remaining
                            Emit(Opcode.TXA, AddressMode.Implied);
                            for (int i = 0; i < shiftCount - 8; i++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                            _ushortInAX = false;
                        }
                        else if (_ushortInAX)
                        {
                            // ushort >> N (N < 8): shift both bytes
                            for (int i = 0; i < shiftCount; i++)
                            {
                                Emit(Opcode.STX, AddressMode.ZeroPage, 0x17);
                                Emit(Opcode.LSR, AddressMode.ZeroPage, 0x17);
                                Emit(Opcode.ROR, AddressMode.Accumulator);
                                Emit(Opcode.LDX, AddressMode.ZeroPage, 0x17);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < shiftCount; i++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                            _ushortInAX = false;
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0);
                    }
                    else
                    {
                        Stack.Push(value >> shiftCount);
                    }
                }
                break;
            case ILOpCode.Shl:
                {
                    int shiftCount = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool shlLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var shlLocal) && shlLocal.Address != null;

                    if (_runtimeValueInA || shlLocalInA)
                    {
                        if (!_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }
                        for (int i = 0; i < shiftCount; i++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        _runtimeValueInA = true;
                        Stack.Push(0);
                    }
                    else
                    {
                        Stack.Push(value << shiftCount);
                    }
                }
                break;
            case ILOpCode.And:
                {
                    int mask = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    // Check if the value came from a local variable load (runtime value)
                    bool localInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var andLocal) && andLocal.Address != null;

                    // Emit runtime AND if A has a runtime value
                    if (_padPollResultAvailable || _runtimeValueInA || localInA)
                    {
                        // Remove the LDA #mask that was emitted by Ldc_i4*
                        // Only remove if WriteLdc actually emitted LDA (not skipped due to _runtimeValueInA)
                        if (!_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 
                            or ILOpCode.Ldc_i4_m1
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        // If not first AND after pad_poll, need to reload pad value
                        if (_padPollResultAvailable && !_firstAndAfterPadPoll)
                        {
                            Emit(Opcode.LDA, AddressMode.Absolute, _padReloadAddress);
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
                {
                    int orMask = Stack.Pop();
                    int orValue = Stack.Count > 0 ? Stack.Pop() : 0;

                    // Check if the value came from a local variable load (runtime value)
                    bool orLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var orLocal) && orLocal.Address != null;

                    if (_runtimeValueInA || orLocalInA)
                    {
                        // Remove the LDA #mask emitted by WriteLdc
                        if (!_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_m1
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }

                        Emit(Opcode.ORA, AddressMode.Immediate, checked((byte)orMask));
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(orValue | orMask);
                    }
                }
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
        _ldlocByteArrayLabel = null;
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ldarg_s:
                WriteLdarg(operand);
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
                {
                    bool isStructArray = instruction.String != null && StructLayouts.ContainsKey(instruction.String);
                    if (isStructArray)
                    {
                        // Struct array: remove the LDA for the count (purely compile-time allocation)
                        bool isLdcPrevious = previous == ILOpCode.Ldc_i4_s || previous == ILOpCode.Ldc_i4
                            || (previous >= ILOpCode.Ldc_i4_0 && previous <= ILOpCode.Ldc_i4_8);
                        if (isLdcPrevious)
                        {
                            int toRemove = Stack.Count > 0 && Stack.Peek() > byte.MaxValue ? 2 : 1;
                            RemoveLastInstructions(toRemove);
                        }
                        _pendingStructArrayCount = Stack.Count > 0 ? Stack.Peek() : 0;
                        // Pre-allocate the struct array now (optimizer uses dup before stloc)
                        int structSize = GetStructSize(instruction.String!);
                        int totalBytes = _pendingStructArrayCount * structSize;
                        _pendingStructArrayBase = (ushort)(local + LocalCount);
                        LocalCount += totalBytes;
                    }
                    else if (previous == ILOpCode.Ldc_i4_s || previous == ILOpCode.Ldc_i4
                        || (previous >= ILOpCode.Ldc_i4_0 && previous <= ILOpCode.Ldc_i4_8))
                    {
                        // Remove LDA emitted for the array size constant
                        int toRemove = Stack.Count > 0 && Stack.Peek() > byte.MaxValue ? 2 : 1;
                        RemoveLastInstructions(toRemove);
                    }
                    // Track the array element type so the next Ldtoken can handle non-byte arrays
                    _pendingArrayType = instruction.String;
                    if (_pendingArrayType != null && _pendingArrayType != "Byte")
                    {
                        if (Stack.Count > 0)
                            Stack.Pop();
                    }
                }
                break;
            case ILOpCode.Stloc_s:
                {
                    int localIdx = operand;
                    if (_pendingIncDecLocal == localIdx)
                    {
                        // INC/DEC was already emitted, just update tracking
                        Locals[localIdx] = new Local(Stack.Pop(), Locals[localIdx].Address, IsWord: Locals[localIdx].IsWord);
                        _pendingIncDecLocal = null;
                    }
                    else if (previous == ILOpCode.Newarr)
                    {
                        HandleStlocAfterNewarr(localIdx);
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
                        // Regular local — allocate address and store
                        var addr = Locals.TryGetValue(localIdx, out var existing) && existing.Address is not null
                            ? (ushort)existing.Address : (ushort)(local + LocalCount);
                        WriteStloc(Locals[localIdx] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(localIdx) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult));
                    }
                }
                break;
            case ILOpCode.Ldloc_s:
                WriteLdloc(Locals[operand]);
                _lastLoadedLocalIndex = operand;
                break;
            case ILOpCode.Bne_un_s:
            case ILOpCode.Bne_un:
                // Branch if not equal (unsigned)
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Bne_un_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Bne_un_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal);
                    
                    // If the last instruction is INC/DEC (from x++ pattern),
                    // A doesn't have the variable's value. Re-emit LDA to reload it.
                    var bneBlock = CurrentBlock!;
                    if (bneBlock.Count > 0)
                    {
                        var lastInstr = bneBlock[bneBlock.Count - 1];
                        if (lastInstr.Opcode is Opcode.INC or Opcode.DEC
                            && lastInstr.Operand is AbsoluteOperand absOp)
                            Emit(Opcode.LDA, AddressMode.Absolute, absOp.Address);
                    }
                    
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Bne_un_s)
                        EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BEQ, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Beq_s:
            case ILOpCode.Beq:
                // Branch if equal: value1 == value2
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Beq_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Beq_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal);
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Beq_s)
                        EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BNE, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Brfalse_s:
                // Branch if value is zero/false (after AND test)
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Brtrue_s:
                // Branch if value is non-zero/true — inverse of Brfalse_s
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = $"instruction_{instruction.Offset + operand + 2:X2}";
                    EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Blt_s:
            case ILOpCode.Blt:
                // Branch if less than (signed): value1 < value2
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Blt_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Blt_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal);
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Blt_s)
                        EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCS, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                }
                break;
            case ILOpCode.Ble_s:
            case ILOpCode.Ble:
                // Branch if less than or equal: CMP #(value2+1) + BCC/trampoline
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Ble_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Ble_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal, adjustValue: 1);
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Ble_s)
                        EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCS, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                }
                break;
            case ILOpCode.Bge_s:
            case ILOpCode.Bge:
                // Branch if greater than or equal: CMP #value2 + BCS/trampoline
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Bge_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Bge_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal);
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Bge_s)
                        EmitWithLabel(Opcode.BCS, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCC, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                }
                break;
            case ILOpCode.Bgt_s:
            case ILOpCode.Bgt:
                // Branch if greater than: CMP #(value2+1) + BCS/trampoline
                {
                    int branchOffset = instruction.OpCode == ILOpCode.Bgt_s
                        ? (sbyte)(byte)operand : operand;
                    int instrSize = instruction.OpCode == ILOpCode.Bgt_s ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    EmitBranchCompare(cmpVal, adjustValue: 1);
                    var labelName = $"instruction_{instruction.Offset + branchOffset + instrSize:X2}";
                    if (instruction.OpCode == ILOpCode.Bgt_s)
                        EmitWithLabel(Opcode.BCS, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCC, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                }
                break;
            case ILOpCode.Brtrue:
                // Long-form branch if non-zero — use trampoline: BEQ +3, JMP target
                {
                    var labelName = $"instruction_{instruction.Offset + operand + 5:X2}";
                    Emit(Opcode.BEQ, AddressMode.Relative, 3); // skip JMP if zero
                    EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Brfalse:
                // Long-form branch if zero — use trampoline: BNE +3, JMP target
                {
                    var labelName = $"instruction_{instruction.Offset + operand + 5:X2}";
                    Emit(Opcode.BNE, AddressMode.Relative, 3); // skip JMP if non-zero
                    EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Ldloca_s:
                // Load address of local variable — used for struct field access
                _pendingStructLocal = operand;
                break;
            case ILOpCode.Ldelema:
                HandleLdelema(instruction);
                break;
            case ILOpCode.Switch:
                HandleSwitch(instruction, operand);
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
                bool argsAlreadyPopped = false;
                switch (operand)
                {
                    case nameof(NTADR_A):
                    case nameof(NTADR_B):
                    case nameof(NTADR_C):
                    case nameof(NTADR_D):
                        {
                            var block = CurrentBlock!;
                            // Check if y (last loaded value) is runtime: LDA $abs vs LDA #imm
                            var lastInstr = block[block.Count - 1];
                            bool yIsRuntime = lastInstr.Mode == AddressMode.Absolute
                                && lastInstr.Opcode == Opcode.LDA;

                            // Check if x (first arg) is runtime
                            bool xIsRuntime = false;
                            bool xFromFlag = false; // true when x is runtime via _runtimeValueInA (not LDA Absolute)
                            Instruction? xInstr = null;
                            if (!yIsRuntime && _runtimeValueInA)
                            {
                                // x came from a runtime expression (And/Or/Div etc.)
                                // WriteLdc for y was skipped, so no LDA #y in block
                                xIsRuntime = true;
                                xFromFlag = true;
                            }
                            else if (!yIsRuntime && block.Count >= 2)
                            {
                                var prevInstr = block[block.Count - 2];
                                if (prevInstr.Mode == AddressMode.Absolute && prevInstr.Opcode == Opcode.LDA)
                                {
                                    xIsRuntime = true;
                                    xInstr = prevInstr;
                                }
                            }

                            if (!yIsRuntime && !xIsRuntime)
                            {
                                // Compile-time: both args are constants
                                byte cy = checked((byte)Stack.Pop());
                                byte cx = checked((byte)Stack.Pop());
                                var address = operand switch
                                {
                                    nameof(NTADR_A) => NTADR_A(cx, cy),
                                    nameof(NTADR_B) => NTADR_B(cx, cy),
                                    nameof(NTADR_C) => NTADR_C(cx, cy),
                                    nameof(NTADR_D) => NTADR_D(cx, cy),
                                    _ => throw new InvalidOperationException(),
                                };
                                RemoveLastInstructions(3);
                                Emit(Opcode.LDX, AddressMode.Immediate, checked((byte)(address >> 8)));
                                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)(address & 0xFF)));
                                Stack.Push(address);
                                _runtimeValueInA = false;
                            }
                            else
                            {
                                // At least one arg is runtime — use nametable subroutine
                                int yVal = Stack.Pop(); // y (constant or placeholder)
                                Stack.Pop(); // x (constant or placeholder)
                                string subroutine = operand switch
                                {
                                    nameof(NTADR_A) => "nametable_a",
                                    nameof(NTADR_B) => "nametable_b",
                                    nameof(NTADR_C) => "nametable_c",
                                    nameof(NTADR_D) => "nametable_d",
                                    _ => throw new InvalidOperationException(),
                                };
                                if (yIsRuntime)
                                {
                                    // Runtime y: block has LDA #x, JSR pusha, LDA $y_addr (3 instrs)
                                    var xConstInstr = block[block.Count - 3];
                                    byte cx = ((ImmediateOperand)xConstInstr.Operand!).Value;
                                    RemoveLastInstructions(3);
                                    Emit(Opcode.LDA, AddressMode.Immediate, cx);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    block.Emit(lastInstr); // re-emit LDA $y_addr
                                }
                                else if (xFromFlag)
                                {
                                    // Runtime x from expression (And/Div etc.), constant y
                                    // WriteLdc for y was skipped (_runtimeValueInA), so no LDA #y in block
                                    byte cy = checked((byte)yVal);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.LDA, AddressMode.Immediate, cy);
                                }
                                else
                                {
                                    // Runtime x from local load, constant y
                                    // Block has: LDA $x_addr, LDA #y (2 instrs)
                                    byte cy = ((ImmediateOperand)lastInstr.Operand!).Value;
                                    RemoveLastInstructions(2);
                                    block.Emit(xInstr!); // re-emit LDA $x_addr
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.LDA, AddressMode.Immediate, cy);
                                }
                                EmitWithLabel(Opcode.JSR, AddressMode.Absolute, subroutine);
                                UsedMethods?.Add(subroutine);
                                // Save result: A=lo→TEMP2, X=hi→TEMP
                                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
                                Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                                Stack.Push(0); // placeholder for runtime address
                                _runtimeValueInA = false;
                                _ntadrRuntimeResult = true;
                            }
                            argsAlreadyPopped = true;
                        }
                        break;
                    case "pad_poll":
                        // pad_poll returns result in A — store to dynamically allocated temp
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        if (_padReloadAddress == 0)
                        {
                            _padReloadAddress = (ushort)(local + LocalCount);
                            LocalCount += 1;
                        }
                        Emit(Opcode.STA, AddressMode.Absolute, _padReloadAddress);
                        _padPollResultAvailable = true;
                        _firstAndAfterPadPoll = true;
                        _immediateInA = null;
                        break;
                    case "oam_spr":
                        EmitOamSprDecsp4();
                        break;
                    case nameof(NESLib.oam_meta_spr):
                        EmitOamMetaSpr();
                        break;
                    case nameof(NESLib.oam_meta_spr_pal):
                        EmitOamMetaSprPal();
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
                    case nameof(NESLib.nmi_set_callback):
                    case nameof(NESLib.famitone_init):
                    case nameof(NESLib.sfx_init):
                        {
                            // These functions take a string label name as argument.
                            // The Ldstr emitted: LDA #<string_N, LDX #>string_N, JSR pushax, LDX #0, LDA #len
                            // Replace with: LDA #<_label, LDX #>_label, JSR target
                            var block = CurrentBlock!;
                            string? labelName = null;
                            if (block.Count >= 5)
                            {
                                var ldaStringInstr = block[block.Count - 5];
                                if (ldaStringInstr.Operand is LowByteOperand lbo)
                                {
                                    foreach (var entry in _stringLabelMap)
                                    {
                                        if (entry.Value == lbo.Label)
                                        {
                                            labelName = entry.Key;
                                            break;
                                        }
                                    }
                                }
                            }
                            RemoveLastInstructions(5);
                            if (labelName != null)
                            {
                                string label = $"_{labelName}";
                                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, label);
                                EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, label);
                            }
                            // nmi_set_callback is a built-in; famitone_init/sfx_init are extern (always _ prefix)
                            string jsrTarget = operand == nameof(NESLib.nmi_set_callback)
                                ? "nmi_set_callback"
                                : $"_{operand}";
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, jsrTarget);
                            if (operand == nameof(NESLib.nmi_set_callback))
                                UsedMethods?.Add("nmi_set_callback");
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
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
                    case "split":
                    case "scroll":
                        // scroll()/split() takes unsigned int params, which use popax (2-byte pop).
                        // Patterns for the second arg (Y):
                        // 1. Byte constant: last instr = LDA #yy
                        // 2. Byte local: last instr = LDA $addr
                        // 3. Ushort constant: LDX #hi was emitted before LDA #lo
                        // 4. Ushort local: last 2 instrs = LDA $lo, LDX $hi (second arg already in A:X)
                        {
                            var block = CurrentBlock!;
                            var lastInstr = block[block.Count - 1];

                            // Pattern 4: ushort local — last instr is LDX (high byte)
                            if (lastInstr.Opcode == Opcode.LDX && lastInstr.Mode == AddressMode.Absolute)
                            {
                                // Second arg is already in A:X (LDA lo + LDX hi from WriteLdloc)
                                // Keep both instructions, look further back for first arg push
                                var ldxInstr = lastInstr;
                                var ldaInstr = block[block.Count - 2]; // LDA $lo
                                var firstArgInstr = block[block.Count - 3]; // first arg's instruction
                                bool firstIsPusha = firstArgInstr.Opcode == Opcode.JSR
                                    && firstArgInstr.Operand is LabelOperand lbl4 && lbl4.Label == "pusha";
                                RemoveLastInstructions(3); // Remove first arg push + LDA lo + LDX hi
                                Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                EmitJSR("pushax");
                                block.Emit(ldaInstr);  // Re-emit LDA $lo
                                block.Emit(ldxInstr);  // Re-emit LDX $hi
                            }
                            else
                            {
                                // Patterns 1-3: last instr is LDA (second arg)
                                var ldaInstr = lastInstr;
                                var prevInstr = block[block.Count - 2];
                                bool alreadyPushax = prevInstr.Opcode == Opcode.JSR
                                    && prevInstr.Operand is LabelOperand lbl && lbl.Label == "pushax";
                                bool hasPusha = prevInstr.Opcode == Opcode.JSR
                                    && prevInstr.Operand is LabelOperand lbl2 && lbl2.Label == "pusha";
                                if (alreadyPushax)
                                {
                                    // Ushort arg already pushed correctly via pushax
                                    RemoveLastInstructions(1); // Remove just LDA (second arg)
                                    block.Emit(ldaInstr); // Re-emit LDA for second arg
                                }
                                else if (hasPusha)
                                {
                                    // Byte constant: replace pusha with LDX #$00 + pushax
                                    RemoveLastInstructions(2); // Remove JSR pusha + LDA
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                    EmitJSR("pushax");
                                    block.Emit(ldaInstr); // Re-emit LDA for second arg
                                }
                                else
                                {
                                    // Local var or runtime value: A has the value, just push it
                                    RemoveLastInstructions(1); // Remove only the second arg's LDA
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                    EmitJSR("pushax");
                                    block.Emit(ldaInstr); // Re-emit second arg LDA
                                }
                            }
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            _immediateInA = null;
                        }
                        break;
                    case nameof(NESLib.vram_fill):
                        // vram_fill(value, count) — count is passed in A:X (16-bit).
                        // WriteLdc(ushort) already sets both A and X.
                        // WriteLdc(byte) only sets A, so X may be garbage — clear it.
                        if (Stack.Count > 0 && Stack.Peek() <= byte.MaxValue)
                        {
                            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                        }
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        _immediateInA = null;
                        break;
                    case nameof(NESLib.vrambuf_put):
                        {
                            var block = CurrentBlock!;

                            // Detect byte array overload via _lastLoadedLocalIndex
                            Local? arrayLocal = null;
                            bool isByteArrayOverload = _lastLoadedLocalIndex.HasValue &&
                                Locals.TryGetValue(_lastLoadedLocalIndex.Value, out arrayLocal) &&
                                arrayLocal.ArraySize > 0;

                            if (isByteArrayOverload)
                            {
                                // vrambuf_put(addr, buf, len) — byte array overload (vertical)
                                int len = checked((int)Stack.Pop());    // length
                                Stack.Pop();                            // array size placeholder
                                int addr = checked((int)Stack.Pop());   // NTADR result

                                ushort arrayAddr = (ushort)arrayLocal!.Address!;

                                // Remove ldloc buf (LDA $arrayAddr) + ldc len (LDA #len)
                                RemoveLastInstructions(2);

                                if (_ntadrRuntimeResult)
                                {
                                    // TEMP/TEMP2 already set by NTADR handler
                                    // OR TEMP (hi) with NT_UPD_VERT (0x80) for vertical writes
                                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.ORA, AddressMode.Immediate, 0x80);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                }
                                else if (block.Count >= 2
                                    && block[block.Count - 1] is { Opcode: Opcode.LDX, Mode: AddressMode.Absolute } ldxInstr
                                    && block[block.Count - 2] is { Opcode: Opcode.LDA, Mode: AddressMode.Absolute } ldaInstr)
                                {
                                    // Address loaded from a ushort local (LDA $lo, LDX $hi)
                                    ushort loAddr = ((AbsoluteOperand)ldaInstr.Operand!).Address;
                                    ushort hiAddr = ((AbsoluteOperand)ldxInstr.Operand!).Address;
                                    RemoveLastInstructions(2);
                                    Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                                    Emit(Opcode.ORA, AddressMode.Immediate, 0x80);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
                                }
                                else
                                {
                                    // Compile-time NTADR
                                    byte addrHi = (byte)(addr >> 8);
                                    byte addrLo = (byte)(addr & 0xFF);
                                    // Remove the compile-time NTADR instructions (LDX #hi, LDA #lo)
                                    RemoveLastInstructions(2);
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(addrHi | 0x80));
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.LDA, AddressMode.Immediate, addrLo);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
                                }

                                // ptr1 = array base address
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(arrayAddr & 0xFF));
                                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(arrayAddr >> 8));
                                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1 + 1);
                                // A = length
                                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)len));
                                EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            }
                            else
                            {
                                // vrambuf_put(NTADR_A(x,y), "string") — string overload
                                int len = checked((int)Stack.Pop());
                                int addr = checked((int)Stack.Pop());

                                // Extract string label: always at -5 from end (Ldstr pattern)
                                var ldaStrInstr = block[block.Count - 5];
                                string strLabel = ((LowByteOperand)ldaStrInstr.Operand!).Label;

                                if (_ntadrRuntimeResult)
                                {
                                    var ntadrInstrs = new Instruction[6];
                                    for (int ri = 0; ri < 6; ri++)
                                        ntadrInstrs[ri] = block[block.Count - 11 + ri];
                                    RemoveLastInstructions(11);
                                    foreach (var instr in ntadrInstrs)
                                        block.Emit(instr);
                                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.ORA, AddressMode.Immediate, 0x40);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                }
                                else
                                {
                                    byte addrHi = (byte)(addr >> 8);
                                    byte addrLo = (byte)(addr & 0xFF);
                                    RemoveLastInstructions(7);
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(addrHi | 0x40));
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    Emit(Opcode.LDA, AddressMode.Immediate, addrLo);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
                                }
                                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, strLabel);
                                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
                                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, strLabel);
                                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1 + 1);
                                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)len));
                                EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            }
                            _immediateInA = null;
                            _ntadrRuntimeResult = false;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case "Array.Fill":
                        HandleArrayFill();
                        argsAlreadyPopped = true;
                        break;
                    case "Array.Copy":
                        HandleArrayCopy();
                        argsAlreadyPopped = true;
                        break;
                    default:
                        // Handle byte array locals loaded via ldloc (pushax pattern).
                        // Fastcall functions (pal_bg, pal_spr, pal_all, vram_unrle) expect
                        // pointer in A:X, not on cc65 stack. Replace pushax+size with just LDA/LDX.
                        if (_ldlocByteArrayLabel != null && operand is nameof(NESLib.pal_bg)
                            or nameof(NESLib.pal_spr) or nameof(NESLib.pal_all) or nameof(NESLib.vram_unrle))
                        {
                            // WriteLdloc emitted: LDA #lo, LDX #hi, JSR pushax, LDX #$00, LDA #size
                            RemoveLastInstructions(5);
                            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, _ldlocByteArrayLabel);
                            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, _ldlocByteArrayLabel);
                        }
                        _ldlocByteArrayLabel = null;
                        if (_lastByteArrayLabel != null && previous != ILOpCode.Ldtoken
                            && !_byteArrayAddressEmitted)
                        {
                            bool needsLoad = _needsByteArrayLoadInCall;
                            // Check if THIS call's arguments include the byte array marker
                            // Only look at the top N stack items that this call will consume
                            int argCount = _reflectionCache.GetNumberOfArguments(operand);
                            if (!needsLoad && argCount > 0)
                            {
                                int checked_ = 0;
                                foreach (var val in Stack)
                                {
                                    if (checked_ >= argCount) break;
                                    if (val < 0) { needsLoad = true; break; }
                                    checked_++;
                                }
                            }
                            if (needsLoad)
                            {
                                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, _lastByteArrayLabel);
                                EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, _lastByteArrayLabel);
                                _needsByteArrayLoadInCall = false;
                            }
                        }
                        // Emit JSR — extern methods use cc65 _prefix convention
                        if (ExternMethodNames.Contains(operand))
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, $"_{operand}");
                        else
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        _immediateInA = null;
                        break;
                }
                // Pop N times (unless handler already popped)
                if (!argsAlreadyPopped)
                {
                    int args = _reflectionCache.GetNumberOfArguments(operand);
                    for (int i = 0; i < args; i++)
                    {
                        if (Stack.Count > 0)
                            Stack.Pop();
                    }
                }
                // Skip post-call tracking for BCL methods (not in ReflectionCache)
                if (argsAlreadyPopped && operand.Contains('.'))
                {
                    _runtimeValueInA = false;
                    _ushortInAX = false;
                    break;
                }
                // Clear byte array label if it was consumed by this call
                // Only clear if this call actually takes arguments (consumes the array)
                // Don't clear for 0-arg calls like ppu_off that follow ldtoken
                if (_lastByteArrayLabel != null && previous == ILOpCode.Ldtoken
                    && _reflectionCache.GetNumberOfArguments(operand) > 0)
                    _lastByteArrayLabel = null;
                // Return value handling
                if (_reflectionCache.HasReturnValue(operand))
                {
                    // Only set _runtimeValueInA for methods that produce true runtime values
                    // Skip: NTADR_* (compile-time computed)
                    if (operand is not (nameof(NTADR_A) or nameof(NTADR_B) or nameof(NTADR_C) or nameof(NTADR_D)))
                    {
                        _runtimeValueInA = true;
                        // 16-bit return (e.g. bcd_add returns ushort): result in A/X
                        _ushortInAX = _reflectionCache.Returns16Bit(operand);
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
                    _ushortInAX = false;
                }
                break;
            case ILOpCode.Stsfld:
                if (operand == nameof(NESLib.oam_off))
                {
                    // Store value to OAM_OFF zero-page address
                    if (_runtimeValueInA)
                    {
                        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
                    }
                    else if (_immediateInA != null)
                    {
                        Emit(Opcode.LDA, AddressMode.Immediate, (byte)_immediateInA);
                        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
                    }
                    _runtimeValueInA = false;
                    _immediateInA = null;
                }
                else
                {
                    HandleStsfld(operand);
                }
                break;
            case ILOpCode.Ldsfld:
                if (operand == nameof(NESLib.oam_off))
                {
                    // Load value from OAM_OFF zero-page address
                    Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.OAM_OFF);
                    _runtimeValueInA = true;
                    _immediateInA = null;
                }
                else
                {
                    HandleLdsfld(operand);
                }
                break;
            case ILOpCode.Stfld:
                HandleStfld(operand);
                break;
            case ILOpCode.Ldfld:
                HandleLdfld(operand);
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
    /// Gets or sets the starting index for byte array labels.
    /// Used to offset user method writers so their labels don't collide with the main writer's.
    /// </summary>
    internal int ByteArrayLabelStartIndex { set => _byteArrayLabelIndex = value; }
    
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
                
                // HACK: write these if next instruction is a Call that consumes this array
                // Don't emit if the next Call is a 0-arg function (e.g., ppu_off)
                // that just happens to follow the ldtoken — the address would get clobbered.
                _byteArrayAddressEmitted = false;
                if (Instructions is not null && Instructions[Index + 1].OpCode == ILOpCode.Call)
                {
                    var nextCallName = Instructions[Index + 1].String;
                    bool nextCallConsumesArray = nextCallName != null 
                        && _reflectionCache.GetNumberOfArguments(nextCallName) > 0;
                    if (nextCallConsumesArray)
                    {
                        EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, byteArrayLabel);
                        EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, byteArrayLabel);
                        _byteArrayAddressEmitted = true;
                    }
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
    /// Handles the IL switch opcode (jump table). Emits CMP/BEQ chains for sequential cases.
    /// The switch value is in A (from preceding ldloc). For each case index 0..N-1,
    /// emit CMP #index; BNE +3; JMP target_label.
    /// </summary>
    void HandleSwitch(ILInstruction instruction, int caseCount)
    {
        if (Stack.Count > 0) Stack.Pop(); // Pop the switch value

        var targets = instruction.Bytes;
        if (targets is null)
            throw new InvalidOperationException("Switch instruction missing target offsets");

        // The instruction after the switch is at: offset + 1 + 4 + caseCount * 4
        int baseOffset = instruction.Offset + 1 + 4 + caseCount * 4;

        // The switch variable should already be in A (from preceding ldloc → LDA)
        // For case 0, we can use BEQ (branch if zero) without CMP
        for (int i = 0; i < caseCount; i++)
        {
            int targetOffset = BitConverter.ToInt32(targets.Value.ToArray(), i * 4);
            int absoluteTarget = baseOffset + targetOffset;
            var labelName = $"instruction_{absoluteTarget:X2}";

            if (i == 0)
            {
                // Case 0: value is already in A, use BEQ (zero check)
                Emit(Opcode.BNE, AddressMode.Relative, 3); // skip JMP if not 0
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
            }
            else
            {
                Emit(Opcode.CMP, AddressMode.Immediate, (byte)i);
                Emit(Opcode.BNE, AddressMode.Relative, 3); // skip JMP if not match
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
            }
        }
        // Fall through = default (no match) — continues to next IL instruction
    }

    /// <summary>
    /// Allocates an absolute address for a user-defined static field.
    /// Static fields share the same $0325+ address space as locals.
    /// </summary>
    ushort GetOrAllocateStaticField(string fieldName)
    {
        if (_staticFieldAddresses.TryGetValue(fieldName, out var addr))
            return addr;
        addr = (ushort)(local + LocalCount);
        LocalCount += 1;
        _staticFieldAddresses[fieldName] = addr;
        return addr;
    }

    void HandleStsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        if (Stack.Count > 0) Stack.Pop();

        if (_runtimeValueInA)
        {
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        else if (_immediateInA != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)_immediateInA);
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        else
        {
            // Constant from WriteLdc — remove previous LDA, re-emit with STA
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        }
        _runtimeValueInA = false;
        _immediateInA = null;
    }

    void HandleLdsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        Emit(Opcode.LDA, AddressMode.Absolute, addr);
        _runtimeValueInA = true;
        _immediateInA = null;
        Stack.Push(0);
    }

    /// <summary>
    /// Gets the zero-page address and allocates storage for a struct local.
    /// </summary>
    ushort GetOrAllocateStructLocal(int localIndex, string structType)
    {
        if (Locals.TryGetValue(localIndex, out var existing) && existing.Address is not null)
            return (ushort)existing.Address;

        // Allocate struct on zero page
        int structSize = 0;
        if (StructLayouts.TryGetValue(structType, out var fields))
        {
            foreach (var f in fields)
                structSize += f.Size;
        }
        if (structSize == 0)
            structSize = 1;

        ushort addr = (ushort)(local + LocalCount);
        LocalCount += structSize;
        Locals[localIndex] = new Local(0, addr);
        _structLocalTypes[localIndex] = structType;
        return addr;
    }

    /// <summary>
    /// Gets the byte offset of a field within a struct type.
    /// </summary>
    int GetFieldOffset(string structType, string fieldName)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");

        int offset = 0;
        foreach (var f in fields)
        {
            if (f.Name == fieldName)
                return offset;
            offset += f.Size;
        }
        throw new InvalidOperationException($"Field '{fieldName}' not found in struct '{structType}'");
    }

    /// <summary>
    /// Gets the total size in bytes of a struct type.
    /// </summary>
    int GetStructSize(string structType)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");
        int size = 0;
        foreach (var f in fields)
            size += f.Size;
        return size > 0 ? size : 1;
    }

    /// <summary>
    /// Resolves the struct type for a local by checking _structLocalTypes or matching
    /// the field name against known struct layouts.
    /// </summary>
    string ResolveStructType(int localIndex, string fieldName)
    {
        if (_structLocalTypes.TryGetValue(localIndex, out var knownType))
            return knownType;

        // Search all struct layouts for a matching field name
        foreach (var kvp in StructLayouts)
        {
            foreach (var f in kvp.Value)
            {
                if (f.Name == fieldName)
                {
                    _structLocalTypes[localIndex] = kvp.Key;
                    return kvp.Key;
                }
            }
        }
        throw new InvalidOperationException($"Cannot resolve struct type for local {localIndex} with field '{fieldName}'");
    }

    /// <summary>
    /// Handles Array.Fill(array, value): fill entire array with a byte value.
    /// IL patterns:
    ///   1. ldloc (array), ldc (value), call Array.Fill  — array stored in local
    ///   2. newarr, [dup,] ldc (value), call Array.Fill  — array on eval stack (Roslyn Release)
    /// Emits inline 6502 fill loop: LDA #value; LDX #(size-1); loop: STA arr,X; DEX; BPL loop
    /// </summary>
    void HandleArrayFill()
    {
        // Pop value and array ref from stack
        int fillValue = Stack.Count > 0 ? Stack.Pop() : 0;
        int arrayRef = Stack.Count > 0 ? Stack.Pop() : 0;

        if (Instructions == null || Index < 2)
            throw new InvalidOperationException("Array.Fill requires at least 2 preceding instructions");

        // Try to find the array local by scanning for ldloc (stops at dup/newarr)
        int? arrayLocalIdx = null;
        for (int scan = Index - 2; scan >= 0; scan--)
        {
            var op = Instructions[scan].OpCode;
            if (op == ILOpCode.Dup || op == ILOpCode.Newarr)
                break; // eval stack pattern — no local
            var idx = GetLdlocIndex(Instructions[scan]);
            if (idx != null)
            {
                arrayLocalIdx = idx;
                break;
            }
        }

        ushort arrayAddr;
        int arraySize;

        if (arrayLocalIdx != null)
        {
            // Pattern 1: array is in a local
            var arrayLocal = Locals[arrayLocalIdx.Value];
            if (arrayLocal.Address == null || arrayLocal.ArraySize == 0)
                throw new InvalidOperationException("Array.Fill: array has no address or zero size");
            arraySize = arrayLocal.ArraySize;
            arrayAddr = (ushort)arrayLocal.Address;
        }
        else
        {
            // Pattern 2: array on eval stack from newarr — allocate now
            arraySize = arrayRef > 0 ? arrayRef : 1;
            arrayAddr = (ushort)(local + LocalCount);
            LocalCount += arraySize;
        }

        // Remove previously emitted instructions (WriteLdloc/WriteLdc or LDA from newarr size)
        // Find the first instruction of the Fill args sequence
        int firstArgILOffset = -1;
        for (int scan = Index - 1; scan >= 0; scan--)
        {
            var op = Instructions[scan].OpCode;
            if (op == ILOpCode.Newarr)
            {
                // Go one further back to include the ldc that pushed the array size
                firstArgILOffset = scan > 0 ? Instructions[scan - 1].Offset : Instructions[scan].Offset;
                break;
            }
            if (op == ILOpCode.Dup)
            {
                firstArgILOffset = Instructions[scan].Offset;
                break;
            }
            if (GetLdlocIndex(Instructions[scan]) != null)
            {
                firstArgILOffset = Instructions[scan].Offset;
                break;
            }
        }
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit fill loop: LDA #value; LDX #(size-1); @loop: STA arr,X; DEX; BPL @loop
        if (arraySize <= 256)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(fillValue & 0xFF));
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(arraySize - 1));
            // STA arr,X (3) + DEX (1) + BPL (-6 to STA)
            Emit(Opcode.STA, AddressMode.AbsoluteX, arrayAddr);
            Emit(Opcode.DEX, AddressMode.Implied);
            Emit(Opcode.BPL, AddressMode.Relative, unchecked((byte)-6));
        }

        _runtimeValueInA = false;
        _immediateInA = null;
    }

    /// <summary>
    /// Handles Array.Copy(src, dst, length): copy bytes between arrays.
    /// IL pattern: ldloc (src), ldloc (dst), ldc (length), call Copy
    /// Emits inline 6502 copy loop: LDX #0; @loop: LDA src,X; STA dst,X; INX; CPX #len; BNE @loop
    /// </summary>
    void HandleArrayCopy()
    {
        // Pop length, dst, src from stack
        int length = Stack.Count > 0 ? Stack.Pop() : 0;
        int dstRef = Stack.Count > 0 ? Stack.Pop() : 0;
        int srcRef = Stack.Count > 0 ? Stack.Pop() : 0;

        if (Instructions == null || Index < 3)
            throw new InvalidOperationException("Array.Copy requires at least 3 preceding instructions");

        // Find dst and src array locals by scanning back (stops at newarr for eval-stack pattern)
        // IL pattern: [ldloc(src) | newarr], ldloc(dst), ldc(len), call Array.Copy
        int? srcLocalIdx = null;
        int? dstLocalIdx = null;
        int firstArgILOffset = -1;

        for (int scan = Index - 2; scan >= 0; scan--)
        {
            var op = Instructions[scan].OpCode;

            // Check for ldloc
            var idx = GetLdlocIndex(Instructions[scan]);
            if (idx != null)
            {
                if (dstLocalIdx == null)
                    dstLocalIdx = idx;
                else
                {
                    srcLocalIdx = idx;
                    firstArgILOffset = Instructions[scan].Offset;
                    break;
                }
                continue;
            }

            // Check for newarr (eval-stack src pattern)
            if (op == ILOpCode.Newarr && dstLocalIdx != null)
            {
                // src is on eval stack from newarr — go one back to include ldc for size
                firstArgILOffset = scan > 0 ? Instructions[scan - 1].Offset : Instructions[scan].Offset;
                break;
            }
        }

        if (dstLocalIdx == null)
            throw new InvalidOperationException("Array.Copy: could not find dst array local");

        var dstLocal = Locals[dstLocalIdx.Value];
        if (dstLocal.Address == null && dstLocal.LabelName == null)
            throw new InvalidOperationException("Array.Copy: dst has no address");

        ushort? dstAddr = dstLocal.Address != null ? (ushort)dstLocal.Address : null;

        // Resolve src address
        ushort srcAddr;
        if (srcLocalIdx != null)
        {
            var srcLocal = Locals[srcLocalIdx.Value];
            if (srcLocal.Address == null)
                throw new InvalidOperationException("Array.Copy: src has no address");
            srcAddr = (ushort)srcLocal.Address;
        }
        else
        {
            // Eval-stack src: allocate now
            int srcSize = srcRef > 0 ? srcRef : (length > 0 ? length : 1);
            srcAddr = (ushort)(local + LocalCount);
            LocalCount += srcSize;
        }

        // Determine copy length
        int copyLen = length > 0 ? length : 1;
        if (copyLen == 0)
            throw new InvalidOperationException("Array.Copy: zero length");

        // Remove previously emitted instructions
        if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCount;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Emit copy loop: LDX #0; @loop: LDA src,X; STA dst,X; INX; CPX #len; BNE @loop
        if (copyLen <= 256)
        {
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            if (dstAddr != null)
            {
                Emit(Opcode.LDA, AddressMode.AbsoluteX, srcAddr);
                Emit(Opcode.STA, AddressMode.AbsoluteX, dstAddr.Value);
            }
            else if (dstLocal.LabelName != null)
            {
                Emit(Opcode.LDA, AddressMode.AbsoluteX, srcAddr);
                EmitWithLabel(Opcode.STA, AddressMode.AbsoluteX, dstLocal.LabelName);
            }
            Emit(Opcode.INX, AddressMode.Implied);
            Emit(Opcode.CPX, AddressMode.Immediate, (byte)copyLen);
            // LDA (3) + STA (3) + INX (1) + CPX (2) + BNE (2) = 11; target is LDA at -11 from after BNE
            Emit(Opcode.BNE, AddressMode.Relative, unchecked((byte)-11));
        }

        _runtimeValueInA = false;
        _immediateInA = null;
    }

    /// <summary>
    /// Handles ldelema: load the address of a struct array element.
    /// IL pattern: ldloc_N (array), ldc_i4 (index), ldelema TypeName
    /// Sets pending state for the subsequent stfld/ldfld.
    /// </summary>
    void HandleLdelema(ILInstruction instruction)
    {
        string? structType = instruction.String;
        if (structType == null || !StructLayouts.ContainsKey(structType))
            throw new NotImplementedException($"ldelema for non-struct type '{structType}' is not supported");

        int structSize = GetStructSize(structType);

        // Pop index from IL stack
        int index = Stack.Count > 0 ? Stack.Pop() : 0;
        // Pop array ref from IL stack
        if (Stack.Count > 0) Stack.Pop();

        if (Instructions == null || Index < 2)
            throw new InvalidOperationException("ldelema requires at least 2 preceding instructions");

        // The previous instruction loaded the index, the one before that loaded the array
        var indexInstr = Instructions[Index - 1];
        var arrayInstr = Instructions[Index - 2];

        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
        ushort arrayBase;
        
        if (arrayLocalIdx != null)
        {
            // Array loaded from local variable
            var arrayLocal = Locals[arrayLocalIdx.Value];
            if (arrayLocal.Address == null)
                throw new InvalidOperationException($"ldelema: array local {arrayLocalIdx} has no address");
            arrayBase = (ushort)arrayLocal.Address;
        }
        else if (_pendingStructArrayBase != null)
        {
            // Array reference is on the evaluation stack (from newarr, dup, or carried through stfld)
            arrayBase = _pendingStructArrayBase.Value;
        }
        else
        {
            throw new InvalidOperationException(
                $"ldelema: no array local or pending struct array base (Index={Index}, Index-2={arrayInstr.OpCode})");
        }

        // Remove the LDA instructions emitted by WriteLdloc (array) and WriteLdc (index)
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Check if the index is a constant or a variable
        bool isConstantIndex = GetLdcValue(indexInstr) != null;

        if (!isConstantIndex)
        {
            // Variable index: the index was loaded by a ldloc
            int? indexLocalIdx = GetLdlocIndex(indexInstr);
            if (indexLocalIdx != null && Locals.TryGetValue(indexLocalIdx.Value, out var indexLocal) && indexLocal.Address != null)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)indexLocal.Address);
                EmitMultiplyA(structSize);
                Emit(Opcode.TAX, AddressMode.Implied);

                _pendingStructArrayRuntimeIndex = true;
                _structArrayBaseForRuntimeIndex = arrayBase;
                _pendingStructElementType = structType;
                _pendingStructElementBase = null;
            }
            else
            {
                throw new NotImplementedException("ldelema: variable index without tracked local");
            }
        }
        else
        {
            // Constant index: compute element base at compile time
            ushort elementBase = (ushort)(arrayBase + index * structSize);
            _pendingStructElementBase = elementBase;
            _pendingStructElementType = structType;
            _pendingStructArrayRuntimeIndex = false;
        }

        _runtimeValueInA = false;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Emits 6502 code to multiply A by a constant factor using shifts and adds.
    /// A must contain the value to multiply; result is left in A.
    /// </summary>
    void EmitMultiplyA(int factor)
    {
        if (factor == 1) return;
        if (factor == 2)
        {
            Emit(Opcode.ASL, AddressMode.Accumulator);
            return;
        }
        if (factor == 4)
        {
            Emit(Opcode.ASL, AddressMode.Accumulator);
            Emit(Opcode.ASL, AddressMode.Accumulator);
            return;
        }
        if (factor == 8)
        {
            Emit(Opcode.ASL, AddressMode.Accumulator);
            Emit(Opcode.ASL, AddressMode.Accumulator);
            Emit(Opcode.ASL, AddressMode.Accumulator);
            return;
        }
        // For non-power-of-2, use shift+add (e.g., *3 = *2 + *1, *6 = *2 * 3)
        // Store A in TEMP, use shifts and add back
        if ((factor & (factor - 1)) != 0)
        {
            // General case: decompose into shifts and adds
            // For small struct sizes (common: 2-8 bytes), this covers most cases
            Emit(Opcode.STA, AddressMode.ZeroPage, 0x17); // TEMP
            int remaining = factor;
            bool first = true;
            for (int bit = 0; remaining > 0; bit++)
            {
                if ((remaining & 1) != 0)
                {
                    if (first)
                    {
                        Emit(Opcode.LDA, AddressMode.ZeroPage, 0x17); // start with original
                        for (int s = 0; s < bit; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        first = false;
                    }
                    else
                    {
                        Emit(Opcode.STA, AddressMode.ZeroPage, 0x18); // save partial to TEMP+1
                        Emit(Opcode.LDA, AddressMode.ZeroPage, 0x17);
                        for (int s = 0; s < bit; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        Emit(Opcode.CLC, AddressMode.Implied);
                        Emit(Opcode.ADC, AddressMode.ZeroPage, 0x18);
                    }
                }
                remaining >>= 1;
            }
        }
        else
        {
            // Pure power of 2
            while (factor > 1)
            {
                Emit(Opcode.ASL, AddressMode.Accumulator);
                factor >>= 1;
            }
        }
    }

    /// <summary>
    /// Handles stfld: store a value to a struct field on zero page.
    /// IL pattern: ldloca.s N, ldc.i4 value, stfld fieldName
    /// </summary>
    void HandleStfld(string fieldName)
    {
        // Check for struct array element access (from ldelema)
        if (_pendingStructElementType != null)
        {
            string structType = _pendingStructElementType;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            // The value to store was pushed by ldc before stfld
            int value = Stack.Count > 0 ? Stack.Pop() : 0;

            if (_pendingStructArrayRuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                if (_runtimeValueInA)
                {
                    Emit(Opcode.STA, AddressMode.AbsoluteX, fieldAddr);
                    _runtimeValueInA = false;
                }
                else
                {
                    RemoveLastInstructions(1);
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                    Emit(Opcode.STA, AddressMode.AbsoluteX, fieldAddr);
                }
            }
            else
            {
                // Constant index: _pendingStructElementBase has the element base
                ushort fieldAddr = (ushort)(_pendingStructElementBase!.Value + fieldOffset);
                if (_runtimeValueInA)
                {
                    Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
                    _runtimeValueInA = false;
                }
                else
                {
                    RemoveLastInstructions(1);
                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                    Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
                }
            }

            _pendingStructElementType = null;
            _pendingStructElementBase = null;
            _pendingStructArrayRuntimeIndex = false;
            _immediateInA = null;
            return;
        }

        if (_pendingStructLocal is null)
            throw new InvalidOperationException($"stfld '{fieldName}' without preceding ldloca.s or ldelema");

        {
            int localIndex = _pendingStructLocal.Value;
            _pendingStructLocal = null;

            string structType = ResolveStructType(localIndex, fieldName);
            ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
            int fieldOffset = GetFieldOffset(structType, fieldName);
            ushort fieldAddr = (ushort)(baseAddr + fieldOffset);

        // The value to store was pushed by ldc before stfld
        int value = Stack.Count > 0 ? Stack.Pop() : 0;

        if (_runtimeValueInA)
        {
            // A has the runtime value — just store it
            Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
            _runtimeValueInA = false;
        }
        else
        {
            // Remove the LDA #imm that WriteLdc emitted
            RemoveLastInstructions(1);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
            Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
        }
        _immediateInA = null;
        }
    }

    /// <summary>
    /// Handles ldfld: load a value from a struct field on zero page.
    /// IL patterns: (ldloca.s N | ldloc.N), ldfld fieldName
    /// </summary>
    void HandleLdfld(string fieldName)
    {
        // Check for struct array element access (from ldelema)
        if (_pendingStructElementType != null)
        {
            string structType = _pendingStructElementType;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            if (_pendingStructArrayRuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, fieldAddr);
            }
            else
            {
                // Constant index: _pendingStructElementBase has the element base
                ushort fieldAddr = (ushort)(_pendingStructElementBase!.Value + fieldOffset);
                Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            }

            _pendingStructElementType = null;
            _pendingStructElementBase = null;
            _pendingStructArrayRuntimeIndex = false;
            _runtimeValueInA = true;
            _immediateInA = null;
            Stack.Push(0);
            return;
        }

        {
            int localIndex;
            if (_pendingStructLocal is not null)
            {
                localIndex = _pendingStructLocal.Value;
                _pendingStructLocal = null;
            }
            else if (_lastLoadedLocalIndex is not null && _lastLoadedLocalIndex >= 0 && _structLocalTypes.ContainsKey(_lastLoadedLocalIndex.Value))
            {
                // ldloc loaded the struct value — undo the WriteLdloc LDA and use field access instead
                localIndex = _lastLoadedLocalIndex.Value;
                RemoveLastInstructions(1);
                if (Stack.Count > 0) Stack.Pop(); // Remove the value WriteLdloc pushed
            }
            else
            {
                throw new InvalidOperationException($"ldfld '{fieldName}' without preceding ldloca.s or struct ldloc");
            }

            string structType = ResolveStructType(localIndex, fieldName);
            ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
            int fieldOffset = GetFieldOffset(structType, fieldName);
            ushort fieldAddr = (ushort)(baseAddr + fieldOffset);

            Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            _runtimeValueInA = true;
            _immediateInA = null;

            // Push the field value onto the IL stack (unknown at compile time)
            Stack.Push(0);
        }
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
                ushort addr = (ushort)loadedLocal.Address.Value;

                if (loadedLocal.IsWord)
                {
                    // 16-bit local: remove LDA $lo, LDX $hi, JSR pushax, LDA #1 (4 instructions)
                    RemoveLastInstructions(4);
                    if (isAdd)
                    {
                        // INC lo; BNE +3; INC hi
                        Emit(Opcode.INC, AddressMode.Absolute, addr);
                        Emit(Opcode.BNE, AddressMode.Relative, 0x03);
                        Emit(Opcode.INC, AddressMode.Absolute, (ushort)(addr + 1));
                    }
                    else
                    {
                        // LDA lo; BNE +3; DEC hi; DEC lo
                        Emit(Opcode.LDA, AddressMode.Absolute, addr);
                        Emit(Opcode.BNE, AddressMode.Relative, 0x03);
                        Emit(Opcode.DEC, AddressMode.Absolute, (ushort)(addr + 1));
                        Emit(Opcode.DEC, AddressMode.Absolute, addr);
                    }
                }
                else
                {
                    // 8-bit local: remove LDA $addr + LDA #1 (2 instructions)
                    RemoveLastInstructions(2);
                    if (isAdd)
                    {
                        Emit(Opcode.INC, AddressMode.Absolute, addr);
                    }
                    else
                    {
                        Emit(Opcode.DEC, AddressMode.Absolute, addr);
                    }
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
        
        // 16-bit arithmetic: ushort +/- constant
        if (_ushortInAX)
        {
            byte lo = (byte)(operand & 0xFF);
            byte hi = (byte)((operand >> 8) & 0xFF);
            if (isAdd)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, lo);
                if (hi != 0)
                {
                    // Full 16-bit add: also add hi byte to X via TEMP
                    Emit(Opcode.STA, AddressMode.ZeroPage, 0x17);
                    Emit(Opcode.TXA, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, hi);
                    Emit(Opcode.TAX, AddressMode.Implied);
                    Emit(Opcode.LDA, AddressMode.ZeroPage, 0x17);
                }
                else
                {
                    Emit(Opcode.BCC, AddressMode.Relative, 1);
                    Emit(Opcode.INX, AddressMode.Implied);
                }
            }
            else
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Immediate, lo);
                if (hi != 0)
                {
                    Emit(Opcode.STA, AddressMode.ZeroPage, 0x17);
                    Emit(Opcode.TXA, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.Immediate, hi);
                    Emit(Opcode.TAX, AddressMode.Implied);
                    Emit(Opcode.LDA, AddressMode.ZeroPage, 0x17);
                }
                else
                {
                    Emit(Opcode.BCS, AddressMode.Relative, 1);
                    Emit(Opcode.DEX, AddressMode.Implied);
                }
            }
            Stack.Push(0);
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = true;
            return;
        }

        // Byte in A + ushort constant: A has byte, operand > 255
        // Produces 16-bit result in A:X
        if (operand > byte.MaxValue && (LastLDA || _runtimeValueInA || (_lastLoadedLocalIndex.HasValue &&
            Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var localForUshort) && localForUshort.Address.HasValue)))
        {
            byte lo = (byte)(operand & 0xFF);
            byte hi = (byte)((operand >> 8) & 0xFF);
            if (isAdd)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, lo);
                Emit(Opcode.LDX, AddressMode.Immediate, hi);
                Emit(Opcode.BCC, AddressMode.Relative, 1);
                Emit(Opcode.INX, AddressMode.Implied);
            }
            else
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Immediate, lo);
                Emit(Opcode.LDX, AddressMode.Immediate, hi);
                Emit(Opcode.BCS, AddressMode.Relative, 1);
                Emit(Opcode.DEX, AddressMode.Implied);
            }
            _ushortInAX = true;
            Stack.Push(0);
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = true;
            return;
        }

        // Check if the value came from a local variable load (runtime value)
        // Same class of bug as AND: ldloc emits LDA $addr but doesn't set _runtimeValueInA
        Local? lastLocal = null;
        bool localInA = _lastLoadedLocalIndex.HasValue &&
            Locals.TryGetValue(_lastLoadedLocalIndex.Value, out lastLocal) && lastLocal.Address.HasValue;

        // Default: if we have runtime values, emit actual arithmetic
        if (_runtimeValueInA || localInA)
        {
            // Detect whether the local was loaded SECOND (ldc then ldloc pattern)
            // In that case, after removing 2 instructions, A has the constant,
            // and we need to use the local's address for arithmetic (not its compile-time value)
            bool useLocalAddress = false;
            ushort localAddr = 0;
            if (localInA && !_runtimeValueInA)
            {
                var block = CurrentBlock!;
                var lastInstr = block[block.Count - 1];
                useLocalAddress = lastInstr.Mode == AddressMode.Absolute && lastInstr.Opcode == Opcode.LDA;
                if (useLocalAddress)
                {
                    localAddr = (ushort)lastLocal!.Address!.Value;
                    // Remove pusha + ldloc's LDA — A retains constant from ldc
                    RemoveLastInstructions(2);
                }
                else
                {
                    // Local was loaded FIRST (ldloc then ldc). Only remove ldc's LDA.
                    // A retains local's value from ldloc's LDA which stays in the block.
                    RemoveLastInstructions(1);
                }
            }
            if (isAdd)
            {
                if (_savedRuntimeToTemp)
                {
                    // Two runtime values: first in TEMP, second in A
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                }
                else if (useLocalAddress)
                {
                    // constant + runtime_local: A = constant, add local's runtime value
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Absolute, localAddr);
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
                else if (useLocalAddress)
                {
                    // constant - runtime_local: A = constant, subtract local's runtime value
                    Emit(Opcode.SEC, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.Absolute, localAddr);
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
            _runtimeValueInA = true; // Result of arithmetic is a runtime value
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

    static int? GetLdcValue(ILInstruction instr) => instr.OpCode switch
    {
        ILOpCode.Ldc_i4_m1 => -1,
        ILOpCode.Ldc_i4_0 => 0,
        ILOpCode.Ldc_i4_1 => 1,
        ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldc_i4_3 => 3,
        ILOpCode.Ldc_i4_4 => 4,
        ILOpCode.Ldc_i4_5 => 5,
        ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7,
        ILOpCode.Ldc_i4_8 => 8,
        ILOpCode.Ldc_i4_s => instr.Integer,
        ILOpCode.Ldc_i4 => instr.Integer,
        _ => null
    };

    /// <summary>
    /// Handles stloc after newarr: allocates a runtime array (byte[] or struct[]).
    /// For struct arrays, allocates count * structSize bytes and records the struct type.
    /// </summary>
    void HandleStlocAfterNewarr(int localIdx)
    {
        bool isStructArray = _pendingArrayType != null && StructLayouts.ContainsKey(_pendingArrayType);
        if (isStructArray)
        {
            int count = _pendingStructArrayCount;
            int structSize = GetStructSize(_pendingArrayType!);
            int totalBytes = count * structSize;
            // Use the pre-allocated base address from newarr
            ushort arrayAddr = _pendingStructArrayBase ?? (ushort)(local + LocalCount);
            if (_pendingStructArrayBase == null)
                LocalCount += totalBytes; // fallback: allocate now if not pre-allocated
            Locals[localIdx] = new Local(count, arrayAddr, ArraySize: totalBytes, StructArrayType: _pendingArrayType);
            _pendingStructArrayCount = 0;
            _pendingStructArrayBase = null;
        }
        else
        {
            int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
            ushort arrayAddr = (ushort)(local + LocalCount);
            LocalCount += arraySize;
            Locals[localIdx] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
        }
    }

    void WriteStloc(Local local)
    {
        if (local.Address is null)
            throw new ArgumentNullException(nameof(local.Address));

        // Storing a local clobbers A/X
        _firstAndAfterPadPoll = false;
        _ushortInAX = false;

        if (_ntadrRuntimeResult)
        {
            // NTADR result is in TEMP ($17 = hi) and TEMP2 ($19 = lo)
            // Store both bytes to the ushort local
            LocalCount += 2;
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)(local.Address + 1));
            _ntadrRuntimeResult = false;
            _runtimeValueInA = false;
            _immediateInA = null;
        }
        else if (_runtimeValueInA)
        {
            if (local.IsWord)
            {
                // A=lo, X=hi from a ushort-returning function — store both bytes
                LocalCount += 2;
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.STX, AddressMode.Absolute, (ushort)(local.Address + 1));
            }
            else
            {
                // A has the runtime value — just store it
                LocalCount += 1;
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            }
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            _immediateInA = null;
        }
        else if (local.IsWord)
        {
            LocalCount += 2;
            // Word local (e.g. ushort x = 0): store low byte in A, high byte = 0
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)(local.Address + 1));
            _immediateInA = 0x00;
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
                // Store scalar local: remove previous LDA, re-emit with STA
                RemoveLastInstructions(1);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                if (_lastByteArrayLabel != null)
                {
                    // Byte array address needs to be in A:X for upcoming call — use label resolution
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, _lastByteArrayLabel);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, _lastByteArrayLabel);
                    _byteArrayAddressEmitted = true;
                }
                _immediateInA = null;
            }
        }
        else if (local.Value < ushort.MaxValue)
        {
            LocalCount += 2;
            // Remove the previous LDX + LDA instructions (2 instructions = 4 bytes)
            RemoveLastInstructions(2);
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(local.Value >> 8));
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(local.Value & 0xFF));
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.STX, AddressMode.Absolute, (ushort)(local.Address + 1));
            _immediateInA = null;
        }
        else
        {
            throw new NotImplementedException($"{nameof(WriteStloc)} not implemented for value larger than ushort: {local.Value}");
        }
    }

    void WriteLdc(ushort operand)
    {
        // Check if next instruction can handle the constant directly with A's current value
        bool nextIsAddSub = Instructions is not null && Index + 1 < Instructions.Length &&
            Instructions[Index + 1].OpCode is ILOpCode.Add or ILOpCode.Sub;
        if (nextIsAddSub && LastLDA)
        {
            // Keep current A value — the Add/Sub handler will do 16-bit add inline
            Stack.Push(operand);
            return;
        }

        if (LastLDA)
        {
            EmitJSR("pusha");
        }
        Emit(Opcode.LDX, AddressMode.Immediate, checked((byte)(operand >> 8)));
        Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)(operand & 0xff)));
        _ushortInAX = true;
        Stack.Push(operand);
    }

    void WriteLdc(byte operand)
    {
        if (_ushortInAX)
        {
            // Check if the next instruction can handle A:X directly
            bool nextIsShift = Instructions is not null && Index + 1 < Instructions.Length &&
                Instructions[Index + 1].OpCode is ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Shl;
            bool nextIsAddSub = _runtimeValueInA && Instructions is not null && Index + 1 < Instructions.Length &&
                Instructions[Index + 1].OpCode is ILOpCode.Add or ILOpCode.Sub;
            if (nextIsShift || nextIsAddSub)
            {
                // Keep A:X intact — the operator will handle the 16-bit value
                Stack.Push(operand);
                return;
            }
            // A:X holds a 16-bit value — push both bytes via pushax
            EmitJSR("pushax");
            _ushortInAX = false;
            _immediateInA = null;
        }
        else if (LastLDA)
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
        _ushortInAX = false;
        if (local.LabelName is not null)
        {
            // This local holds a byte array label reference
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, local.LabelName);
            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, local.LabelName);
            EmitJSR("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)local.Value); // Size of array
            _immediateInA = (byte)local.Value;
            _ldlocByteArrayLabel = local.LabelName;
        }
        else if (local.Address is not null)
        {
            // This is actually a local variable
            if (local.IsWord)
            {
                if (LastLDA)
                {
                    EmitJSR("pusha");
                }
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(local.Address + 1));
                _immediateInA = null;
                _ushortInAX = true;
            }
            else if (local.Value < byte.MaxValue)
            {
                if (_runtimeValueInA && !LastLDA)
                {
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                    _savedRuntimeToTemp = true;
                }
                else if (LastLDA)
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
                _ushortInAX = true;
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
    /// Emits 6502 code to load a function parameter from the cc65 software stack.
    /// Parameters are pushed left-to-right by the caller, with the last arg in A (pushed
    /// by the method prologue). Stack layout: arg0 at offset (N-1), arg(N-1) at offset 0.
    /// </summary>
    void WriteLdarg(int argIndex)
    {
        if (MethodParamCount == 0)
            throw new InvalidOperationException("ldarg used but MethodParamCount is 0");

        if (_runtimeValueInA && !LastLDA)
        {
            // Save return value to TEMP (not pusha — would shift arg offsets)
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
            _savedRuntimeToTemp = true;
        }
        else if (LastLDA)
        {
            EmitJSR("pusha");
            _argStackAdjust++;
        }

        // cc65 stack offset: first arg (index 0) is deepest, adjusted for extra pushes
        byte offset = checked((byte)(MethodParamCount - 1 - argIndex + _argStackAdjust));
        Emit(Opcode.LDY, AddressMode.Immediate, offset);
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp);
        _immediateInA = null;
        _runtimeValueInA = true;
        Stack.Push(0); // placeholder for runtime value
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
        // Walk back through IL to find the 5 argument-producing IL instructions.
        // Args can be: constants, locals, or array elements (ldloc_arr + ldloc_idx + ldelem_u1).
        var argInfos = new List<(bool isLocal, int localIndex, int constValue, bool hasAdd, int addValue, bool isArrayElem, int arrayLocIdx, int indexLocIdx)>();
        int ilIdx = Index - 1;
        int needed = 5;
        int firstArgIlIdx = -1;
        int? pendingAddValue = null;

        while (needed > 0 && ilIdx >= 0)
        {
            var il = Instructions[ilIdx];
            switch (il.OpCode)
            {
                case ILOpCode.Ldelem_u1:
                {
                    // Array element: walk back to find index + array locals
                    ilIdx--;
                    int idxLoc = -1, arrLoc = -1;
                    if (ilIdx >= 0) { idxLoc = GetLdlocIndex(Instructions[ilIdx]) ?? -1; ilIdx--; }
                    if (ilIdx >= 0) { arrLoc = GetLdlocIndex(Instructions[ilIdx]) ?? -1; }
                    argInfos.Add((false, 0, 0, false, 0, true, arrLoc, idxLoc));
                    needed--;
                    firstArgIlIdx = ilIdx;
                    break;
                }
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                {
                    int locIdx = il.OpCode - ILOpCode.Ldloc_0;
                    if (pendingAddValue != null)
                    {
                        argInfos.Add((true, locIdx, 0, true, pendingAddValue.Value, false, 0, 0));
                        pendingAddValue = null;
                    }
                    else
                    {
                        argInfos.Add((true, locIdx, 0, false, 0, false, 0, 0));
                    }
                    needed--;
                    firstArgIlIdx = ilIdx;
                    break;
                }
                case ILOpCode.Ldloc_s:
                {
                    int locIdx = il.Integer ?? 0;
                    if (pendingAddValue != null)
                    {
                        argInfos.Add((true, locIdx, 0, true, pendingAddValue.Value, false, 0, 0));
                        pendingAddValue = null;
                    }
                    else
                    {
                        argInfos.Add((true, locIdx, 0, false, 0, false, 0, 0));
                    }
                    needed--;
                    firstArgIlIdx = ilIdx;
                    break;
                }
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
                        argInfos.Add((false, 0, val, false, 0, false, 0, 0));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                {
                    int val = il.OpCode == ILOpCode.Ldc_i4_m1 ? -1 : (il.Integer ?? 0);
                    if (IsConsumedByAdd(ilIdx))
                    {
                        pendingAddValue = val;
                    }
                    else
                    {
                        argInfos.Add((false, 0, val, false, 0, false, 0, 0));
                        needed--;
                        firstArgIlIdx = ilIdx;
                    }
                    break;
                }
                case ILOpCode.Add: case ILOpCode.Sub:
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2:
                case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
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

        // Emit: JSR decsp4
        EmitJSR("decsp4");

        // First 4 args go to stack via STA ($22),Y
        for (int i = 0; i < 4 && i < argInfos.Count; i++)
        {
            var arg = argInfos[i];
            if (arg.isArrayElem)
            {
                // Array element: LDX index, LDA array,X
                var idxLocal = Locals[arg.indexLocIdx];
                var arrLocal = Locals[arg.arrayLocIdx];
                if (idxLocal.Address != null)
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idxLocal.Address);
                if (arrLocal.Address != null)
                    Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)arrLocal.Address);
            }
            else if (arg.isLocal)
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

        // 5th arg (id) stays in A
        if (argInfos.Count >= 5)
        {
            var idArg = argInfos[4];
            if (idArg.isLocal)
            {
                var loc = Locals[idArg.localIndex];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc.Address!);
            }
            else if (idArg.isArrayElem)
            {
                var idxLocal = Locals[idArg.indexLocIdx];
                var arrLocal = Locals[idArg.arrayLocIdx];
                if (idxLocal.Address != null)
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idxLocal.Address);
                if (arrLocal.Address != null)
                    Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)arrLocal.Address);
            }
            else
            {
                byte idVal = checked((byte)idArg.constValue);
                // Check if A already has the right value from the 4th arg STA
                if (argInfos.Count >= 4)
                {
                    var attrArg = argInfos[3];
                    if (!attrArg.isLocal && !attrArg.isArrayElem && !idArg.isLocal
                        && attrArg.constValue == idArg.constValue)
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
    /// Emits oam_meta_spr_pal call with proper argument setup.
    /// IL pattern: ldloc(arr_x), ldloc_s(i), ldelem_u1, ldloc(arr_y), ldloc_s(i), ldelem_u1,
    ///             ldloc_s(pal), ldloc(metasprite), call oam_meta_spr_pal
    /// 
    /// Sets up: TEMP = x, TEMP2 = y, TEMP3 = pal, PTR = data pointer
    /// Uses OAM_OFF zero-page global for OAM buffer offset.
    /// </summary>
    void EmitOamMetaSprPal()
    {
        if (Instructions is null)
            throw new InvalidOperationException("EmitOamMetaSprPal requires Instructions");

        int? xArrayIdx = null, yArrayIdx = null, indexIdx = null, palIdx = null;
        string? dataLabel = null;
        int firstArgILOffset = -1;

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

        // Find pal source
        if (scan >= 0)
        {
            var palInstr = Instructions[scan];
            var palLocIdx = GetLdlocIndex(palInstr);
            if (palLocIdx != null && Locals.TryGetValue(palLocIdx.Value, out var palLocal) && palLocal.Address != null)
            {
                palIdx = palLocIdx.Value;
                scan--;
            }
        }

        // Find y source (ldelem_u1 preceded by ldloc(arr) + ldloc(idx))
        if (scan >= 0 && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
        {
            scan--;
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
            scan--;
            scan--;
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
            {
                RemoveLastInstructions(instrToRemove);
            }
        }

        // 1. Load x coordinate into TEMP
        if (xArrayIdx != null && indexIdx != null)
        {
            var xArr = Locals[xArrayIdx.Value];
            var idx = Locals[indexIdx.Value];
            if (idx.Address != null)
            {
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)idx.Address);
            }
            if (xArr.ArraySize > 0 && xArr.Address != null)
            {
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)xArr.Address);
            }
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
        }

        // 2. Load y coordinate into TEMP2
        if (yArrayIdx != null && indexIdx != null)
        {
            var yArr = Locals[yArrayIdx.Value];
            if (yArr.ArraySize > 0 && yArr.Address != null)
            {
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)yArr.Address);
            }
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
        }

        // 3. Load palette into TEMP3
        if (palIdx != null)
        {
            var palLocal = Locals[palIdx.Value];
            if (palLocal.Address != null)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)palLocal.Address);
            }
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP3);
        }

        // 4. Load data pointer into PTR
        if (dataLabel != null)
        {
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.ptr1);
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_HighByte, dataLabel);
            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(NESConstants.ptr1 + 1));
        }

        // 5. Call oam_meta_spr_pal (uses OAM_OFF global, no sprid param)
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.oam_meta_spr_pal));
        _immediateInA = null;
        _runtimeValueInA = false; // void return
    }
    /// <summary>
    /// Checks if the value loaded at instruction index <paramref name="idx"/> is consumed
    /// by an Add or Sub operation (making it part of a compound expression like Ldloc + Ldc + Add).
    /// </summary>
    bool IsConsumedByAdd(int idx)
    {
        if (Instructions is null) return false;
        for (int scan = idx + 1; scan < Index; scan++)
        {
            var op = Instructions[scan].OpCode;
            if (op == ILOpCode.Add || op == ILOpCode.Sub)
                return true;
            if (op is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1 or ILOpCode.Conv_i2)
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

        // Pop index and array reference from the evaluation stack; their values are
        // not needed here because we re-derive them from the preceding IL instructions.
        if (Stack.Count > 0) Stack.Pop(); // index value
        if (Stack.Count > 0) Stack.Pop(); // array ref

        // Find the two Ldloc instructions that loaded array and index
        var indexInstr = Instructions[Index - 1];
        var arrayInstr = Instructions[Index - 2];

        int? indexLocalIdx = GetLdlocIndex(indexInstr);
        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);

        // Handle constant index (Ldc_i4_* or Ldc_i4_s)
        int? constantIndex = null;
        if (indexLocalIdx == null)
        {
            constantIndex = indexInstr.OpCode switch
            {
                ILOpCode.Ldc_i4_0 => 0,
                ILOpCode.Ldc_i4_1 => 1,
                ILOpCode.Ldc_i4_2 => 2,
                ILOpCode.Ldc_i4_3 => 3,
                ILOpCode.Ldc_i4_4 => 4,
                ILOpCode.Ldc_i4_5 => 5,
                ILOpCode.Ldc_i4_6 => 6,
                ILOpCode.Ldc_i4_7 => 7,
                ILOpCode.Ldc_i4_8 => 8,
                ILOpCode.Ldc_i4_s => indexInstr.Integer,
                ILOpCode.Ldc_i4 => indexInstr.Integer,
                _ => null
            };
        }

        if (indexLocalIdx == null && constantIndex == null)
            throw new NotImplementedException("Ldelem_u1 only supports Ldloc or Ldc_i4 patterns for index");
        if (arrayLocalIdx == null)
            throw new NotImplementedException("Ldelem_u1 only supports Ldloc patterns for array");

        Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;
        var arrayLocal = Locals[arrayLocalIdx.Value];

        // Remove the previously emitted instructions from WriteLdloc/WriteLdc calls
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
                RemoveLastInstructions(instrToRemove);
        }

        // Check if there's a preceding value in the block that will be clobbered by the
        // upcoming LDA. This happens in patterns like: ldloc rh; ldloc arr; ldloc idx; ldelem.u1; sub
        // After removing the array/index instructions, the block still has LDA $rh_addr.
        // We save it to TEMP so the arithmetic handler can use it.
        int blockCountAfterRemove = GetBufferedBlockCount();
        if (blockCountAfterRemove > 0)
        {
            var block = CurrentBlock!;
            var lastInstr = block[blockCountAfterRemove - 1];
            if (lastInstr.Opcode == Opcode.LDA &&
                (lastInstr.Mode == AddressMode.Absolute || lastInstr.Mode == AddressMode.Immediate
                 || lastInstr.Mode == AddressMode.ZeroPage))
            {
                // Check if the next IL instruction is arithmetic (add/sub)
                bool nextIsArithmetic = Instructions is not null && Index + 1 < Instructions.Length &&
                    Instructions[Index + 1].OpCode is ILOpCode.Add or ILOpCode.Sub;
                // Also check for conv.u1/conv.u2 followed by arithmetic
                if (!nextIsArithmetic && Instructions is not null && Index + 1 < Instructions.Length)
                {
                    var nextOp = Instructions[Index + 1].OpCode;
                    if (nextOp is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1 && Index + 2 < Instructions.Length)
                        nextIsArithmetic = Instructions[Index + 2].OpCode is ILOpCode.Add or ILOpCode.Sub;
                }
                if (nextIsArithmetic)
                {
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                    _savedRuntimeToTemp = true;
                }
            }
        }

        // Emit: LDX index; LDA array_base,X
        if (constantIndex != null)
        {
            if (constantIndex.Value == 0 && arrayLocal.Address is not null)
            {
                // Constant index 0: just LDA array_base (no X needed)
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)arrayLocal.Address);
                Stack.Push(0);
                _immediateInA = null;
                _lastLoadedLocalIndex = null;
                _runtimeValueInA = true;
                return;
            }
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)constantIndex.Value);
        }
        else if (indexLocal?.Address is not null)
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
        int constantIndex = -1; // For constant-index stores like actor_dx[0] = 254
        int targetArrayILOffset = -1;

        // Collect the value expression info

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
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                    push = 1; break;
                case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.Mul: case ILOpCode.Div: case ILOpCode.Rem:
                case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Xor:
                case ILOpCode.Shr: case ILOpCode.Shr_un: case ILOpCode.Shl:
                    pop = 2; push = 1; break;
                case ILOpCode.Ldelem_u1:
                    pop = 2; push = 1; break;
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2: case ILOpCode.Conv_u4: case ILOpCode.Conv_u8:
                case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
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
                    
                    // The next instruction should be the index (ldloc or constant)
                    if (i + 1 < Index)
                    {
                        var nextIl = Instructions[i + 1];
                        var nextLocIdx = GetLdlocIndex(nextIl);
                        if (nextLocIdx != null)
                            targetIndexLocalIdx = nextLocIdx.Value;
                        else
                        {
                            // Check for constant index (ldc_i4_0, ldc_i4_1, etc.)
                            int? constIdx = GetLdcValue(nextIl);
                            if (constIdx != null)
                                constantIndex = constIdx.Value;
                        }
                    }
                    
                    valueStart = i + 2; // Value expression starts after arr + idx
                }
                break;
            }
        }

        if (targetArrayLocalIdx < 0)
        {
            // Can't identify target array at all
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            _lastLoadedLocalIndex = null;
            return;
        }

        // Handle expression-based index: buf[expr] = constValue
        // Pattern: call+and, call, etc. as index expression
        if (targetIndexLocalIdx < 0 && constantIndex < 0)
        {
            var targetArray2 = Locals[targetArrayLocalIdx];
            if (targetArray2.Address is null)
            {
                _runtimeValueInA = false;
                _savedRuntimeToTemp = false;
                _lastLoadedLocalIndex = null;
                return;
            }

            // Remove previously emitted instructions
            if (_blockCountAtILOffset.TryGetValue(targetArrayILOffset, out int blockCountExpr))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountExpr;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }

            // Find the instruction array index of the target array ldloc
            int targetArrayInstrIdx = -1;
            for (int i = 0; i < Instructions.Length; i++)
            {
                if (Instructions[i].Offset == targetArrayILOffset) { targetArrayInstrIdx = i; break; }
            }

            // Find the constant value (last ldc before stelem, searching backwards)
            int? exprValue = null;
            int valueInstrIdx = -1;
            for (int i = Index - 1; i > targetArrayInstrIdx; i--)
            {
                int? val = GetLdcValue(Instructions[i]);
                if (val != null && Instructions[i].OpCode != ILOpCode.And)
                {
                    exprValue = val;
                    valueInstrIdx = i;
                    break;
                }
            }

            // Emit index expression: instructions between array ldloc and value
            int idxStart = targetArrayInstrIdx + 1;
            if (idxStart >= 0 && valueInstrIdx >= 0)
            {
                for (int i = idxStart; i < valueInstrIdx; i++)
                {
                    var il = Instructions[i];
                    if (il.OpCode == ILOpCode.Call && il.String != null)
                    {
                        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, il.String);
                        UsedMethods?.Add(il.String);
                    }
                    else if (il.OpCode == ILOpCode.And)
                    {
                        // Find the AND mask from the preceding ldc
                        if (i > idxStart)
                        {
                            int? mask = GetLdcValue(Instructions[i - 1]);
                            if (mask != null)
                                Emit(Opcode.AND, AddressMode.Immediate, checked((byte)mask.Value));
                        }
                    }
                    // Skip ldc values that are part of the index expression (AND mask, etc.)
                }
                // Move index from A to X
                Emit(Opcode.TAX, AddressMode.Implied);
            }

            // Load value and store
            if (exprValue != null)
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)exprValue.Value));
            Emit(Opcode.STA, AddressMode.AbsoluteX, (ushort)targetArray2.Address);

            _immediateInA = null;
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            return;
        }

        var targetArray = Locals[targetArrayLocalIdx];

        // Handle constant-index stores (e.g., actor_dx[0] = 254)
        if (constantIndex >= 0)
        {
            // ROM arrays (LabelName-based) — constant-index stores are initialization
            // artifacts handled at the Transpiler level, no runtime code needed
            if (targetArray.Address is null)
                return;

            // Runtime arrays — emit LDA #value; STA array+offset
            // Remove ALL previously emitted instructions for this stelem sequence
            if (_blockCountAtILOffset.TryGetValue(targetArrayILOffset, out int blockCountConst))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountConst;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }

            // Determine the value to store from the value expression
            int? constValue = null;
            for (int i = valueStart; i < Index; i++)
            {
                int? val = GetLdcValue(Instructions[i]);
                if (val != null)
                    constValue = val;
            }

            if (constValue != null)
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)constValue.Value));

            Emit(Opcode.STA, AddressMode.Absolute, (ushort)(targetArray.Address + constantIndex));

            _immediateInA = null;
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            return;
        }

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
        int addValue = 0;
        bool hasMul = false;
        int mulValue = 0;
        int mulLocalIdx = -1;
        bool hasTwoLdelems = false;
        int sourceArray1Idx = -1;
        int sourceArray2Idx = -1;
        int valueLocalIdx = -1;

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
                case ILOpCode.Mul:
                    hasMul = true;
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
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1: case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                case ILOpCode.Ldloc_s:
                    {
                        // Detect standalone value local (not part of arr[idx] ldelem pattern)
                        bool isLdelemPart = false;
                        if (i + 1 < Index && Instructions[i + 1].OpCode == ILOpCode.Ldelem_u1)
                            isLdelemPart = true; // This is the index before ldelem
                        else if (i + 2 < Index
                            && Instructions[i + 1].OpCode is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s
                            && Instructions[i + 2].OpCode == ILOpCode.Ldelem_u1)
                            isLdelemPart = true; // This is the array before ldloc+ldelem
                        if (!isLdelemPart)
                        {
                            var locIdx = GetLdlocIndex(il);
                            if (locIdx != null)
                                valueLocalIdx = locIdx.Value;
                        }
                    }
                    break;
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                    {
                        int val = il.OpCode == ILOpCode.Ldc_i4_m1 ? -1 : (il.OpCode - ILOpCode.Ldc_i4_0);
                        // Check what operation consumes this constant
                        if (i + 1 < Index)
                        {
                            if (Instructions[i + 1].OpCode == ILOpCode.And)
                                andMask = val;
                            else if (Instructions[i + 1].OpCode == ILOpCode.Sub)
                                subValue = val;
                            else if (Instructions[i + 1].OpCode == ILOpCode.Mul)
                            {
                                mulValue = val;
                                if (i - 1 >= valueStart)
                                {
                                    var locIdx = GetLdlocIndex(Instructions[i - 1]);
                                    if (locIdx != null) mulLocalIdx = locIdx.Value;
                                }
                            }
                            else if (Instructions[i + 1].OpCode == ILOpCode.Add)
                                addValue = val;
                        }
                    }
                    break;
                case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                    {
                        int val = il.Integer ?? 0;
                        if (i + 1 < Index)
                        {
                            if (Instructions[i + 1].OpCode == ILOpCode.And)
                                andMask = val;
                            else if (Instructions[i + 1].OpCode == ILOpCode.Sub)
                                subValue = val;
                            else if (Instructions[i + 1].OpCode == ILOpCode.Mul)
                            {
                                mulValue = val;
                                if (i - 1 >= valueStart)
                                {
                                    var locIdx = GetLdlocIndex(Instructions[i - 1]);
                                    if (locIdx != null) mulLocalIdx = locIdx.Value;
                                }
                            }
                            else if (Instructions[i + 1].OpCode == ILOpCode.Add)
                                addValue = val;
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
            // Pattern: arr[i] = call(...) or arr[i] = (call(...) & N) - M + A * C
            // Collect constant arguments from IL before the call instruction
            var callArgs = new List<int>();
            for (int i = valueStart; i < Index; i++)
            {
                if (Instructions[i].OpCode == ILOpCode.Call)
                    break;
                int? val = GetLdcValue(Instructions[i]);
                if (val != null) callArgs.Add(val.Value);
            }
            // Push all args except the last (cc65 calling convention: last arg in A)
            for (int j = 0; j < callArgs.Count - 1; j++)
            {
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)callArgs[j]));
                EmitJSR("pusha");
            }
            if (callArgs.Count > 0)
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)callArgs[callArgs.Count - 1]));

            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, callName);
            UsedMethods?.Add(callName);
            
            // Handle post-call operations
            if (hasMul && mulValue > 0)
            {
                int shifts = 0;
                for (int v = mulValue; v > 1; v >>= 1) shifts++;
                for (int s = 0; s < shifts; s++)
                    Emit(Opcode.ASL, AddressMode.Accumulator);
            }
            if (hasAnd)
            {
                Emit(Opcode.AND, AddressMode.Immediate, checked((byte)andMask));
            }
            if (hasSub)
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Immediate, checked((byte)subValue));
            }
            if (hasAdd)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)addValue));
            }
        }
        else if (hasMul && mulLocalIdx >= 0)
        {
            // Pattern: arr[i] = local * constant (+ optional constant)
            // Multiply by power of 2 using ASL shifts
            var mulLocal = Locals[mulLocalIdx];
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)mulLocal.Address!);
            int shifts = 0;
            for (int v = mulValue; v > 1; v >>= 1) shifts++;
            for (int s = 0; s < shifts; s++)
                Emit(Opcode.ASL, AddressMode.Accumulator);
            if (addValue != 0)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)addValue));
            }
        }
        else if (valueLocalIdx >= 0)
        {
            // Pattern: arr[i] = local, arr[i] = (local & N), arr[i] = (local + N), etc.
            var valueLoc = Locals[valueLocalIdx];
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valueLoc.Address!);
            if (hasAnd)
                Emit(Opcode.AND, AddressMode.Immediate, checked((byte)andMask));
            if (hasAdd)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)addValue));
            }
            if (hasSub)
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Immediate, checked((byte)subValue));
            }
        }
        else
        {
            // Simple constant or unknown — find the last constant in value expression
            int? constVal = null;
            for (int i = valueStart; i < Index; i++)
            {
                int? val = GetLdcValue(Instructions[i]);
                if (val != null) constVal = val;
            }
            if (constVal != null)
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)constVal.Value));
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