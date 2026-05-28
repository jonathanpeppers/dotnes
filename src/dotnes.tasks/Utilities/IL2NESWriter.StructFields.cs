using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Struct field management — field offset calculation, load/store, static fields.
/// Delegates to <see cref="LocalVariableManager"/> for pure allocation and query methods.
/// </summary>
partial class IL2NESWriter
{
    // Forwarding methods for backward compatibility — delegate to Variables
    ushort GetOrAllocateStaticField(string fieldName) => Variables.GetOrAllocateStaticField(fieldName);
    ushort GetOrAllocateStructLocal(int localIndex, string structType) => Variables.GetOrAllocateStructLocal(localIndex, structType);
    int GetFieldOffset(string structType, string fieldName) => Variables.GetFieldOffset(structType, fieldName);
    int GetStructSize(string structType) => Variables.GetStructSize(structType);
    string ResolveStructType(int localIndex, string fieldName) => Variables.ResolveStructType(localIndex, fieldName);

    void HandleStsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        bool isWord = Variables.WordStaticFields.Contains(fieldName);
        if (Stack.Count > 0) Stack.Pop();

        if (isWord)
        {
            if (_ushortInAX)
            {
                // 16-bit value in A:X — store both bytes
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.STX, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else if (_runtimeValueInA)
            {
                // 8-bit runtime value — zero-extend high byte
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else if (_immediateInA != null)
            {
                int val = (int)_immediateInA;
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(val & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)((val >> 8) & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
            else
            {
                // Constant from WriteLdc — store low byte, zero high byte
                Emit(Opcode.STA, AddressMode.Absolute, addr);
                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
        }
        else
        {
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
        }
        _runtimeValueInA = false;
        _ushortInAX = false;
        _immediateInA = null;
        _pokeLastValue = null;
    }

    void HandleLdsfld(string fieldName)
    {
        var addr = GetOrAllocateStaticField(fieldName);
        bool isWord = Variables.WordStaticFields.Contains(fieldName);

        // Preserve any pending value in A before clobbering it,
        // mirroring WriteLdloc behavior.
        if (_ushortInAX)
        {
            EmitJSR("pushax");
            UsedMethods?.Add("pushax");
            _ushortInAX = false;
        }
        else if (_runtimeValueInA && !LastLDA)
        {
            // When loading args for a default-path call, push to cc65 stack
            // instead of saving to TEMP (which only holds one value).
            if (ScanForUpcomingMultiArgCall() || IsNextCallToDefaultPathTarget())
            {
                EmitJSR("pusha");
                _runtimeValueInA = false;
            }
            else
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                _savedRuntimeToTemp = true;
            }
        }
        else if (LastLDA)
        {
            EmitJSR("pusha");
        }

        if (isWord)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(addr + 1));
            _ushortInAX = true;
        }
        else
        {
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
        }
        _runtimeValueInA = true;
        _immediateInA = null;
        _pokeLastValue = null;
        Stack.Push(0);

        // Look ahead: if the next instruction loads another value and the
        // upcoming Call uses the default call path, push the current value
        // to the cc65 stack so it survives the next load.
        if (!isWord && ScanForUpcomingMultiArgCall())
        {
            EmitJSR("pusha");
            _runtimeValueInA = false;
        }
    }

    /// <summary>
    /// Handles stfld: store a value to a struct field on zero page.
    /// IL pattern: ldloca.s N, ldc.i4 value, stfld fieldName
    /// </summary>
    void HandleStfld(string fieldName)
    {
        // Handle closure struct field store
        if (_pendingStructLocal != null
            && ClosureFieldTypes != null
            && ClosureFieldTypes.ContainsKey(fieldName))
        {
            _pendingStructLocal = null;
            int fieldSize = ClosureFieldTypes[fieldName];

            if (fieldSize == -1) // byte[] field
            {
                // Associate the byte array label from the preceding ldtoken
                if (_lastByteArrayLabel != null)
                {
                    ClosureFieldLabels[fieldName] = _lastByteArrayLabel;
                    _lastByteArrayLabel = null;
                }
                if (Stack.Count > 0) Stack.Pop();
                _runtimeValueInA = false;
                _ushortInAX = false;
                _immediateInA = null;
                return;
            }
            else // scalar field (byte, ushort, short, int captured in closure)
            {
                ushort addr = ClosureFieldAddresses[fieldName];
                int value = Stack.Count > 0 ? Stack.Pop() : 0;

                if (fieldSize == 1)
                {
                    if (_runtimeValueInA)
                    {
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        _runtimeValueInA = false;
                    }
                    else
                    {
                        RemoveLastInstructions(1);
                        Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                    }
                }
                else
                {
                    // Multi-byte scalar (16-bit word: low/high bytes)
                    if (_runtimeValueInA && _ushortInAX)
                    {
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        Emit(Opcode.STX, AddressMode.Absolute, (ushort)(addr + 1));
                    }
                    else
                    {
                        byte low = (byte)(value & 0xFF);
                        byte high = (byte)((value >> 8) & 0xFF);
                        Emit(Opcode.LDA, AddressMode.Immediate, low);
                        Emit(Opcode.STA, AddressMode.Absolute, addr);
                        Emit(Opcode.LDA, AddressMode.Immediate, high);
                        Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
                    }
                    _runtimeValueInA = false;
                    _ushortInAX = false;
                }
                _immediateInA = null;
                return;
            }
        }

        // Check for struct array element access (from ldelema)
        if (_pendingStructElement is PendingStructElement stfldElement)
        {
            string structType = stfldElement.Type;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            // The value to store was pushed by ldc before stfld
            int value = Stack.Count > 0 ? Stack.Pop() : 0;

            if (stfldElement.RuntimeIndex)
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
                // Constant index: ConstantBase has the element base
                ushort fieldAddr = (ushort)(stfldElement.ConstantBase!.Value + fieldOffset);
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

            _pendingStructElement = null;
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
        // Handle closure struct field load (from ldarg.0 in closure methods
        // or ldloca.s in main)
        if ((_pendingClosureAccess || _pendingStructLocal != null)
            && ClosureFieldTypes != null
            && ClosureFieldTypes.ContainsKey(fieldName))
        {
            _pendingClosureAccess = false;
            _pendingStructLocal = null;
            int fieldSize = ClosureFieldTypes[fieldName];

            if (fieldSize == -1) // byte[] field
            {
                if (ClosureFieldLabels.TryGetValue(fieldName, out var label))
                {
                    EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, label);
                    EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, label);
                    _ushortInAX = true;
                }
                else
                {
                    throw new TranspileException(
                        $"Closure byte[] field '{fieldName}' has no ROM data label. " +
                        "Ensure the array is initialized before it is used in a closure method.");
                }
            }
            else // scalar field
            {
                ushort addr = ClosureFieldAddresses[fieldName];
                Emit(Opcode.LDA, AddressMode.Absolute, addr);
                if (fieldSize > 1)
                {
                    Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(addr + 1));
                    _ushortInAX = true;
                }
            }
            _runtimeValueInA = true;
            _immediateInA = null;
            Stack.Push(0);
            return;
        }

        // Check for struct array element access (from ldelema)
        if (_pendingStructElement is PendingStructElement ldfldElement)
        {
            string structType = ldfldElement.Type;
            int fieldOffset = GetFieldOffset(structType, fieldName);

            if (ldfldElement.RuntimeIndex)
            {
                // Variable index: X holds element offset, use AbsoluteX
                ushort fieldAddr = (ushort)(_structArrayBaseForRuntimeIndex + fieldOffset);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, fieldAddr);
            }
            else
            {
                // Constant index: ConstantBase has the element base
                ushort fieldAddr = (ushort)(ldfldElement.ConstantBase!.Value + fieldOffset);
                Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            }

            _pendingStructElement = null;
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
            else if (_lastLoadedLocalIndex is not null && _lastLoadedLocalIndex >= 0 && Variables.IsStructLocal(_lastLoadedLocalIndex.Value))
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
    /// Handles <c>ldflda</c> on a buffer field (<c>fixed byte buf[N]</c> or
    /// <c>[InlineArray(N)]</c>-typed field). Pattern-matches the full IL pattern through
    /// the matching <c>stind.i1</c> or <c>ldind.u1</c> and emits a single LDA/STA
    /// (Absolute or AbsoluteX) at <c>base+offset+index</c>.
    /// Supported indices: constant, ldarg, ldloc, ldsfld.
    /// Supported store values: constant, ldloc, ldsfld.
    /// </summary>
    void HandleLdflda(string fieldName)
    {
        // We only handle the OUTER ldflda on a buffer field. The inner ldflda
        // (FixedElementField) is reached later via the skip mechanism and would not
        // hit this handler. If we somehow reach an inner ldflda directly, the absence
        // of _pendingStructLocal causes us to fall through with a clearer error below.
        if (_pendingStructLocal is null)
            throw new TranspileException(
                $"ldflda '{fieldName}' without a preceding ldloca.s is not supported.", MethodName);

        int localIndex = _pendingStructLocal.Value;
        _pendingStructLocal = null;

        string structType = ResolveStructType(localIndex, fieldName);
        if (!BufferFieldSizes.TryGetValue((structType, fieldName), out int bufferSize) || bufferSize <= 0)
            throw new TranspileException(
                $"ldflda '{structType}.{fieldName}' is only supported for fixed-size buffer or [InlineArray] fields. " +
                "Passing a struct field by reference is not yet supported.", MethodName);

        ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
        int fieldOffset = GetFieldOffset(structType, fieldName);
        ushort bufferBase = (ushort)(baseAddr + fieldOffset);

        if (Instructions is null)
            throw new InvalidOperationException("HandleLdflda requires Instructions to be set");

        // First pass: locate boundary opcodes (add / inline-array call / span call) and the
        // terminal stind.i1 / ldind.u1. This classifies which lowering pattern we're in and
        // where the index vs value live in the IL stream.
        int? addIdx = null;             // `add` for fixed-buffer (base + index)
        int? inlineCallIdx = null;      // InlineArrayElementRef / InlineArrayFirstElementRef
        bool inlineArrayHasIndex = false; // false = FirstElementRef (no explicit index)
        int? spanLdlocaIdx = null;      // ldloca.s for the Span<byte> temp local
        int? spanGetItemIdx = null;     // call Span<>.get_Item
        bool spanPath = false;
        bool isStore = false;
        bool isLoad = false;
        int endIndex = -1;

        for (int j = Index + 1; j < Instructions.Length; j++)
        {
            var op = Instructions[j].OpCode;
            switch (op)
            {
                case ILOpCode.Ldflda:
                    // Inner ldflda (FixedElementField): ignore.
                    continue;
                case ILOpCode.Call:
                {
                    string? mname = Instructions[j].String;
                    if (mname is "InlineArrayElementRef")
                    { inlineCallIdx = j; inlineArrayHasIndex = true; continue; }
                    if (mname is "InlineArrayFirstElementRef")
                    { inlineCallIdx = j; inlineArrayHasIndex = false; continue; }
                    if (mname is "InlineArrayAsSpan")
                    { spanPath = true; continue; }
                    if (mname is "get_Item" && spanPath)
                    { spanGetItemIdx = j; continue; }
                    throw new TranspileException(
                        $"Unexpected call '{mname}' while lowering buffer field '{fieldName}' access.", MethodName);
                }
                case ILOpCode.Stloc_0: case ILOpCode.Stloc_1: case ILOpCode.Stloc_2: case ILOpCode.Stloc_3:
                case ILOpCode.Stloc_s: case ILOpCode.Stloc:
                    // Inside the Span path, Roslyn stores the Span<byte> in a temp local.
                    if (spanPath) continue;
                    throw new TranspileException($"Unexpected stloc while lowering buffer field '{fieldName}' access.", MethodName);
                case ILOpCode.Ldloca_s: case ILOpCode.Ldloca:
                    if (spanPath) { spanLdlocaIdx = j; continue; }
                    throw new TranspileException($"Unexpected ldloca while lowering buffer field '{fieldName}' access.", MethodName);
                case ILOpCode.Add:
                    // Fixed-buffer lowering produces exactly one `add` for `base + index`.
                    // Anything beyond one would imply additional arithmetic in the index
                    // expression (e.g. `buf[i + 1]`), which we don't yet recognise — reject
                    // explicitly to avoid silent miscompilation.
                    if (addIdx != null)
                        throw new TranspileException(
                            $"Arithmetic in the index expression for buffer field '{fieldName}' is not supported.", MethodName);
                    addIdx = j;
                    continue;
                case ILOpCode.Stind_i1:
                    isStore = true;
                    endIndex = j;
                    break;
                case ILOpCode.Ldind_u1:
                    isLoad = true;
                    endIndex = j;
                    break;
                // Everything else (ldc, ldarg, ldloc, ldsfld, conv, nop) is value/index data —
                // we extract it precisely in the second pass below.
                default:
                    continue;
            }
            if (endIndex >= 0) break;
        }

        if (endIndex < 0)
            throw new TranspileException(
                $"Could not find matching stind.i1/ldind.u1 for buffer field '{fieldName}'.", MethodName);

        // Determine the index-expression range and whether an explicit index exists.
        // Pattern shapes:
        //   Fixed buffer + index:   [ldflda inner] <index> add <value> stind.i1
        //   Fixed buffer no index:  [ldflda inner] <value> stind.i1              (implicit 0)
        //   InlineArray + index:    <index> call InlineArrayElementRef <value> stind.i1
        //   InlineArray no index:   call InlineArrayFirstElementRef <value> stind.i1  (implicit 0)
        //   Span path (runtime):    ldc.i4 N call InlineArrayAsSpan stloc M
        //                           ldloca.s M <index> call get_Item <value?> stind/ldind
        bool hasExplicitIndex;
        int indexScanStart = -1, indexScanEnd = -1;
        int valueScanFrom;
        if (spanPath)
        {
            if (spanGetItemIdx is null || spanLdlocaIdx is null)
                throw new TranspileException(
                    $"InlineArray runtime-index pattern for '{fieldName}' was not recognised.", MethodName);
            hasExplicitIndex = true;
            indexScanStart = spanLdlocaIdx.Value + 1;
            indexScanEnd = spanGetItemIdx.Value;
            valueScanFrom = endIndex - 1;
        }
        else if (inlineCallIdx != null)
        {
            hasExplicitIndex = inlineArrayHasIndex;
            if (hasExplicitIndex)
            {
                indexScanStart = Index + 1;
                indexScanEnd = inlineCallIdx.Value;
            }
            valueScanFrom = endIndex - 1;
        }
        else
        {
            // Fixed-buffer path
            hasExplicitIndex = addIdx != null;
            if (hasExplicitIndex)
            {
                indexScanStart = Index + 1;
                indexScanEnd = addIdx!.Value;
            }
            valueScanFrom = endIndex - 1;
        }

        // Resolve the index expression.
        int? indexConst = null;
        int? indexLocalIdx = null;
        int? indexArgIdx = null;
        string? indexStaticField = null;
        if (hasExplicitIndex)
        {
            ScanForIndexExpr(indexScanStart, indexScanEnd,
                out indexConst, out indexLocalIdx, out indexArgIdx, out indexStaticField);
            bool foundIdx = indexConst != null || indexLocalIdx != null || indexArgIdx != null || indexStaticField != null;
            if (!foundIdx)
                throw new TranspileException(
                    $"Could not determine index expression for buffer field '{fieldName}'.", MethodName);
        }
        else
        {
            indexConst = 0;
        }

        bool runtimeIdx = indexLocalIdx != null || indexArgIdx != null || indexStaticField != null;

        // Resolve the value (for stores).
        int? storeValueConst = null;
        int? storeValueLocalIdx = null;
        string? storeValueStaticField = null;
        if (isStore)
        {
            ScanForValueExpr(valueScanFrom,
                out storeValueConst, out storeValueLocalIdx, out storeValueStaticField);
            if (storeValueConst is null && storeValueLocalIdx is null && storeValueStaticField is null)
                throw new TranspileException(
                    $"Could not determine value being stored to buffer field '{fieldName}'. " +
                    "Only constants, locals, and static fields are supported as buffer store values; " +
                    "method arguments and expression results are not yet implemented.", MethodName);
        }

        if (isStore)
        {
            if (!runtimeIdx)
            {
                ushort target = (ushort)(bufferBase + indexConst!.Value);
                EmitLoadValueIntoA(storeValueConst, storeValueLocalIdx, storeValueStaticField);
                Emit(Opcode.STA, AddressMode.Absolute, target);
            }
            else
            {
                // Order matters: load X (index) first, then A (value), so the LDA doesn't clobber X.
                EmitLoadIndexIntoX(indexLocalIdx, indexArgIdx, indexStaticField);
                EmitLoadValueIntoA(storeValueConst, storeValueLocalIdx, storeValueStaticField);
                Emit(Opcode.STA, AddressMode.AbsoluteX, bufferBase);
            }
            _runtimeValueInA = false;
            _immediateInA = null;
        }
        else if (isLoad)
        {
            if (!runtimeIdx)
            {
                ushort target = (ushort)(bufferBase + indexConst!.Value);
                Emit(Opcode.LDA, AddressMode.Absolute, target);
            }
            else
            {
                EmitLoadIndexIntoX(indexLocalIdx, indexArgIdx, indexStaticField);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, bufferBase);
            }
            _runtimeValueInA = true;
            _immediateInA = null;
            // ldind.u1 leaves a byte on the IL stack
            Stack.Push(0);
        }

        // Skip every instruction we just consumed (including the stind.i1/ldind.u1).
        SkipUntilIndex = endIndex + 1;
        previous = isStore ? ILOpCode.Stind_i1 : ILOpCode.Ldind_u1;
    }

    /// <summary>
    /// Scans an IL window for the first instruction that produces an index value
    /// (constant, local, argument, or static field).
    /// </summary>
    void ScanForIndexExpr(int scanStart, int scanEndExclusive,
        out int? indexConst, out int? indexLocalIdx, out int? indexArgIdx, out string? indexStaticField)
    {
        indexConst = null; indexLocalIdx = null; indexArgIdx = null; indexStaticField = null;
        if (Instructions is null) return;

        for (int k = scanStart; k < scanEndExclusive; k++)
        {
            var op = Instructions[k].OpCode;
            int? c = op switch
            {
                ILOpCode.Ldc_i4_0 => 0, ILOpCode.Ldc_i4_1 => 1, ILOpCode.Ldc_i4_2 => 2,
                ILOpCode.Ldc_i4_3 => 3, ILOpCode.Ldc_i4_4 => 4, ILOpCode.Ldc_i4_5 => 5,
                ILOpCode.Ldc_i4_6 => 6, ILOpCode.Ldc_i4_7 => 7, ILOpCode.Ldc_i4_8 => 8,
                ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s => Instructions[k].Integer ?? 0,
                _ => (int?)null,
            };
            if (c != null) { indexConst = c; return; }
            switch (op)
            {
                case ILOpCode.Ldarg_0: indexArgIdx = 0; return;
                case ILOpCode.Ldarg_1: indexArgIdx = 1; return;
                case ILOpCode.Ldarg_2: indexArgIdx = 2; return;
                case ILOpCode.Ldarg_3: indexArgIdx = 3; return;
                case ILOpCode.Ldarg_s: case ILOpCode.Ldarg:
                    indexArgIdx = Instructions[k].Integer ?? 0; return;
                case ILOpCode.Ldloc_0: indexLocalIdx = 0; return;
                case ILOpCode.Ldloc_1: indexLocalIdx = 1; return;
                case ILOpCode.Ldloc_2: indexLocalIdx = 2; return;
                case ILOpCode.Ldloc_3: indexLocalIdx = 3; return;
                case ILOpCode.Ldloc_s: case ILOpCode.Ldloc:
                    indexLocalIdx = Instructions[k].Integer ?? 0; return;
                case ILOpCode.Ldsfld:
                    indexStaticField = Instructions[k].String; return;
            }
        }
    }

    /// <summary>
    /// Scans backwards from <paramref name="valueScanFrom"/> for the value being stored:
    /// constant, local, or static field. Skips pass-through conversions.
    /// </summary>
    void ScanForValueExpr(int valueScanFrom,
        out int? storeValueConst, out int? storeValueLocalIdx, out string? storeValueStaticField)
    {
        storeValueConst = null; storeValueLocalIdx = null; storeValueStaticField = null;
        if (Instructions is null || valueScanFrom < 0) return;

        for (int k = valueScanFrom; k >= 0; k--)
        {
            var op = Instructions[k].OpCode;
            int? c = op switch
            {
                ILOpCode.Ldc_i4_0 => 0, ILOpCode.Ldc_i4_1 => 1, ILOpCode.Ldc_i4_2 => 2,
                ILOpCode.Ldc_i4_3 => 3, ILOpCode.Ldc_i4_4 => 4, ILOpCode.Ldc_i4_5 => 5,
                ILOpCode.Ldc_i4_6 => 6, ILOpCode.Ldc_i4_7 => 7, ILOpCode.Ldc_i4_8 => 8,
                ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s => Instructions[k].Integer ?? 0,
                _ => (int?)null,
            };
            if (c != null) { storeValueConst = c; return; }
            switch (op)
            {
                case ILOpCode.Ldloc_0: storeValueLocalIdx = 0; return;
                case ILOpCode.Ldloc_1: storeValueLocalIdx = 1; return;
                case ILOpCode.Ldloc_2: storeValueLocalIdx = 2; return;
                case ILOpCode.Ldloc_3: storeValueLocalIdx = 3; return;
                case ILOpCode.Ldloc_s: case ILOpCode.Ldloc:
                    storeValueLocalIdx = Instructions[k].Integer ?? 0; return;
                case ILOpCode.Ldsfld:
                    storeValueStaticField = Instructions[k].String; return;
                case ILOpCode.Conv_u1: case ILOpCode.Conv_i1: case ILOpCode.Nop:
                    continue;
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Emits the LDA that loads the value (constant or runtime) into A for a buffer store.
    /// </summary>
    void EmitLoadValueIntoA(int? constVal, int? localIdx, string? staticField)
    {
        if (constVal != null)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(constVal.Value & 0xFF));
            return;
        }
        if (localIdx != null && Locals.TryGetValue(localIdx.Value, out var lcl) && lcl.Address is int la)
        {
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)la);
            return;
        }
        if (staticField != null && Variables.StaticFieldAddresses.TryGetValue(staticField, out var sfa))
        {
            Emit(Opcode.LDA, AddressMode.Absolute, sfa);
            return;
        }
        throw new TranspileException("Could not resolve buffer store value to an addressable value.", MethodName);
    }

    void EmitLoadIndexIntoX(int? localIdx, int? argIdx, string? staticField)
    {
        if (localIdx != null && Locals.TryGetValue(localIdx.Value, out var lcl) && lcl.Address is int la)
        {
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)la);
            return;
        }
        if (argIdx != null)
        {
            // Method arguments are loaded from the cc65 stack by `WriteLdarg` and are not
            // tracked in `Locals`. Falling back to `Locals[argIdx]` here would silently
            // load an unrelated same-numbered local. Until proper argument-to-X loading is
            // implemented (mirroring WriteLdarg into X), reject this case explicitly.
            throw new TranspileException(
                $"Runtime index from method argument {argIdx} into a buffer field is not yet supported.", MethodName);
        }
        if (staticField != null && Variables.StaticFieldAddresses.TryGetValue(staticField, out var sfa))
        {
            Emit(Opcode.LDX, AddressMode.Absolute, sfa);
            return;
        }
        throw new TranspileException("Could not resolve buffer-index expression to an addressable value.", MethodName);
    }
}
