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

        if (!BufferFieldSizes.TryGetValue(fieldName, out int bufferSize) || bufferSize <= 0)
            throw new TranspileException(
                $"ldflda '{fieldName}' is only supported for fixed-size buffer or [InlineArray] fields. " +
                "Passing a struct field by reference is not yet supported.", MethodName);

        int localIndex = _pendingStructLocal.Value;
        _pendingStructLocal = null;

        string structType = ResolveStructType(localIndex, fieldName);
        ushort baseAddr = GetOrAllocateStructLocal(localIndex, structType);
        int fieldOffset = GetFieldOffset(structType, fieldName);
        ushort bufferBase = (ushort)(baseAddr + fieldOffset);

        if (Instructions is null)
            throw new InvalidOperationException("HandleLdflda requires Instructions to be set");

        // Walk forward and classify each instruction until we reach stind.i1 / ldind.u1.
        // Recognize:
        //   - second ldflda (FixedElementField) — ignore (fixed buffer)
        //   - call to InlineArrayElementRef / InlineArrayFirstElementRef — ignore (inline array)
        //   - call to InlineArrayAsSpan + stloc + ldloca.s + ... + call get_Item — Span path
        //   - ldc.i4.* / ldarg.* / ldloc.* / ldsfld — index expression
        //   - add — pointer + index (fixed buffer); ignore
        //   - ldc.i4.* immediately before stind.i1 — the VALUE being stored
        int? indexConst = null;
        int? indexLocalIdx = null;
        int? indexArgIdx = null;
        string? indexStaticField = null;
        int? storeValueConst = null;
        bool storeValueRuntime = false; // true when value comes from arg/local/etc., not yet supported here
        bool isStore = false;
        bool isLoad = false;
        bool spanPath = false;
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
                    if (mname is "InlineArrayElementRef" or "InlineArrayFirstElementRef")
                        continue;
                    if (mname is "InlineArrayAsSpan")
                    {
                        spanPath = true;
                        continue;
                    }
                    if (mname is "get_Item" && spanPath)
                        continue;
                    // Any other call inside this sequence is unexpected
                    throw new TranspileException(
                        $"Unexpected call '{mname}' while lowering buffer field '{fieldName}' access.", MethodName);
                }
                case ILOpCode.Stloc_0: case ILOpCode.Stloc_1: case ILOpCode.Stloc_2: case ILOpCode.Stloc_3:
                case ILOpCode.Stloc_s: case ILOpCode.Stloc:
                    // Inside the Span path, Roslyn stores the Span<byte> in a temp local.
                    if (spanPath) continue;
                    throw new TranspileException($"Unexpected stloc while lowering buffer field '{fieldName}' access.", MethodName);
                case ILOpCode.Ldloca_s: case ILOpCode.Ldloca:
                    if (spanPath) continue;
                    throw new TranspileException($"Unexpected ldloca while lowering buffer field '{fieldName}' access.", MethodName);
                case ILOpCode.Add:
                    // Fixed-buffer pointer + index. Just ignore — we already track base separately.
                    continue;
                case ILOpCode.Ldc_i4_0: indexConst ??= 0; break;
                case ILOpCode.Ldc_i4_1: indexConst ??= 1; break;
                case ILOpCode.Ldc_i4_2: indexConst ??= 2; break;
                case ILOpCode.Ldc_i4_3: indexConst ??= 3; break;
                case ILOpCode.Ldc_i4_4: indexConst ??= 4; break;
                case ILOpCode.Ldc_i4_5: indexConst ??= 5; break;
                case ILOpCode.Ldc_i4_6: indexConst ??= 6; break;
                case ILOpCode.Ldc_i4_7: indexConst ??= 7; break;
                case ILOpCode.Ldc_i4_8: indexConst ??= 8; break;
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                    indexConst ??= Instructions[j].Integer ?? 0;
                    break;
                case ILOpCode.Ldarg_0: indexArgIdx ??= 0; break;
                case ILOpCode.Ldarg_1: indexArgIdx ??= 1; break;
                case ILOpCode.Ldarg_2: indexArgIdx ??= 2; break;
                case ILOpCode.Ldarg_3: indexArgIdx ??= 3; break;
                case ILOpCode.Ldarg_s: case ILOpCode.Ldarg:
                    indexArgIdx ??= Instructions[j].Integer ?? 0;
                    break;
                case ILOpCode.Ldloc_0: indexLocalIdx ??= 0; break;
                case ILOpCode.Ldloc_1: indexLocalIdx ??= 1; break;
                case ILOpCode.Ldloc_2: indexLocalIdx ??= 2; break;
                case ILOpCode.Ldloc_3: indexLocalIdx ??= 3; break;
                case ILOpCode.Ldloc_s: case ILOpCode.Ldloc:
                    indexLocalIdx ??= Instructions[j].Integer ?? 0;
                    break;
                case ILOpCode.Ldsfld:
                    indexStaticField ??= Instructions[j].String;
                    break;
                case ILOpCode.Conv_u: case ILOpCode.Conv_i: case ILOpCode.Conv_u1:
                case ILOpCode.Conv_i1: case ILOpCode.Conv_u2: case ILOpCode.Conv_i2:
                case ILOpCode.Conv_u4: case ILOpCode.Conv_i4:
                case ILOpCode.Nop:
                    continue;
                case ILOpCode.Stind_i1:
                    // The instruction immediately before stind.i1 is the VALUE.
                    isStore = true;
                    endIndex = j;
                    // The value should already be the most recently seen ldc.i4 (and we
                    // tracked it in indexConst because of the ??=). But we need to disambiguate:
                    // for stind.i1 with constant index, the order is:
                    //   <index> [add] <value> stind.i1
                    // So the LAST ldc.i4 seen is actually the value, and the FIRST ldc.i4 was
                    // the index. We re-scan to extract these correctly.
                    break;
                case ILOpCode.Ldind_u1:
                    isLoad = true;
                    endIndex = j;
                    break;
                default:
                    throw new TranspileException(
                        $"Unsupported opcode '{op}' while lowering buffer field '{fieldName}' access.", MethodName);
            }
            if (endIndex >= 0) break;
        }

        if (endIndex < 0)
            throw new TranspileException(
                $"Could not find matching stind.i1/ldind.u1 for buffer field '{fieldName}'.", MethodName);

        // Re-scan precisely to separate index from value for the store case.
        // Pattern shapes we accept:
        //   Fixed buffer store:    [ldflda inner] <index> [add] <value:ldc> stind.i1
        //   InlineArray store:     <index> call InlineArrayElementRef  <value:ldc> stind.i1
        //   InlineArray store 0:   call InlineArrayFirstElementRef     <value:ldc> stind.i1
        //   Fixed buffer load:     [ldflda inner] <index> [add] ldind.u1
        //   InlineArray load:      <index> call InlineArrayElementRef  ldind.u1
        //   InlineArray load 0:    call InlineArrayFirstElementRef     ldind.u1
        //   InlineArray runtime:   ldc.i4 N call InlineArrayAsSpan stloc.M
        //                          ldloca.s M <index> call get_Item <value?> stind.i1/ldind.u1
        indexConst = null; indexLocalIdx = null; indexArgIdx = null; indexStaticField = null;
        storeValueConst = null; storeValueRuntime = false;

        // For the Span path, the index is between ldloca.s (temp span local) and the call to get_Item.
        // For other paths, the index is before "add"/call.
        int valueScanStart = isStore ? endIndex - 1 : -1;
        int indexBoundaryEnd; // exclusive index for the index expression scan
        if (spanPath)
        {
            // Find the call to get_Item; index is between the previous ldloca.s and that call.
            int callItemIdx = -1, ldlocaIdx = -1;
            for (int k = Index + 1; k < endIndex; k++)
            {
                if (Instructions[k].OpCode == ILOpCode.Call && Instructions[k].String == "get_Item")
                { callItemIdx = k; break; }
                if (Instructions[k].OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca)
                    ldlocaIdx = k;
            }
            if (callItemIdx < 0 || ldlocaIdx < 0)
                throw new TranspileException(
                    $"InlineArray runtime-index pattern for '{fieldName}' was not recognised.", MethodName);
            indexBoundaryEnd = callItemIdx;
            // Scan range for index: [ldlocaIdx+1, callItemIdx)
            ScanForIndexAndValue(ldlocaIdx + 1, indexBoundaryEnd, isStore ? valueScanStart : -1,
                out indexConst, out indexLocalIdx, out indexArgIdx, out indexStaticField,
                out storeValueConst, out storeValueRuntime);
        }
        else
        {
            indexBoundaryEnd = endIndex; // up to but not including stind/ldind
            ScanForIndexAndValue(Index + 1, indexBoundaryEnd, isStore ? valueScanStart : -1,
                out indexConst, out indexLocalIdx, out indexArgIdx, out indexStaticField,
                out storeValueConst, out storeValueRuntime);
        }

        // Determine addressing
        bool runtimeIdx = indexLocalIdx != null || indexArgIdx != null || indexStaticField != null;

        // Pop IL evaluation stack entries that the consumed instructions pushed.
        // For store: the stind.i1 pops <address> and <value>. The address was conceptually
        // pushed by the outer ldflda. Our existing dispatch hasn't actually pushed anything
        // for ldflda (we're handling it right now), and we haven't dispatched any of the
        // skipped instructions, so we don't need to touch the IL stack — neither the index
        // nor the value was visibly pushed.

        if (isStore)
        {
            // Value to store
            if (storeValueConst is null && !storeValueRuntime)
                throw new TranspileException(
                    $"Could not determine value being stored to buffer field '{fieldName}'.", MethodName);

            if (!runtimeIdx)
            {
                ushort target = (ushort)(bufferBase + (indexConst ?? 0));
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(storeValueConst!.Value & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, target);
            }
            else
            {
                EmitLoadIndexIntoX(indexLocalIdx, indexArgIdx, indexStaticField);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(storeValueConst!.Value & 0xFF));
                Emit(Opcode.STA, AddressMode.AbsoluteX, bufferBase);
            }
            _runtimeValueInA = false;
            _immediateInA = null;
        }
        else if (isLoad)
        {
            if (!runtimeIdx)
            {
                ushort target = (ushort)(bufferBase + (indexConst ?? 0));
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

    void ScanForIndexAndValue(int scanStart, int scanEndExclusive, int valueScanFrom,
        out int? indexConst, out int? indexLocalIdx, out int? indexArgIdx, out string? indexStaticField,
        out int? storeValueConst, out bool storeValueRuntime)
    {
        indexConst = null; indexLocalIdx = null; indexArgIdx = null; indexStaticField = null;
        storeValueConst = null; storeValueRuntime = false;

        if (Instructions is null) return;

        // Scan for the index: first integer-producing instruction in [scanStart, scanEndExclusive).
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
            if (c != null) { indexConst = c; break; }
            switch (op)
            {
                case ILOpCode.Ldarg_0: indexArgIdx = 0; break;
                case ILOpCode.Ldarg_1: indexArgIdx = 1; break;
                case ILOpCode.Ldarg_2: indexArgIdx = 2; break;
                case ILOpCode.Ldarg_3: indexArgIdx = 3; break;
                case ILOpCode.Ldarg_s: case ILOpCode.Ldarg:
                    indexArgIdx = Instructions[k].Integer ?? 0; break;
                case ILOpCode.Ldloc_0: indexLocalIdx = 0; break;
                case ILOpCode.Ldloc_1: indexLocalIdx = 1; break;
                case ILOpCode.Ldloc_2: indexLocalIdx = 2; break;
                case ILOpCode.Ldloc_3: indexLocalIdx = 3; break;
                case ILOpCode.Ldloc_s: case ILOpCode.Ldloc:
                    indexLocalIdx = Instructions[k].Integer ?? 0; break;
                case ILOpCode.Ldsfld:
                    indexStaticField = Instructions[k].String; break;
                default: continue;
            }
            break;
        }

        // Scan for the value (last constant immediately before stind.i1).
        if (valueScanFrom >= 0)
        {
            for (int k = valueScanFrom; k > 0; k--)
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
                // Allow runtime expressions for the value in the future
                if (op is ILOpCode.Conv_u1 or ILOpCode.Conv_i1 or ILOpCode.Nop) continue;
                // Stop search at first non-skip instruction
                break;
            }
        }
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
            // Arguments for the main entry method are not addressable; only user methods
            // have ldarg with usable addressing. For main, ldarg is rare. We support it for
            // user methods by reading from the cc65 stack/parameter slot if available — but
            // the existing transpiler models args as zero-page slots in some scenarios.
            // For now, emit a clear error to surface unsupported edge cases early.
            // The simplest supported case is a user method with one byte arg → arg is in A
            // on entry, but by the time we reach this code A has been clobbered. So we use
            // the parameter zero-page slot if the writer tracks one.
            // Fall back to LDX from the same zero-page address allocated for arg-0.
            // In dotnes, user-method args are pushed by the caller and accessed by the callee
            // via specific patterns. For now, try local-address lookup; if missing, throw.
            if (Locals.TryGetValue(argIdx.Value, out var argLocal) && argLocal.Address is int aa)
            {
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)aa);
                return;
            }
            throw new TranspileException(
                $"Runtime index from arg {argIdx} into a buffer field is not supported at this position.", MethodName);
        }
        if (staticField != null && Variables.StaticFieldAddresses.TryGetValue(staticField, out var sfa))
        {
            Emit(Opcode.LDX, AddressMode.Absolute, sfa);
            return;
        }
        throw new TranspileException("Could not resolve buffer-index expression to an addressable value.", MethodName);
    }
}
