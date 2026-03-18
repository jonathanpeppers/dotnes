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
    /// Handles ldelema: load the address of a struct array element.
    /// IL pattern: ldloc_N (array), ldc_i4 (index), ldelema TypeName
    /// Sets pending state for the subsequent stfld/ldfld.
    /// </summary>
    void HandleLdelema(ILInstruction instruction)
    {
        string? structType = instruction.String;
        if (structType == null)
            throw new TranspileException("Arrays are not supported for this element type. Only byte[], ushort[], and struct arrays are supported.", MethodName);
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

                _pendingStructArrayRuntimeIndex = true;
                _structArrayBaseForRuntimeIndex = arrayBase;
                _pendingStructElementType = structType;
                _pendingStructElementBase = null;
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
            _pendingStructElementBase = elementBase;
            _pendingStructElementType = structType;
            _pendingStructArrayRuntimeIndex = false;
        }

        _runtimeValueInA = false;
        _lastLoadedLocalIndex = null;
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
        int targetIndexLocalIdx = -1;
        int constantIndex = -1; // For constant-index stores like actor_dx[0] = 254
        int indexAddend = 0;    // For index arithmetic like buf[col + 1]
        int indexLocalAddend = -1; // For index arithmetic like buf[offset + j]
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
                        {
                            targetIndexLocalIdx = nextLocIdx.Value;
                            valueStart = i + 2;
                            // Check for index arithmetic: ldloc idx, ldc N, add, [conv]
                            // Pattern: buf[(byte)(col + 1)] → ldloc col, ldc 1, add, conv.u1
                            if (valueStart + 1 < Index)
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
                                constantIndex = constIdx.Value;
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
        bool hasShr = false;
        int shrValue = 0;
        bool hasTwoLdelems = false;
        int sourceArray1Idx = -1;
        int sourceIndex1Idx = -1;
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
            
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)targetIndex.Address!);
            if (indexAddend != 0 || indexLocalAddend >= 0)
            {
                RemoveLastInstructions(1); // remove the LDX
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)targetIndex.Address!);
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
            Emit(Opcode.LDX, AddressMode.Absolute, (ushort)targetIndex.Address!);
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
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)targetIndex.Address!);
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
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)targetIndex.Address!);
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
}
