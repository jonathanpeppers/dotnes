using System.Collections.Immutable;
using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Array handling — byte[]/ushort[]/struct[] load, store, fill, and copy patterns.
/// </summary>
partial class IL2NESWriter
{
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
    /// Handles ldelema: load the address of an array element.
    /// For struct arrays: IL pattern: ldloc_N (array), ldc_i4 (index), ldelema TypeName
    /// Sets pending state for the subsequent stfld/ldfld.
    /// For byte arrays: IL pattern: ldloc/ldsfld (array), ldloc/ldsfld/ldc (index), ldelema System.Byte
    /// Sets pending state for the subsequent dup/ldind.u1/.../stind.i1 compound assignment.
    /// </summary>
    void HandleLdelema(ILInstruction instruction)
    {
        string? structType = instruction.String;
        if (structType == null)
            throw new TranspileException("Arrays are not supported for this element type. Only byte[], ushort[], and struct arrays are supported.", MethodName);

        // Byte arrays: compound assignment pattern (arr[i]++, arr[i] += expr)
        if (structType == "Byte")
        {
            HandleLdelemaByte();
            return;
        }

        // Ushort arrays: compound assignment pattern (arr[i]++, arr[i] += expr)
        if (structType == "UInt16")
        {
            HandleLdelemaUshort();
            return;
        }

        if (!StructLayouts.ContainsKey(structType))
            throw new TranspileException($"Arrays of type '{structType}' are not supported. Only byte[], ushort[], and struct arrays are supported.", MethodName);

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

                _pendingStructElement = new PendingStructElement(structType, ConstantBase: null, RuntimeIndex: true);
                _structArrayBaseForRuntimeIndex = arrayBase;
            }
            else
            {
                throw new TranspileException("Struct array access with a variable index requires the index to be stored in a local variable.", MethodName);
            }
        }
        else
        {
            // Constant index: compute element base at compile time
            ushort elementBase = (ushort)(arrayBase + index * structSize);
            _pendingStructElement = new PendingStructElement(structType, elementBase, RuntimeIndex: false);
        }

        _runtimeValueInA = false;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Handles ldelema System.Byte: sets up array+index addressing for compound
    /// byte array assignments (arr[i]++, arr[i] += expr, etc.).
    /// The subsequent dup/ldind.u1/.../stind.i1 sequence uses the pending state
    /// to emit LDA array,X (read) and STA array,X (write-back).
    /// </summary>
    void HandleLdelemaByte()
    {
        // Pop index and array ref from IL evaluation stack
        if (Stack.Count > 0) Stack.Pop(); // index
        if (Stack.Count > 0) Stack.Pop(); // array ref

        if (Instructions == null || Index < 2)
            throw new InvalidOperationException("ldelema System.Byte requires at least 2 preceding instructions");

        var indexInstr = Instructions[Index - 1];
        var arrayInstr = Instructions[Index - 2];

        // Resolve array local (same patterns as HandleLdelemU1)
        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
        Local? arrayLocalFromField = null;
        if (arrayLocalIdx == null)
            arrayLocalFromField = TryResolveArrayLocal(arrayInstr);

        if (arrayLocalIdx == null && arrayLocalFromField == null)
            throw new TranspileException("Compound byte array assignment requires the array to be stored in a local variable or static field.", MethodName);

        var arrayLocal = arrayLocalIdx != null ? Locals[arrayLocalIdx.Value] : arrayLocalFromField!;
        if (arrayLocal.Address == null)
            throw new TranspileException("Compound byte array assignment failed: the array variable has no allocated address.", MethodName);
        ushort arrayBase = (ushort)arrayLocal.Address;

        // Resolve index
        int? constantIndex = GetLdcValue(indexInstr);
        int? indexLocalIdx = GetLdlocIndex(indexInstr);
        int? indexLocalFromField = null;
        if (indexLocalIdx == null && constantIndex == null && indexInstr.OpCode == ILOpCode.Ldsfld && indexInstr.String != null)
        {
            var sfLocal = TryResolveArrayLocal(indexInstr);
            if (sfLocal?.Address != null)
                indexLocalFromField = sfLocal.Address;
        }

        // Remove previously emitted LDA instructions from WriteLdloc/WriteLdc
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
            {
                RemoveLastInstructions(instrToRemove);
                _savedRuntimeToTemp = false;
            }
        }

        // Set up X register with the index for AbsoluteX addressing
        if (constantIndex != null)
        {
            if (constantIndex.Value == 0)
            {
                // Constant index 0: use direct Absolute addressing
                _pendingByteArrayElement = new PendingByteArrayElement(arrayBase, arrayBase);
            }
            else
            {
                Emit(Opcode.LDX, AddressMode.Immediate, (byte)constantIndex.Value);
                _pendingByteArrayElement = new PendingByteArrayElement(arrayBase, ConstantElementAddress: null);
            }
        }
        else
        {
            Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;
            if (indexLocal?.Address is not null)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)indexLocal.Address);
            else if (indexLocalFromField != null)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)indexLocalFromField);
            else
                throw new TranspileException("Compound byte array assignment requires the index to be a constant or local variable.", MethodName);

            _pendingByteArrayElement = new PendingByteArrayElement(arrayBase, ConstantElementAddress: null);
        }

        // ldelema pushes a managed reference onto the eval stack
        Stack.Push(0);
        _runtimeValueInA = false;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Handles ldind.u1: load a byte through a pointer/reference.
    /// Used after ldelema System.Byte to read the current array element value.
    /// </summary>
    void HandleLdindU1()
    {
        if (_pendingByteArrayElement is not { } pending)
            throw new TranspileException("ldind.u1 without preceding ldelema System.Byte is not supported.", MethodName);

        // Pop the address reference from the eval stack
        if (Stack.Count > 0) Stack.Pop();

        if (pending.ConstantElementAddress is { } addr)
            Emit(Opcode.LDA, AddressMode.Absolute, addr);
        else
            Emit(Opcode.LDA, AddressMode.AbsoluteX, pending.ArrayBase);

        // Push the loaded byte value
        Stack.Push(0);
        _runtimeValueInA = true;
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Handles stind.i1: store a byte through a pointer/reference.
    /// Used after ldelema System.Byte to write back the modified array element.
    /// </summary>
    void HandleStindI1()
    {
        if (_pendingByteArrayElement is not { } pending)
            throw new TranspileException("stind.i1 without preceding ldelema System.Byte is not supported.", MethodName);

        // Pop the value and address reference from the eval stack
        if (Stack.Count > 0) Stack.Pop(); // value
        if (Stack.Count > 0) Stack.Pop(); // address

        if (pending.ConstantElementAddress is { } addr)
            Emit(Opcode.STA, AddressMode.Absolute, addr);
        else
            Emit(Opcode.STA, AddressMode.AbsoluteX, pending.ArrayBase);

        _pendingByteArrayElement = null;
        _runtimeValueInA = false;
    }

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
    /// Also supports complex index expressions (e.g., array[(x >> 3) + ((y >> 3) << 4)])
    /// where the index is computed by preceding arithmetic operations.
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
        // Also try static field arrays (ldsfld pattern)
        Local? arrayLocalFromField = null;
        if (arrayLocalIdx == null)
            arrayLocalFromField = TryResolveArrayLocal(arrayInstr);

        // Handle constant index (Ldc_i4_* or Ldc_i4_s)
        int? constantIndex = null;
        if (indexLocalIdx == null)
        {
            constantIndex = GetLdcValue(indexInstr);
        }

        // Also try static field for index
        int? indexLocalFromField = null;
        if (indexLocalIdx == null && constantIndex == null && indexInstr.OpCode == ILOpCode.Ldsfld && indexInstr.String != null)
        {
            var sfLocal = TryResolveArrayLocal(indexInstr);
            if (sfLocal?.Address != null)
                indexLocalFromField = sfLocal.Address;
        }

        // Complex index expression: the index was computed by preceding arithmetic
        // operations (shr, shl, add, sub, and, or, etc.). The result is already in A.
        if (indexLocalIdx == null && constantIndex == null && indexLocalFromField == null)
        {
            HandleLdelemU1ComplexIndex();
            return;
        }
        if (arrayLocalIdx == null && arrayLocalFromField == null)
            throw new TranspileException("Array element access requires the array to be stored in a local variable.", MethodName);

        Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;
        var arrayLocal = arrayLocalIdx != null ? Locals[arrayLocalIdx.Value] : arrayLocalFromField!;

        // Remove the previously emitted instructions from WriteLdloc/WriteLdc calls
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
            {
                RemoveLastInstructions(instrToRemove);
                // The removed instructions may have included a STA $TEMP emitted by
                // WriteLdloc's _runtimeValueInA save. Clear the flag so downstream
                // handlers (HandleRem, HandleAddSub) don't reference a non-existent save.
                _savedRuntimeToTemp = false;
            }
        }

        // Check if there's a preceding value in the block that will be clobbered by the
        // upcoming LDA. This happens in patterns like: ldloc rh; ldloc arr; ldloc idx; ldelem.u1; sub
        // After removing the array/index instructions, the block still has LDA $rh_addr.
        // We save it to TEMP so the arithmetic/comparison handler can use it.
        int blockCountAfterRemove = GetBufferedBlockCount();
        if (blockCountAfterRemove > 0)
        {
            var block = CurrentBlock!;
            var lastInstr = block[blockCountAfterRemove - 1];

            bool needsSave = false;

            // Case 1: A has a computed value (from SBC, ADC, AND, etc.) that will be lost
            // when we emit the LDA for the array element. This covers patterns like:
            //   dy = (byte)(rh - floor_ypos[0]); if (dy < floor_height[0])
            // where the subtraction result in A must be preserved for the branch comparison.
            if (_runtimeValueInA)
            {
                needsSave = true;
            }
            // Case 2: A has a loaded value (LDA) and the next IL op is arithmetic
            else if (lastInstr.Opcode == Opcode.LDA &&
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
                needsSave = nextIsArithmetic;
            }

            if (needsSave)
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                _savedRuntimeToTemp = true;
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
        else if (indexLocalFromField != null)
        {
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)indexLocalFromField);
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
            throw new TranspileException("Array element access failed: the array variable has no allocated address. Ensure the array is initialized before use.", MethodName);
        }

        Stack.Push(0); // Push a placeholder value
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = true;
    }

    /// <summary>
    /// Handles ldelem.u1 with a complex index expression (e.g., array[(x >> 3) + ((y >> 3) << 4)]).
    /// The index was computed by preceding arithmetic operations and the result is in A.
    /// Walks backward through IL to find the array instruction, removes array-load code
    /// from the block buffer, then emits TAX + LDA array,X.
    /// </summary>
    void HandleLdelemU1ComplexIndex()
    {
        // Walk backward through IL using stack depth tracking to find the array instruction.
        // ldelem.u1 consumes 2 values (array ref + index). When depth reaches 2,
        // the current instruction is the array load.
        int? arrayILIndex = null;
        int depth = 0;
        for (int i = Index - 1; i >= 0; i--)
        {
            var il = Instructions![i];
            int push = 0, pop = 0;
            switch (il.OpCode)
            {
                case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1: case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                case ILOpCode.Ldloc_s:
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                    push = 1; break;
                case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.Mul: case ILOpCode.Div:
                case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Xor:
                case ILOpCode.Shr: case ILOpCode.Shr_un: case ILOpCode.Shl:
                case ILOpCode.Rem: case ILOpCode.Rem_un:
                case ILOpCode.Ldelem_u1: // nested ldelem: pops array+index, pushes result
                    pop = 2; push = 1; break;
                case ILOpCode.Conv_u1: case ILOpCode.Conv_u2: case ILOpCode.Conv_u4:
                case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
                    break; // no net change
                default:
                    break;
            }
            depth += push - pop;
            if (depth >= 2)
            {
                arrayILIndex = i;
                break;
            }
        }

        if (arrayILIndex == null)
            throw new TranspileException("Array element access with complex index: could not find array variable in IL.", MethodName);

        var arrayInstr = Instructions![arrayILIndex.Value];
        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
        Local? arrayLocal;
        if (arrayLocalIdx != null)
        {
            arrayLocal = Locals[arrayLocalIdx.Value];
        }
        else
        {
            // Try static field array (ldsfld pattern)
            arrayLocal = TryResolveArrayLocal(arrayInstr);
            if (arrayLocal == null)
                throw new TranspileException("Array element access requires the array to be stored in a local variable or static field.", MethodName);
        }

        // Remove the array-load instructions from the block buffer.
        // The block count at the array IL offset tells us where the array-load instructions
        // start, and the block count at the next IL instruction tells us where they end.
        int arrayILOffset = arrayInstr.Offset;
        int nextILIndex = arrayILIndex.Value + 1;
        if (nextILIndex < Instructions.Length)
        {
            int nextILOffset = Instructions[nextILIndex].Offset;
            if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int bcArray) &&
                _blockCountAtILOffset.TryGetValue(nextILOffset, out int bcIndexStart))
            {
                int arrayLoadCount = bcIndexStart - bcArray;
                var block = CurrentBlock!;

                // Preserve operand-saving instructions that keep a prior runtime
                // value alive across the complex index computation. WriteLdloc
                // emits STA TEMP when _runtimeValueInA is true, or JSR pusha/pushax
                // when LastLDA is true, before loading the array reference. Removing
                // these would break downstream arithmetic handlers that expect the
                // first operand to be in TEMP or on the runtime stack.
                if (arrayLoadCount > 0 && block.Count > bcArray)
                {
                    var firstInstr = block[bcArray];
                    if (firstInstr.Opcode == Opcode.STA && firstInstr.Mode == AddressMode.ZeroPage
                        && firstInstr.Operand is ImmediateOperand imm && imm.Value == (byte)NESConstants.TEMP)
                    {
                        bcArray++;
                        arrayLoadCount--;
                    }
                    else if (firstInstr.Opcode == Opcode.JSR
                        && firstInstr.Operand is LabelOperand saveLabel
                        && (saveLabel.Label == "pusha" || saveLabel.Label == "pushax"))
                    {
                        bcArray++;
                        arrayLoadCount--;
                    }
                }

                // Remove array-load instructions from the block (they are dead code
                // since the index computation overwrites A anyway, but for ROM arrays
                // they include JSR pushax which has stack side effects).
                for (int i = 0; i < arrayLoadCount && block.Count > bcArray; i++)
                    block.RemoveAt(bcArray);

                // Check if the instruction now at bcArray is a JSR pusha/pushax that was
                // emitted by the first index instruction because the array load set LastLDA.
                // This happens for ROM arrays where WriteLdloc ends with LDA #imm.
                if (block.Count > bcArray)
                {
                    var afterRemoval = block[bcArray];
                    if (afterRemoval.Opcode == Opcode.JSR
                        && afterRemoval.Operand is LabelOperand label
                        && (label.Label == "pusha" || label.Label == "pushax"))
                    {
                        block.RemoveAt(bcArray);
                    }
                }
            }
        }

        // The computed index is in A from the preceding arithmetic operations.
        // Transfer it to X and load the array element.
        Emit(Opcode.TAX, AddressMode.Implied);

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
            throw new TranspileException("Array element access failed: the array variable has no allocated address. Ensure the array is initialized before use.", MethodName);
        }

        Stack.Push(0);
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
        Local? targetArrayFromField = null; // For ldsfld array sources
        int targetIndexLocalIdx = -1;
        ushort? targetIndexFromField = null; // For ldsfld index sources
        int constantIndex = -1; // For constant-index stores like actor_dx[0] = 254
        int indexAddend = 0;    // For index arithmetic like buf[col + 1]
        int indexLocalAddend = -1; // For index arithmetic like buf[offset + j]
        int targetArrayILOffset = -1;

        // Array-element index: buf[arr2[j] * N + M] = value
        int indexArrayLocalIdx = -1;  // The array used in the index expression (arr2)
        int indexSubLocalIdx = -1;    // The sub-index into that array (j) — local variable
        int indexSubConstant = -1;    // The sub-index as a constant (when compiler folds j)
        int indexArrayMul = 0;        // Multiplier (N)
        int indexArrayAdd = 0;        // Addend (M)

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
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                case ILOpCode.Dup:
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
                case ILOpCode.Stloc_0: case ILOpCode.Stloc_1: case ILOpCode.Stloc_2: case ILOpCode.Stloc_3:
                case ILOpCode.Stloc_s: case ILOpCode.Stloc:
                case ILOpCode.Pop:
                    pop = 1; break;
                case ILOpCode.Bne_un_s: case ILOpCode.Bne_un:
                case ILOpCode.Beq_s: case ILOpCode.Beq:
                case ILOpCode.Bge_s: case ILOpCode.Bge: case ILOpCode.Bge_un_s: case ILOpCode.Bge_un:
                case ILOpCode.Bgt_s: case ILOpCode.Bgt: case ILOpCode.Bgt_un_s: case ILOpCode.Bgt_un:
                case ILOpCode.Ble_s: case ILOpCode.Ble: case ILOpCode.Ble_un_s: case ILOpCode.Ble_un:
                case ILOpCode.Blt_s: case ILOpCode.Blt: case ILOpCode.Blt_un_s: case ILOpCode.Blt_un:
                    pop = 2; break; // compare-and-branch pops 2 values (comparison operands)
                case ILOpCode.Brfalse_s: case ILOpCode.Brfalse:
                case ILOpCode.Brtrue_s: case ILOpCode.Brtrue:
                    pop = 1; break; // test-and-branch pops 1
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
                // This instruction is the array ldloc or ldsfld (first push)
                var locIdx = GetLdlocIndex(il);
                if (locIdx != null)
                {
                    targetArrayLocalIdx = locIdx.Value;
                }
                else
                {
                    targetArrayFromField = TryResolveArrayLocal(il);
                }

                if (locIdx != null || targetArrayFromField != null)
                {
                    if (locIdx != null)
                        targetArrayLocalIdx = locIdx.Value;
                    targetArrayILOffset = il.Offset;
                    
                    // The next instruction should be the index (ldloc, ldsfld, or constant)
                    if (i + 1 < Index)
                    {
                        var nextIl = Instructions[i + 1];
                        var nextLocIdx = GetLdlocIndex(nextIl);
                        if (nextLocIdx != null)
                        {
                            targetIndexLocalIdx = nextLocIdx.Value;
                            valueStart = i + 2;

                            // Check if the "index" local is actually an array.
                            // Pattern: buf[arr2[j] * N + M] = value
                            // IL: ldloc arr2, ldloc j, ldelem.u1, [ldc N, mul], [ldc M, add], [conv]
                            if (Locals.TryGetValue(targetIndexLocalIdx, out var indexAsArray) && indexAsArray.ArraySize > 0)
                            {
                                indexArrayLocalIdx = targetIndexLocalIdx;
                                targetIndexLocalIdx = -1; // not a simple scalar index

                                int scan = valueStart;
                                // ldloc j (the sub-index) — or ldc N if compiler constant-folded
                                if (scan < Index)
                                {
                                    var subIdx = GetLdlocIndex(Instructions[scan]);
                                    if (subIdx != null)
                                    {
                                        indexSubLocalIdx = subIdx.Value;
                                        scan++;
                                    }
                                    else
                                    {
                                        int? subConst = GetLdcValue(Instructions[scan]);
                                        if (subConst != null)
                                        {
                                            indexSubConstant = subConst.Value;
                                            scan++;
                                        }
                                    }
                                }
                                // ldelem.u1
                                if (scan < Index && Instructions[scan].OpCode == ILOpCode.Ldelem_u1)
                                    scan++;
                                // [ldc N, mul]
                                if (scan + 1 < Index)
                                {
                                    int? mulConst = GetLdcValue(Instructions[scan]);
                                    if (mulConst != null && Instructions[scan + 1].OpCode == ILOpCode.Mul)
                                    {
                                        indexArrayMul = mulConst.Value;
                                        scan += 2;
                                    }
                                }
                                // [ldc M, add]
                                if (scan + 1 < Index)
                                {
                                    int? addConst = GetLdcValue(Instructions[scan]);
                                    if (addConst != null && Instructions[scan + 1].OpCode == ILOpCode.Add)
                                    {
                                        indexArrayAdd = addConst.Value;
                                        scan += 2;
                                    }
                                }
                                // [conv.u1/conv.u2/conv.i1]
                                if (scan < Index && Instructions[scan].OpCode
                                    is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1)
                                    scan++;

                                valueStart = scan;
                            }
                            // Check for index arithmetic: ldloc idx, ldc N, add, [conv]
                            // Pattern: buf[(byte)(col + 1)] → ldloc col, ldc 1, add, conv.u1
                            else if (valueStart + 1 < Index)
                            {
                                int? addend = GetLdcValue(Instructions[valueStart]);
                                if (addend != null && Instructions[valueStart + 1].OpCode == ILOpCode.Add)
                                {
                                    indexAddend = addend.Value;
                                    valueStart += 2; // skip ldc + add
                                    // Skip conv.u1/conv.u2 if present
                                    if (valueStart < Index && Instructions[valueStart].OpCode
                                        is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1)
                                        valueStart++;
                                }
                                else
                                {
                                    // Check for ldloc + add pattern: buf[(byte)(offset + j)]
                                    int? secondLocalIdx = GetLdlocIndex(Instructions[valueStart]);
                                    if (secondLocalIdx != null && Instructions[valueStart + 1].OpCode == ILOpCode.Add)
                                    {
                                        indexLocalAddend = secondLocalIdx.Value;
                                        valueStart += 2; // skip ldloc + add
                                        if (valueStart < Index && Instructions[valueStart].OpCode
                                            is ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i1)
                                            valueStart++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Check for constant index (ldc_i4_0, ldc_i4_1, etc.)
                            int? constIdx = GetLdcValue(nextIl);
                            if (constIdx != null)
                            {
                                constantIndex = constIdx.Value;
                            }
                            else if (nextIl.OpCode == ILOpCode.Ldsfld && nextIl.String != null)
                            {
                                // Static field index (e.g., G.i)
                                var sfLoc = TryResolveArrayLocal(nextIl);
                                if (sfLoc?.Address != null)
                                    targetIndexFromField = (ushort)sfLoc.Address;
                            }
                            valueStart = i + 2;
                        }
                    }
                    else
                    {
                        valueStart = i + 2;
                    }
                }
                break;
            }
        }

        if (targetArrayLocalIdx < 0 && targetArrayFromField == null)
        {
            // Can't identify target array at all
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            _lastLoadedLocalIndex = null;
            return;
        }

        // Resolve the target array Local from either local variable or static field
        Local resolvedTargetArray = targetArrayLocalIdx >= 0 ? Locals[targetArrayLocalIdx] : targetArrayFromField!;

        // Handle array-element index: buf[arr2[j] * N + M] = value
        // Pattern: floor_objpos[f] * 2, floor_objpos[f] * 2 + 1, etc.
        if (indexArrayLocalIdx >= 0 && resolvedTargetArray.Address is not null)
        {
            var indexArray = Locals[indexArrayLocalIdx];
            if (indexArray.Address is null || (indexSubLocalIdx < 0 && indexSubConstant < 0))
            {
                _runtimeValueInA = false;
                _savedRuntimeToTemp = false;
                _lastLoadedLocalIndex = null;
                return;
            }

            // Remove previously emitted instructions
            if (_blockCountAtILOffset.TryGetValue(targetArrayILOffset, out int blockCountArr))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountArr;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }

            // Analyze the value expression to determine what to store
            int? valueConst = null;
            int valueLocIdx = -1;
            bool valHasAdd = false;
            int valAddValue = 0;
            for (int i = valueStart; i < Index; i++)
            {
                var il = Instructions[i];
                int? val = GetLdcValue(il);
                if (val != null)
                {
                    if (i + 1 < Index && Instructions[i + 1].OpCode == ILOpCode.Add)
                        valAddValue = val.Value;
                    else
                        valueConst = val;
                }
                if (il.OpCode == ILOpCode.Add)
                    valHasAdd = true;
                var locIdx = GetLdlocIndex(il);
                if (locIdx != null)
                {
                    // Check it's not part of an array ldelem pattern
                    bool isLdelemPart = i + 1 < Index && Instructions[i + 1].OpCode == ILOpCode.Ldelem_u1;
                    if (!isLdelemPart)
                        valueLocIdx = locIdx.Value;
                }
            }

            // Compute the index: arr2[j] * N + M
            if (indexSubLocalIdx >= 0)
            {
                var subLocal = Locals[indexSubLocalIdx];
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)subLocal.Address!);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)indexArray.Address!);
            }
            else
            {
                // Constant sub-index: load directly from arr2[constant]
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)(indexArray.Address! + indexSubConstant));
            }
            Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)indexArray.Address!);
            if (indexArrayMul > 0)
            {
                int shifts = 0;
                for (int v = indexArrayMul; v > 1; v >>= 1) shifts++;
                for (int s = 0; s < shifts; s++)
                    Emit(Opcode.ASL, AddressMode.Accumulator);
            }
            if (indexArrayAdd != 0)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)indexArrayAdd));
            }
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);

            // Load the value to store
            if (valueLocIdx >= 0)
            {
                var valueLoc = Locals[valueLocIdx];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valueLoc.Address!);
                if (valHasAdd && valAddValue != 0)
                {
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)valAddValue));
                }
            }
            else if (valueConst != null)
            {
                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)valueConst.Value));
            }

            // Store: LDX TEMP; STA buf,X
            Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STA, AddressMode.AbsoluteX, (ushort)resolvedTargetArray.Address);

            _immediateInA = null;
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            return;
        }

        // Handle expression-based index: buf[expr] = constValue
        // Pattern: call+and, call, etc. as index expression
        if (targetIndexLocalIdx < 0 && constantIndex < 0 && targetIndexFromField == null)
        {
            var targetArray2 = resolvedTargetArray;
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

        var targetArray = resolvedTargetArray;

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
            int? storeValueLocalIdx = null;
            for (int i = valueStart; i < Index; i++)
            {
                int? val = GetLdcValue(Instructions[i]);
                if (val != null)
                    constValue = val;
                int? locIdx = GetLdlocIndex(Instructions[i]);
                if (locIdx != null)
                    storeValueLocalIdx = locIdx;
            }

            // Check for ushort high/low byte extraction: (byte)(ushort_local >> 8) or (byte)ushort_local
            bool isUshortExtraction = false;
            if (storeValueLocalIdx != null &&
                Locals.TryGetValue(storeValueLocalIdx.Value, out var ushortLocal) &&
                ushortLocal.IsWord && ushortLocal.Address.HasValue)
            {

                // Pattern: ldloc(ushort), ldc_i4_8, shr, [conv_u1] → high byte
                bool hasShr8 = false;
                for (int i = valueStart; i < Index; i++)
                {
                    if (Instructions[i].OpCode is ILOpCode.Shr or ILOpCode.Shr_un)
                    {
                        // Check if the preceding ldc was 8
                        if (i > valueStart)
                        {
                            int? shrVal = GetLdcValue(Instructions[i - 1]);
                            if (shrVal == 8)
                                hasShr8 = true;
                        }
                    }
                }

                if (hasShr8)
                {
                    // (byte)(ushort_local >> 8): load high byte directly
                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)(ushortLocal.Address.Value + 1));
                    isUshortExtraction = true;
                }
                else
                {
                    // Check if this is a simple cast: ldloc(ushort), conv_u1 → low byte
                    bool hasOnlyConv = true;
                    for (int i = valueStart; i < Index; i++)
                    {
                        var op = Instructions[i].OpCode;
                        if (op != ILOpCode.Conv_u1 && GetLdlocIndex(Instructions[i]) == null)
                        {
                            hasOnlyConv = false;
                            break;
                        }
                    }
                    if (hasOnlyConv)
                    {
                        Emit(Opcode.LDA, AddressMode.Absolute, (ushort)ushortLocal.Address.Value);
                        isUshortExtraction = true;
                    }
                }
            }

            if (!isUshortExtraction)
            {
                if (constValue != null)
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)constValue.Value));
                }
                else if (storeValueLocalIdx != null &&
                         Locals.TryGetValue(storeValueLocalIdx.Value, out var valueLocal) &&
                         valueLocal.Address.HasValue)
                {
                    // Value is a runtime local variable — load it
                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valueLocal.Address.Value);
                }
            }

            Emit(Opcode.STA, AddressMode.Absolute, (ushort)(targetArray.Address + constantIndex));

            _immediateInA = null;
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            return;
        }

        ushort targetIndexAddr;
        if (targetIndexLocalIdx >= 0)
        {
            var targetIndex = Locals[targetIndexLocalIdx];
            if (targetIndex.Address is null)
                throw new InvalidOperationException("Stelem_i1: index local has no address");
            targetIndexAddr = (ushort)targetIndex.Address;
        }
        else if (targetIndexFromField != null)
        {
            targetIndexAddr = targetIndexFromField.Value;
        }
        else
        {
            throw new InvalidOperationException("Stelem_i1: no index local or static field address");
        }

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
        bool hasShr = false;
        int shrValue = 0;
        bool hasTwoLdelems = false;
        int sourceArray1Idx = -1;
        int sourceIndex1Idx = -1;
        int sourceArray2Idx = -1;
        int valueLocalIdx = -1;
        int valueLocalIdx2 = -1; // second local for ldloc+ldloc+add pattern

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
                case ILOpCode.Shr:
                case ILOpCode.Shr_un:
                    hasShr = true;
                    break;
                case ILOpCode.Ldelem_u1:
                    if (sourceArray1Idx < 0)
                    {
                        // Find the index local (immediately before ldelem) and the array local
                        if (i > valueStart)
                        {
                            var idxLocal = GetLdlocIndex(Instructions[i - 1]);
                            if (idxLocal != null && Locals.TryGetValue(idxLocal.Value, out var idxLoc) && idxLoc.ArraySize == 0)
                                sourceIndex1Idx = idxLocal.Value;
                        }
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
                            {
                                if (valueLocalIdx < 0)
                                    valueLocalIdx = locIdx.Value;
                                else if (valueLocalIdx2 < 0)
                                    valueLocalIdx2 = locIdx.Value; // track second local
                            }
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
                            else if (Instructions[i + 1].OpCode is ILOpCode.Shr or ILOpCode.Shr_un)
                                shrValue = val;
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
                            else if (Instructions[i + 1].OpCode is ILOpCode.Shr or ILOpCode.Shr_un)
                                shrValue = val;
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
            
            Emit(Opcode.LDX, AddressMode.Absolute, targetIndexAddr);
            if (indexAddend != 0 || indexLocalAddend >= 0)
            {
                RemoveLastInstructions(1); // remove the LDX
                Emit(Opcode.LDA, AddressMode.Absolute, targetIndexAddr);
                Emit(Opcode.CLC, AddressMode.Implied);
                if (indexAddend != 0)
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)indexAddend));
                else
                    Emit(Opcode.ADC, AddressMode.Absolute, (ushort)Locals[indexLocalAddend].Address!);
                Emit(Opcode.TAX, AddressMode.Implied);
            }
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
        else if (sourceArray1Idx >= 0 && !hasTwoLdelems && sourceArray1Idx == targetArrayLocalIdx
            && (hasSub || hasAdd || hasAnd))
        {
            // Pattern: arr[i] = arr[i] ± N or arr[i] = arr[i] & N (self-referencing update)
            // ldloc arr, ldloc idx, ldloc arr, ldloc idx, ldelem_u1, ldc N, sub/add/and, conv_u1, stelem_i1
            Emit(Opcode.LDX, AddressMode.Absolute, targetIndexAddr);
            Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)targetArray.Address!);
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
            Emit(Opcode.STA, AddressMode.AbsoluteX, (ushort)targetArray.Address!);

            _immediateInA = null;
            _lastLoadedLocalIndex = null;
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            return;
        }
        else if (sourceArray1Idx >= 0 && !hasTwoLdelems && sourceArray1Idx != targetArrayLocalIdx
            && sourceIndex1Idx >= 0)
        {
            // Pattern: arr1[i] = arr2[j] — cross-array copy with possibly different indices
            var srcArray = Locals[sourceArray1Idx];
            var srcIndex = Locals[sourceIndex1Idx];
            if (srcArray.Address.HasValue && srcIndex.Address.HasValue)
            {
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)srcIndex.Address!);
                Emit(Opcode.LDA, AddressMode.AbsoluteX, (ushort)srcArray.Address!);
            }
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
        else if (valueLocalIdx >= 0 && valueLocalIdx2 >= 0 && (hasAdd != hasSub))
        {
            // Pattern: arr[i] = (byte)(local1 + local2) or arr[i] = (byte)(local1 - local2)
            var loc1 = Locals[valueLocalIdx];
            var loc2 = Locals[valueLocalIdx2];
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)loc1.Address!);
            if (hasAdd)
            {
                Emit(Opcode.CLC, AddressMode.Implied);
                Emit(Opcode.ADC, AddressMode.Absolute, (ushort)loc2.Address!);
            }
            else
            {
                Emit(Opcode.SEC, AddressMode.Implied);
                Emit(Opcode.SBC, AddressMode.Absolute, (ushort)loc2.Address!);
            }
        }
        else if (valueLocalIdx >= 0)
        {
            // Pattern: arr[i] = local, arr[i] = (local & N), arr[i] = (local + N), etc.
            var valueLoc = Locals[valueLocalIdx];

            // Handle ushort high byte extraction: arr[i] = (byte)(ushort_local >> 8)
            if (hasShr && shrValue == 8 && valueLoc.IsWord)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)(valueLoc.Address! + 1));
            }
            else if (hasShr && shrValue > 0)
            {
                // General right shift: load value, then LSR N times
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valueLoc.Address!);
                for (int s = 0; s < shrValue; s++)
                    Emit(Opcode.LSR, AddressMode.Accumulator);
            }
            else
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valueLoc.Address!);
            }

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
            if (indexAddend != 0 || indexLocalAddend >= 0)
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP); // save value
                Emit(Opcode.LDA, AddressMode.Absolute, targetIndexAddr);
                Emit(Opcode.CLC, AddressMode.Implied);
                if (indexAddend != 0)
                    Emit(Opcode.ADC, AddressMode.Immediate, checked((byte)indexAddend));
                else
                    Emit(Opcode.ADC, AddressMode.Absolute, (ushort)Locals[indexLocalAddend].Address!);
                Emit(Opcode.TAX, AddressMode.Implied);
                Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP); // restore value
            }
            else
            {
                Emit(Opcode.LDX, AddressMode.Absolute, targetIndexAddr);
            }
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
            throw new TranspileException("Array element assignment failed: the array variable has no allocated address. Ensure the array is initialized before use.", MethodName);
        }

        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = false;
        _savedRuntimeToTemp = false;
    }

    // ── Ushort array handling ───────────────────────────────────────────

    /// <summary>
    /// Handles ldelem.u2: loads a 16-bit value from a ushort[] array.
    /// Pattern: Ldloc_N (array), Ldloc_M (index), Ldelem_u2
    /// Constant index: LDA base+i*2; LDX base+i*2+1
    /// Variable index: LDA idx; ASL A; TAY; LDA base+1,Y; TAX; LDA base,Y
    /// Result in A:X (lo:hi), sets _ushortInAX.
    /// </summary>
    void HandleLdelemU2()
    {
        if (Instructions is null || Index < 1)
            throw new InvalidOperationException("HandleLdelemU2 requires at least 1 previous instruction");

        if (Stack.Count > 0) Stack.Pop(); // index
        if (Stack.Count > 0) Stack.Pop(); // array ref

        var indexInstr = Instructions[Index - 1];
        int? indexLocalIdx = GetLdlocIndex(indexInstr);
        int? constantIndex = indexLocalIdx == null ? GetLdcValue(indexInstr) : null;

        // Determine the array base address
        ushort arrayBase;
        int removeFromILOffset;

        if (_pendingUshortArrayBase is not null)
        {
            // Array ref is implicit on eval stack (never stored to local — Release compiler pattern).
            // Only the index instruction precedes ldelem.u2.
            arrayBase = _pendingUshortArrayBase.Value;
            removeFromILOffset = indexInstr.Offset;
        }
        else if (Index >= 2)
        {
            var arrayInstr = Instructions[Index - 2];
            int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
            Local? arrayLocalFromField = arrayLocalIdx == null ? TryResolveArrayLocal(arrayInstr) : null;

            if (arrayLocalIdx != null)
            {
                var arrayLocal = Locals[arrayLocalIdx.Value];
                if (arrayLocal.Address is null)
                    throw new TranspileException("Ushort array element access failed: the array variable has no allocated address.", MethodName);
                arrayBase = (ushort)arrayLocal.Address;
            }
            else if (arrayLocalFromField?.Address is not null)
            {
                arrayBase = (ushort)arrayLocalFromField.Address;
            }
            else
            {
                throw new TranspileException("Ushort array element access requires the array to be stored in a local variable or static field.", MethodName);
            }
            removeFromILOffset = arrayInstr.Offset;
        }
        else
        {
            throw new TranspileException("Ushort array element access requires the array to be stored in a local variable or static field.", MethodName);
        }

        // Remove previously emitted instructions from WriteLdloc/WriteLdc
        if (_blockCountAtILOffset.TryGetValue(removeFromILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
            {
                RemoveLastInstructions(instrToRemove);
                _savedRuntimeToTemp = false;
            }
        }

        Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;

        if (constantIndex != null)
        {
            // Compile-time element address
            ushort elemAddr = (ushort)(arrayBase + constantIndex.Value * 2);
            Emit(Opcode.LDA, AddressMode.Absolute, elemAddr);           // lo byte
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(elemAddr + 1)); // hi byte
        }
        else
        {
            // Variable index: compute byte offset = index * 2, use Y register
            if (indexLocal?.Address is not null)
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)indexLocal.Address);
            else
                throw new TranspileException("Ushort array element access requires the index to be a constant or local variable.", MethodName);

            Emit(Opcode.ASL, AddressMode.Accumulator);                   // A = index * 2
            Emit(Opcode.TAY, AddressMode.Implied);                       // Y = byte offset
            Emit(Opcode.LDA, AddressMode.AbsoluteY, (ushort)(arrayBase + 1)); // hi byte first
            Emit(Opcode.TAX, AddressMode.Implied);                       // X = hi byte
            Emit(Opcode.LDA, AddressMode.AbsoluteY, arrayBase);          // A = lo byte
        }

        Stack.Push(0);
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = true;
        _ushortInAX = true;
    }

    /// <summary>
    /// Handles stelem.i2: stores a 16-bit value into a ushort[] array.
    /// IL stack: array_ref, index, value → (empty)
    /// For the value, handles:
    /// - Immediate constant (from ldc)
    /// - Runtime ushort in A:X (from ldloc of word local, arithmetic, function call)
    /// - Runtime byte in A (zero-extends hi byte)
    /// </summary>
    void HandleStelemI2()
    {
        if (Instructions is null)
            throw new InvalidOperationException("HandleStelemI2 requires Instructions");

        if (Stack.Count >= 3) { Stack.Pop(); Stack.Pop(); Stack.Pop(); }
        else Stack.Clear();

        ushort arrayBase;
        int? constantIndex = null;
        int? indexLocalIdx = null;
        int valueStart = -1;
        int pendingIndexILOffset = -1; // IL offset for deferred removal in pending path

        if (_pendingUshortArrayBase is not null)
        {
            // Array ref is on the eval stack from newarr/dup chain (before stloc).
            // Only need to find index + value (2 stack values, not 3).
            arrayBase = _pendingUshortArrayBase.Value;

            int depth = 0;
            int indexIdx = -1;
            for (int i = Index - 1; i >= 0; i--)
            {
                var il = Instructions[i];
                int push = 0, pop = 0;
                switch (il.OpCode)
                {
                    case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1: case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                    case ILOpCode.Ldloc_s:
                    case ILOpCode.Ldsfld:
                    case ILOpCode.Ldc_i4_m1:
                    case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                    case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                        push = 1; break;
                    case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.Mul: case ILOpCode.Div:
                    case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Shr: case ILOpCode.Shr_un: case ILOpCode.Shl:
                        pop = 2; push = 1; break;
                    case ILOpCode.Conv_u1: case ILOpCode.Conv_u2: case ILOpCode.Conv_u4:
                    case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
                        break;
                    default:
                        push = 1; break;
                }
                depth += push - pop;
                if (depth >= 2)
                {
                    indexIdx = i;
                    break;
                }
            }

            if (indexIdx < 0)
                throw new TranspileException("stelem.i2: could not locate index for pending array pattern.", MethodName);

            var indexInstr = Instructions[indexIdx];
            constantIndex = GetLdcValue(indexInstr);
            indexLocalIdx = GetLdlocIndex(indexInstr);
            valueStart = indexIdx + 1;
            pendingIndexILOffset = indexInstr.Offset;

            // Defer removal to after value type determination — runtime values
            // need the previously emitted 6502 code that computed A:X.
        }
        else
        {
            // Array stored in a local — walk backward for all 3 values
            int depth = 0;
            for (int i = Index - 1; i >= 0; i--)
            {
                var il = Instructions[i];
                int push = 0, pop = 0;
                switch (il.OpCode)
                {
                    case ILOpCode.Ldloc_0: case ILOpCode.Ldloc_1: case ILOpCode.Ldloc_2: case ILOpCode.Ldloc_3:
                    case ILOpCode.Ldloc_s:
                    case ILOpCode.Ldsfld:
                    case ILOpCode.Ldc_i4_m1:
                    case ILOpCode.Ldc_i4_0: case ILOpCode.Ldc_i4_1: case ILOpCode.Ldc_i4_2: case ILOpCode.Ldc_i4_3:
                    case ILOpCode.Ldc_i4_4: case ILOpCode.Ldc_i4_5: case ILOpCode.Ldc_i4_6: case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8: case ILOpCode.Ldc_i4_s: case ILOpCode.Ldc_i4:
                        push = 1; break;
                    case ILOpCode.Add: case ILOpCode.Sub: case ILOpCode.Mul: case ILOpCode.Div:
                    case ILOpCode.And: case ILOpCode.Or: case ILOpCode.Shr: case ILOpCode.Shr_un: case ILOpCode.Shl:
                        pop = 2; push = 1; break;
                    case ILOpCode.Conv_u1: case ILOpCode.Conv_u2: case ILOpCode.Conv_u4:
                    case ILOpCode.Conv_i1: case ILOpCode.Conv_i2: case ILOpCode.Conv_i4:
                        break;
                    case ILOpCode.Call:
                        push = 1; break;
                    default:
                        push = 1; break;
                }
                depth += push - pop;
                if (depth >= 3)
                {
                    valueStart = i + 2;
                    break;
                }
            }

            if (valueStart < 2)
                throw new TranspileException("stelem.i2: could not locate array/index/value boundary in IL.", MethodName);

            var arrayInstr = Instructions[valueStart - 2];
            var indexInstr = Instructions[valueStart - 1];

            int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
            Local? arrayLocalFromField = null;
            if (arrayLocalIdx == null)
                arrayLocalFromField = TryResolveArrayLocal(arrayInstr);

            if (arrayLocalIdx != null)
            {
                var arrayLocal = Locals[arrayLocalIdx.Value];
                if (arrayLocal.Address is null)
                    throw new TranspileException("stelem.i2: array has no address.", MethodName);
                arrayBase = (ushort)arrayLocal.Address;
            }
            else if (arrayLocalFromField?.Address is not null)
            {
                arrayBase = (ushort)arrayLocalFromField.Address;
            }
            else
            {
                throw new TranspileException($"stelem.i2: could not resolve array local (arrayInstr={arrayInstr.OpCode}).", MethodName);
            }

            constantIndex = GetLdcValue(indexInstr);
            indexLocalIdx = GetLdlocIndex(indexInstr);

            // Remove previously emitted instructions
            int arrayILOffset = arrayInstr.Offset;
            if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
                if (instrToRemove > 0)
                {
                    RemoveLastInstructions(instrToRemove);
                    _savedRuntimeToTemp = false;
                }
            }
        }

        // Determine the value expression type
        // Check if value is a simple constant
        int? valueConstant = null;
        bool valueIsWordLocal = false;
        int? valueLocalIdx = null;
        bool valueIsRuntimeUshort = false;

        if (valueStart == Index)
        {
            // Single instruction value: could be ldc or ldloc
            // Actually it's the instruction right before stelem
        }

        var valueInstr = Instructions[Index - 1];
        // If the value is preceded by conv.u2, look one more back
        if (valueInstr.OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2 && Index - 2 >= valueStart)
            valueInstr = Instructions[Index - 2];

        if (valueInstr.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.And or ILOpCode.Or)
        {
            // Runtime computed value — already in A (or A:X if ushort arithmetic)
            valueIsRuntimeUshort = _ushortInAX;
        }
        else
        {
            int? valLdcValue = GetLdcValue(valueInstr);
            if (valLdcValue != null)
            {
                valueConstant = valLdcValue;
            }
            else
            {
                int? valLocalIdx = GetLdlocIndex(valueInstr);
                if (valLocalIdx != null && Locals.TryGetValue(valLocalIdx.Value, out var valLocal))
                {
                    valueLocalIdx = valLocalIdx;
                    valueIsWordLocal = valLocal.IsWord;
                }
                else if (valueInstr.OpCode == ILOpCode.Call)
                {
                    valueIsRuntimeUshort = _ushortInAX;
                }
            }
        }

        // Deferred removal for pending path: only remove emitted code when
        // the value is constant (runtime values need the already-emitted computation).
        if (pendingIndexILOffset >= 0 && (valueConstant != null || (valueIsWordLocal && valueLocalIdx != null)))
        {
            if (_blockCountAtILOffset.TryGetValue(pendingIndexILOffset, out int blockCountAtIdx))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCountAtIdx;
                if (instrToRemove > 0)
                {
                    RemoveLastInstructions(instrToRemove);
                    _savedRuntimeToTemp = false;
                }
            }
        }

        // Now emit 6502 code
        if (constantIndex != null)
        {
            ushort elemAddr = (ushort)(arrayBase + constantIndex.Value * 2);

            if (valueConstant != null)
            {
                // Constant value, constant index
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(valueConstant.Value & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, elemAddr);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)((valueConstant.Value >> 8) & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(elemAddr + 1));
            }
            else if (valueIsWordLocal && valueLocalIdx != null)
            {
                // Word local value, constant index
                var valLocal = Locals[valueLocalIdx.Value];
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)valLocal.Address!);
                Emit(Opcode.STA, AddressMode.Absolute, elemAddr);
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)(valLocal.Address! + 1));
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(elemAddr + 1));
            }
            else
            {
                // Runtime value in A:X (or just A for byte), constant index
                Emit(Opcode.STA, AddressMode.Absolute, elemAddr);
                if (valueIsRuntimeUshort || _ushortInAX)
                    Emit(Opcode.STX, AddressMode.Absolute, (ushort)(elemAddr + 1));
                else
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
                    Emit(Opcode.STA, AddressMode.Absolute, (ushort)(elemAddr + 1));
                }
            }
        }
        else
        {
            // Variable index: need Y register for byte offset
            Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;
            if (indexLocal?.Address is null)
                throw new TranspileException("stelem.i2: variable index requires a local variable.", MethodName);

            if (valueConstant != null)
            {
                // Constant value, variable index
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)indexLocal.Address);
                Emit(Opcode.ASL, AddressMode.Accumulator);
                Emit(Opcode.TAY, AddressMode.Implied);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(valueConstant.Value & 0xFF));
                Emit(Opcode.STA, AddressMode.AbsoluteY, arrayBase);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)((valueConstant.Value >> 8) & 0xFF));
                Emit(Opcode.STA, AddressMode.AbsoluteY, (ushort)(arrayBase + 1));
            }
            else
            {
                // Runtime value in A:X, variable index
                // Save A:X to TEMP, compute Y offset, store both bytes
                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                if (valueIsRuntimeUshort || _ushortInAX)
                    Emit(Opcode.STX, AddressMode.ZeroPage, TEMP2);
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)indexLocal.Address);
                Emit(Opcode.ASL, AddressMode.Accumulator);
                Emit(Opcode.TAY, AddressMode.Implied);
                Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                Emit(Opcode.STA, AddressMode.AbsoluteY, arrayBase);
                if (valueIsRuntimeUshort || _ushortInAX)
                {
                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
                    Emit(Opcode.STA, AddressMode.AbsoluteY, (ushort)(arrayBase + 1));
                }
                else
                {
                    Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
                    Emit(Opcode.STA, AddressMode.AbsoluteY, (ushort)(arrayBase + 1));
                }
            }
        }

        _immediateInA = null;
        _lastLoadedLocalIndex = null;
        _runtimeValueInA = false;
        _ushortInAX = false;
        _savedRuntimeToTemp = false;
    }

    /// <summary>
    /// Handles ldelema System.UInt16: sets up array+index addressing for compound
    /// ushort array assignments (arr[i]++, arr[i] += expr, etc.).
    /// The subsequent dup/ldind.u2/.../stind.i2 sequence uses the pending state
    /// to emit LDA/STA with AbsoluteY addressing.
    /// </summary>
    void HandleLdelemaUshort()
    {
        // Pop index and array ref from IL evaluation stack
        if (Stack.Count > 0) Stack.Pop(); // index
        if (Stack.Count > 0) Stack.Pop(); // array ref

        if (Instructions == null || Index < 2)
            throw new InvalidOperationException("ldelema System.UInt16 requires at least 2 preceding instructions");

        var indexInstr = Instructions[Index - 1];
        var arrayInstr = Instructions[Index - 2];

        int? arrayLocalIdx = GetLdlocIndex(arrayInstr);
        Local? arrayLocalFromField = null;
        if (arrayLocalIdx == null)
            arrayLocalFromField = TryResolveArrayLocal(arrayInstr);

        if (arrayLocalIdx == null && arrayLocalFromField == null)
            throw new TranspileException("Compound ushort array assignment requires the array to be stored in a local variable or static field.", MethodName);

        var arrayLocal = arrayLocalIdx != null ? Locals[arrayLocalIdx.Value] : arrayLocalFromField!;
        if (arrayLocal.Address == null)
            throw new TranspileException("Compound ushort array assignment failed: the array variable has no allocated address.", MethodName);
        ushort arrayBase = (ushort)arrayLocal.Address;

        int? constantIndex = GetLdcValue(indexInstr);
        int? indexLocalIdx = GetLdlocIndex(indexInstr);

        // Remove previously emitted LDA instructions from WriteLdloc/WriteLdc
        int arrayILOffset = arrayInstr.Offset;
        if (_blockCountAtILOffset.TryGetValue(arrayILOffset, out int blockCountAtArray))
        {
            int instrToRemove = GetBufferedBlockCount() - blockCountAtArray;
            if (instrToRemove > 0)
            {
                RemoveLastInstructions(instrToRemove);
                _savedRuntimeToTemp = false;
            }
        }

        if (constantIndex != null)
        {
            if (constantIndex.Value == 0)
            {
                // Constant index 0: use direct Absolute addressing
                _pendingUshortArrayElement = new PendingUshortArrayElement(arrayBase, arrayBase);
            }
            else
            {
                // Constant index N: compute element address at compile time
                ushort elemAddr = (ushort)(arrayBase + constantIndex.Value * 2);
                _pendingUshortArrayElement = new PendingUshortArrayElement(arrayBase, elemAddr);
            }
        }
        else
        {
            // Variable index: compute byte offset in Y register
            Local? indexLocal = indexLocalIdx != null ? Locals[indexLocalIdx.Value] : null;
            if (indexLocal?.Address is not null)
            {
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)indexLocal.Address);
                Emit(Opcode.ASL, AddressMode.Accumulator);     // A = index * 2
                Emit(Opcode.TAY, AddressMode.Implied);         // Y = byte offset
            }
            else
                throw new TranspileException("Compound ushort array assignment requires the index to be a constant or local variable.", MethodName);

            _pendingUshortArrayElement = new PendingUshortArrayElement(arrayBase, ConstantElementAddress: null);
        }

        // ldelema pushes a managed reference onto the eval stack
        Stack.Push(0);
        _runtimeValueInA = false;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Handles ldind.u2: load a 16-bit value through a pointer/reference.
    /// Used after ldelema System.UInt16 to read the current array element value.
    /// Result in A:X (lo:hi).
    /// </summary>
    void HandleLdindU2()
    {
        if (_pendingUshortArrayElement is not { } pending)
            throw new TranspileException("ldind.u2 without preceding ldelema System.UInt16 is not supported.", MethodName);

        // Pop the address reference from the eval stack
        if (Stack.Count > 0) Stack.Pop();

        if (pending.ConstantElementAddress is { } addr)
        {
            // Constant element address: direct load
            Emit(Opcode.LDA, AddressMode.Absolute, addr);                  // lo byte
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(addr + 1));    // hi byte
        }
        else
        {
            // Variable index: Y already holds byte offset from ldelema
            Emit(Opcode.LDA, AddressMode.AbsoluteY, (ushort)(pending.ArrayBase + 1)); // hi byte first
            Emit(Opcode.TAX, AddressMode.Implied);                                     // X = hi
            Emit(Opcode.LDA, AddressMode.AbsoluteY, pending.ArrayBase);                // A = lo
        }

        Stack.Push(0);
        _runtimeValueInA = true;
        _ushortInAX = true;
        _immediateInA = null;
        _lastLoadedLocalIndex = null;
    }

    /// <summary>
    /// Handles stind.i2: store a 16-bit value through a pointer/reference.
    /// Used after ldelema System.UInt16 to write back the modified array element.
    /// Value in A:X (lo:hi) or just A (zero-extends hi byte).
    /// </summary>
    void HandleStindI2()
    {
        if (_pendingUshortArrayElement is not { } pending)
            throw new TranspileException("stind.i2 without preceding ldelema System.UInt16 is not supported.", MethodName);

        // Pop the value and address reference from the eval stack
        if (Stack.Count > 0) Stack.Pop(); // value
        if (Stack.Count > 0) Stack.Pop(); // address

        bool isUshortValue = _ushortInAX;

        if (pending.ConstantElementAddress is { } addr)
        {
            // Constant element address
            Emit(Opcode.STA, AddressMode.Absolute, addr);                  // lo byte
            if (isUshortValue)
                Emit(Opcode.STX, AddressMode.Absolute, (ushort)(addr + 1)); // hi byte
            else
            {
                Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)(addr + 1));
            }
        }
        else
        {
            // Variable index: Y still holds byte offset from ldelema
            // Save A:X, then store both bytes using AbsoluteY
            Emit(Opcode.STA, AddressMode.AbsoluteY, pending.ArrayBase);                // lo byte
            if (isUshortValue)
            {
                Emit(Opcode.TXA, AddressMode.Implied);
                Emit(Opcode.STA, AddressMode.AbsoluteY, (ushort)(pending.ArrayBase + 1)); // hi byte
            }
            else
            {
                Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
                Emit(Opcode.STA, AddressMode.AbsoluteY, (ushort)(pending.ArrayBase + 1));
            }
        }

        _pendingUshortArrayElement = null;
        _runtimeValueInA = false;
        _ushortInAX = false;
    }

    /// <summary>
    /// Handles meta_spr_2x2 / meta_spr_2x2_flip transpiler intrinsics.
    /// Reads 5 constant arguments from the IL stack, constructs the 17-byte metasprite
    /// array, and registers it as ROM data (same as Ldtoken handler).
    /// </summary>
    void HandleMetaSpr2x2(bool flip)
    {
        // Pop 5 constants from Stack (reverse order): attr, bottomRight, topRight, bottomLeft, topLeft
        if (Stack.Count < 5)
            throw new InvalidOperationException($"{(flip ? "meta_spr_2x2_flip" : "meta_spr_2x2")} requires 5 arguments on the stack, but only {Stack.Count} found.");

        int attr = Stack.Pop();
        int bottomRight = Stack.Pop();
        int topRight = Stack.Pop();
        int bottomLeft = Stack.Pop();
        int topLeft = Stack.Pop();

        if (flip)
            attr |= 0x40; // Set horizontal flip bit

        // Remove previously emitted LDA instructions for the 5 arguments.
        // This backward scan assumes 5 consecutive ldc instructions with no interleaved
        // opcodes (nop, dup, stloc/ldloc reloads, etc.) between the constant pushes and
        // this Call. This matches the IL that current Roslyn versions emit for
        // meta_spr_2x2(const, const, const, const, const). If a future compiler version
        // interleaves other IL, the scan may misidentify which instructions to remove.
        if (Instructions != null)
        {
            int argsToFind = 5;
            int firstArgILOffset = -1;
            for (int scan = Index - 1; scan >= 0 && argsToFind > 0; scan--)
            {
                if (GetLdcValue(Instructions[scan]) != null)
                {
                    firstArgILOffset = Instructions[scan].Offset;
                    argsToFind--;
                }
            }

            if (firstArgILOffset >= 0 && _blockCountAtILOffset.TryGetValue(firstArgILOffset, out int blockCount))
            {
                int instrToRemove = GetBufferedBlockCount() - blockCount;
                if (instrToRemove > 0)
                    RemoveLastInstructions(instrToRemove);
            }
        }

        // Construct the 17-byte metasprite array
        byte[] data;
        if (flip)
        {
            // Flipped: swap L/R columns (topLeft→x=8, topRight→x=0)
            data = new byte[]
            {
                8, 0, (byte)topLeft, (byte)attr,
                8, 8, (byte)bottomLeft, (byte)attr,
                0, 0, (byte)topRight, (byte)attr,
                0, 8, (byte)bottomRight, (byte)attr,
                128
            };
        }
        else
        {
            data = new byte[]
            {
                0, 0, (byte)topLeft, (byte)attr,
                0, 8, (byte)bottomLeft, (byte)attr,
                8, 0, (byte)topRight, (byte)attr,
                8, 8, (byte)bottomRight, (byte)attr,
                128
            };
        }

        // Register as byte array data (same lifecycle as Ldtoken handler)
        string byteArrayLabel = $"bytearray_{_byteArrayLabelIndex}";
        _byteArrayLabelIndex++;
        _byteArrayAddressEmitted = false;

        // If next instruction is a Call that consumes this array, emit address now
        if (Instructions is not null && Index + 1 < Instructions.Length
            && Instructions[Index + 1].OpCode == ILOpCode.Call)
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

        _byteArrays.Add(data.ToImmutableArray());

        // Set state for subsequent Stloc to capture as byte array label
        _lastByteArrayLabel = byteArrayLabel;
        _lastByteArraySize = data.Length;
        Stack.Push(-(_byteArrayLabelIndex)); // Negative marker (same as Ldtoken)
        _pendingByteArrayFromIntrinsic = true;

        _immediateInA = null;
        _runtimeValueInA = false;
    }
}
