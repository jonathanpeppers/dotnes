using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Transpiles .NET IL instructions into 6502 assembly for the NES.
/// This is the core class containing fields, properties, and the public API surface.
/// The IL dispatch, code emission, and optimization logic are in partial class files.
/// </summary>
partial class IL2NESWriter : NESWriter
{
    public IL2NESWriter(Stream stream, bool leaveOpen = false, ILogger? logger = null, ReflectionCache? reflectionCache = null)
        : base(stream, leaveOpen, logger)
    {
        if (reflectionCache != null)
            _reflectionCache = reflectionCache;
    }

    /// <summary>
    /// Manages local variable zero-page allocation, struct field tracking, and static field addresses.
    /// </summary>
    internal readonly LocalVariableManager Variables = new();

    // Forwarding properties for backward compatibility with existing partial class code
    Dictionary<int, Local> Locals => Variables.Locals;
    static ushort local => LocalStackBase;

    /// <summary>
    /// The local evaluation stack
    /// </summary>
    internal readonly Stack<int> Stack = new();

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
    /// True when a compile-time constant was saved to the cc65 stack via JSR pusha,
    /// because a subsequent Ldloc needed A for a runtime value. HandleAddSub must
    /// use JSR popa to retrieve it (and keep the cc65 stack balanced).
    /// </summary>
    bool _savedConstantViaPusha;

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
    /// True when inside a dup cascade (cascading if-else using dup+ldc+bne/beq pattern).
    /// Set on the first dup when _runtimeValueInA is true. Causes subsequent dups to
    /// emit LDA TEMP_HI (reload saved value) and branch handlers to emit STA TEMP_HI
    /// (save A for the next check). This ensures A holds the correct comparison value
    /// even after intermediate if-blocks modify A via JSR calls.
    /// </summary>
    bool _dupCascadeActive;

    /// <summary>
    /// Set by the dup handler when the next branch instruction should save A to TEMP_HI.
    /// Consumed (cleared) by the bne/beq handler after emitting STA TEMP_HI. This ensures
    /// only the branch immediately following a dup+ldc compare writes to TEMP_HI, not
    /// unrelated branches inside the if-body.
    /// </summary>
    bool _dupPendingSave;

    /// <summary>
    /// Set by dup when _ushortInAX was true, indicating X still holds the high byte
    /// of a duplicated ushort value. Used by shr to emit TXA for the high byte extraction
    /// after a dup+conv_u1+stloc pattern extracts the low byte.
    /// </summary>
    bool _dupPreservedUshortHi;

    /// <summary>
    /// Set by ldftn handler with the method name. Consumed by nmi_set_callback/irq_set_callback
    /// to resolve the callback label from a function pointer instead of a string literal.
    /// </summary>
    string? _lastLdftnMethod;

    /// <summary>
    /// Number of parameters for the current user method being transpiled (0 for main).
    /// Used by ldarg handlers to compute cc65 stack offsets.
    /// </summary>
    public int MethodParamCount { get; init; }

    /// <summary>
    /// Per-parameter array type flags. true = byte[] (16-bit pointer, 2 stack bytes),
    /// false = byte (8-bit value, 1 stack byte). Used by WriteLdarg for correct offsets.
    /// </summary>
    public bool[] ParamIsArray { get; init; } = Array.Empty<bool>();

    /// <summary>
    /// Name of the current method being transpiled (null for main).
    /// Used to scope branch labels so they don't collide across methods.
    /// </summary>
    public string? MethodName { get; init; }

    /// <summary>
    /// Tracks extra values pushed to the cc65 stack within a user function body
    /// (via pusha between ldarg calls). Used to adjust ldarg stack offsets.
    /// </summary>
    int _argStackAdjust;

    /// <summary>
    /// Local variable indices that are word-sized (ushort). Forwarded to <see cref="Variables"/>.
    /// </summary>
    public HashSet<int> WordLocals { get => Variables.WordLocals; init => Variables.WordLocals = value; }

    /// <summary>
    /// Struct type layouts: type name → ordered list of (fieldName, fieldSizeInBytes).
    /// Forwarded to <see cref="Variables"/>.
    /// </summary>
    public Dictionary<string, List<(string Name, int Size)>> StructLayouts { get => Variables.StructLayouts; init => Variables.StructLayouts = value; }

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
    /// Cumulative bytes allocated for locals on zero page.
    /// Forwarded to <see cref="Variables"/>.
    /// </summary>
    public int LocalCount { get => Variables.LocalCount; set => Variables.LocalCount = value; }

    /// <summary>
    /// Set of user-defined method names (for detecting user method calls).
    /// </summary>
    public HashSet<string> UserMethodNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Names of extern methods (declared with static extern). Used to emit JSR _name.
    /// </summary>
    public HashSet<string> ExternMethodNames { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Generates a branch target label name scoped to the current method.
    /// For main(), returns "instruction_XX". For user methods, returns "methodName_instruction_XX".
    /// </summary>
    string InstructionLabel(int offset) =>
        MethodName is null ? $"instruction_{offset:X2}" : $"{MethodName}_instruction_{offset:X2}";

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
    /// Counter for generating unique byte array labels
    /// </summary>
    int _byteArrayLabelIndex;

    /// <summary>
    /// Gets or sets the starting index for byte array labels.
    /// Used to offset user method writers so their labels don't collide with the main writer's.
    /// </summary>
    internal int ByteArrayLabelStartIndex { set => _byteArrayLabelIndex = value; }

    /// <summary>
    /// Gets or sets the starting index for string labels.
    /// Used to offset user method writers so their string labels don't collide.
    /// </summary>
    internal int StringLabelStartIndex { set => _stringLabelIndex = value; }
    
    /// <summary>
    /// Track the last byte array label for Stloc handling
    /// </summary>
    string? _lastByteArrayLabel;
    int _lastByteArraySize;

    public ILInstruction[]? Instructions { get; set; }

    public int Index { get; set; }

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
