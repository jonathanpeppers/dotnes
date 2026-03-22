using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Arithmetic and branching — add/sub optimization, branch comparison, multiply.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// Emits a CMP instruction for branch comparison.
    /// Peeks at the last emitted instruction: if it's LDA with Absolute/ZeroPage mode
    /// (runtime variable), uses CMP $addr. Otherwise removes the LDA #imm and uses CMP #imm.
    /// stackValue is the value popped from IL stack (correct for constants, 0 for runtime).
    /// For &lt;= and &gt; comparisons, pass adjustValue=1 to compare with value+1.
    /// Returns true if the CMP was emitted normally. Returns false if stackValue+adjustValue
    /// overflows a byte (&gt; 255), meaning the caller must handle the always-true/false case.
    /// </summary>
    bool EmitBranchCompare(int stackValue, int adjustValue = 0)
    {
        int compareValue = stackValue + adjustValue;

        // If the combined compare value overflows a byte, the comparison is trivially
        // true or false (no byte can be >= 256). Return false so the caller can emit
        // an unconditional branch or skip the branch entirely.
        if (compareValue > 255 || compareValue < 0)
            return false;

        var block = CurrentBlock!;
        if (block.Count > 0)
        {
            var lastInstr = block[block.Count - 1];

            // Handle indexed array access: LDA array,X from ldelem.u1
            // Pattern: LDA value1; LDX index; LDA array,X → convert last to CMP array,X
            // Only apply when there's a prior value in A to compare against. Check the
            // instruction before LDX: if it's an LDA (loaded a comparison value) or STA to
            // TEMP (saved a runtime value for comparison), A retains a meaningful comparison
            // value through the LDX. Otherwise, the LDA array,X IS the value to compare
            // against a constant (from dup+ldc+bne pattern after ldelem).
            if (lastInstr.Opcode == Opcode.LDA
                && lastInstr.Mode == AddressMode.AbsoluteX
                && lastInstr.Operand is AbsoluteOperand idxOp)
            {
                bool hasPriorCompareValue = false;
                if (block.Count >= 3)
                {
                    var beforeLdx = block[block.Count - 3];
                    hasPriorCompareValue = beforeLdx.Opcode == Opcode.LDA
                        || (beforeLdx.Opcode == Opcode.STA && _savedRuntimeToTemp);
                }

                if (hasPriorCompareValue)
                {
                    // Replace LDA array,X with CMP array,X — A still has value1 from earlier
                    RemoveLastInstructions(1);
                    Emit(Opcode.CMP, AddressMode.AbsoluteX, idxOp.Address);
                }
                else
                {
                    // A holds the runtime value from LDA array,X — compare with the constant
                    Emit(Opcode.CMP, AddressMode.Immediate, (byte)compareValue);
                }
                return true;
            }

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
                    Emit(Opcode.CMP, AddressMode.Immediate, (byte)compareValue);
                }
                return true;
            }

            // Handle LDA ZeroPage (ImmediateOperand) — byte overload in Emit.
            // This occurs when the dup cascade handler reloads a value from TEMP.
            if (lastInstr.Opcode == Opcode.LDA
                && lastInstr.Mode == AddressMode.ZeroPage
                && lastInstr.Operand is ImmediateOperand)
            {
                // Single runtime value from zero page — keep the LDA, emit CMP #constant.
                Emit(Opcode.CMP, AddressMode.Immediate, (byte)compareValue);
                return true;
            }
        }
        // Constant comparison: remove last LDA #imm, emit CMP #imm
        // When _runtimeValueInA is true, WriteLdc skips emitting LDA — the last
        // instruction is the actual computation (SBC, ADC, AND, etc.) and must not
        // be removed.
        if (block.Count > 0)
        {
            var last = block[block.Count - 1];
            if (last.Opcode == Opcode.LDA && last.Mode == AddressMode.Immediate)
                RemoveLastInstructions(1);
        }
        Emit(Opcode.CMP, AddressMode.Immediate, (byte)compareValue);
        return true;
    }

    /// <summary>
    /// Tries to emit a 16-bit comparison and branch sequence.
    /// When A:X hold a 16-bit runtime value (from a word local load), this method
    /// scans the emitted block to find the local's memory addresses and emits a
    /// multi-byte comparison against a compile-time constant.
    ///
    /// Returns true if the 16-bit comparison was emitted, false if the block doesn't
    /// contain a word local pattern (LDA/LDX Absolute pair) and the caller should
    /// fall through to the 8-bit comparison path.
    ///
    /// branchTaken: the branch opcode for the "taken" case (BCC for LT, BCS for GE, BEQ for EQ, BNE for NE).
    /// </summary>
    bool TryEmitBranch16Bit(int cmpVal, string labelName, Opcode branchTaken, bool isShortForm)
    {
        byte cmpLo = (byte)(cmpVal & 0xFF);
        byte cmpHi = (byte)((cmpVal >> 8) & 0xFF);

        // Find value1's memory addresses from the block.
        // Expected pattern: LDA $lo_addr (Absolute), LDX $hi_addr (Absolute)
        var block = CurrentBlock!;
        ushort? loAddr = null, hiAddr = null;
        for (int i = block.Count - 1; i >= 0 && i >= block.Count - 4; i--)
        {
            var instr = block[i];
            if (hiAddr == null && instr.Opcode == Opcode.LDX && instr.Mode == AddressMode.Absolute
                && instr.Operand is AbsoluteOperand hiOp)
                hiAddr = hiOp.Address;
            else if (loAddr == null && instr.Opcode == Opcode.LDA && instr.Mode == AddressMode.Absolute
                && instr.Operand is AbsoluteOperand loOp)
                loAddr = loOp.Address;
        }

        if (loAddr == null || hiAddr == null)
            return false; // Not a word local pattern — caller should fall through to 8-bit path

        // Emit the 16-bit comparison + branch sequence.
        // All internal skip branches use hardcoded relative offsets (distances are known at compile time).
        if (isShortForm)
            EmitBranch16Short(loAddr.Value, hiAddr.Value, cmpLo, cmpHi, labelName, branchTaken);
        else
            EmitBranch16Long(loAddr.Value, hiAddr.Value, cmpLo, cmpHi, labelName, branchTaken);

        _ushortInAX = false;
        _runtimeValueInA = false;
        return true;
    }

    /// <summary>
    /// Emits a 16-bit comparison + short branch sequence (target reachable via relative branch).
    /// </summary>
    void EmitBranch16Short(ushort loAddr, ushort hiAddr, byte cmpLo, byte cmpHi, string labelName, Opcode branchTaken)
    {
        switch (branchTaken)
        {
            case Opcode.BNE:
                // hi differ → taken; else compare lo
                // LDA hi; CMP #hi; BNE target; LDA lo; CMP #lo; BNE target
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                break;

            case Opcode.BEQ:
                // hi differ → skip; else compare lo → BEQ target
                // LDA hi; CMP #hi; BNE +7; LDA lo; CMP #lo; BEQ target
                // Skip: LDA(3) + CMP(2) + BEQ(2) = 7
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BNE, AddressMode.Relative, 7);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
                break;

            case Opcode.BCC: // less than (unsigned)
                // hi < hi_c → taken; hi > hi_c → skip; else compare lo
                // LDA hi; CMP #hi; BCC target; BNE +7; LDA lo; CMP #lo; BCC target
                // Skip from BNE: LDA(3) + CMP(2) + BCC(2) = 7
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                Emit(Opcode.BNE, AddressMode.Relative, 7);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                break;

            case Opcode.BCS: // greater or equal (unsigned)
                // hi < hi_c → skip; hi > hi_c → taken; else compare lo
                // LDA hi; CMP #hi; BCC +9; BNE target; LDA lo; CMP #lo; BCS target
                // Skip from BCC: BNE(2) + LDA(3) + CMP(2) + BCS(2) = 9
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BCC, AddressMode.Relative, 9);
                EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                EmitWithLabel(Opcode.BCS, AddressMode.Relative, labelName);
                break;
        }
    }

    /// <summary>
    /// Emits a 16-bit comparison + long branch sequence (target requires JMP trampoline).
    /// </summary>
    void EmitBranch16Long(ushort loAddr, ushort hiAddr, byte cmpLo, byte cmpHi, string labelName, Opcode branchTaken)
    {
        switch (branchTaken)
        {
            case Opcode.BNE:
                // LDA hi; CMP #hi; BEQ +3; JMP target; LDA lo; CMP #lo; BEQ +3; JMP target
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BEQ, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                Emit(Opcode.BEQ, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                break;

            case Opcode.BEQ:
                // LDA hi; CMP #hi; BNE +10; LDA lo; CMP #lo; BNE +3; JMP target
                // Skip from first BNE: LDA(3) + CMP(2) + BNE(2) + JMP(3) = 10
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BNE, AddressMode.Relative, 10);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                Emit(Opcode.BNE, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                break;

            case Opcode.BCC: // less than
                // LDA hi; CMP #hi; BCS +3; JMP target; BNE +10; LDA lo; CMP #lo; BCS +3; JMP target
                // At BNE: skip LDA(3) + CMP(2) + BCS(2) + JMP(3) = 10
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BCS, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                Emit(Opcode.BNE, AddressMode.Relative, 10);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                Emit(Opcode.BCS, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                break;

            case Opcode.BCS: // greater or equal
                // [0] LDA hi (3); [3] CMP #hi (2)
                // [5] BCC +15 (2) → skip to done at [22]
                // [7] BEQ +3 (2) → skip JMP, check lo at [12]
                // [9] JMP target (3) → hi > hi_c → taken
                // [12] LDA lo (3); [15] CMP #lo (2)
                // [17] BCC +3 (2) → skip to done at [22]
                // [19] JMP target (3) → lo >= lo_c → taken
                // [22] = done
                Emit(Opcode.LDA, AddressMode.Absolute, hiAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpHi);
                Emit(Opcode.BCC, AddressMode.Relative, 15);
                Emit(Opcode.BEQ, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                Emit(Opcode.LDA, AddressMode.Absolute, loAddr);
                Emit(Opcode.CMP, AddressMode.Immediate, cmpLo);
                Emit(Opcode.BCC, AddressMode.Relative, 3);
                EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                break;
        }
    }

    /// <summary>
    /// Emits a JMP to a label reference.
    /// </summary>
    void EmitJMP(string labelName) => EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);

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
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP); // TEMP
            int remaining = factor;
            bool first = true;
            for (int bit = 0; remaining > 0; bit++)
            {
                if ((remaining & 1) != 0)
                {
                    if (first)
                    {
                        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP); // start with original
                        for (int s = 0; s < bit; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        first = false;
                    }
                    else
                    {
                        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP_HI); // save partial to TEMP+1
                        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                        for (int s = 0; s < bit; s++)
                            Emit(Opcode.ASL, AddressMode.Accumulator);
                        Emit(Opcode.CLC, AddressMode.Implied);
                        Emit(Opcode.ADC, AddressMode.ZeroPage, TEMP_HI);
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
    /// Handles Add and Sub operations. For patterns like x++ or x--, emits optimized INC/DEC.
    /// For other arithmetic, performs compile-time calculation.
    /// </summary>
    void HandleAddSub(bool isAdd)
    {
        _lastStaticFieldAddress = null;
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
                
                // The removal may have deleted a STA $TEMP emitted by WriteLdloc's
                // _runtimeValueInA save. Clear stale state flags so downstream handlers
                // don't reference a non-existent TEMP save or skip needed LDA emissions.
                _savedRuntimeToTemp = false;
                _runtimeValueInA = false;

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
                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                    Emit(Opcode.TXA, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.Immediate, hi);
                    Emit(Opcode.TAX, AddressMode.Implied);
                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
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
                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                    Emit(Opcode.TXA, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.Immediate, hi);
                    Emit(Opcode.TAX, AddressMode.Implied);
                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
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
                    _savedConstantViaPusha = false; // pusha was removed from code
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
                else if (_savedConstantViaPusha && operand == 0)
                {
                    // Constant was pusha'd, runtime in A — A + constant (commutative)
                    // Only applies when operand==0 (no real Stack value for the add).
                    // If operand!=0, the pusha'd value is a function call arg, not ours.
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                    EmitJSR("popa");
                    Emit(Opcode.CLC, AddressMode.Implied);
                    Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                    _savedConstantViaPusha = false;
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
                else if (_savedConstantViaPusha && operand == 0)
                {
                    // Constant was pusha'd, runtime in A — need constant - A
                    // Only applies when operand==0 (no real Stack value for the sub).
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                    EmitJSR("popa");
                    Emit(Opcode.SEC, AddressMode.Implied);
                    Emit(Opcode.SBC, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                    _savedConstantViaPusha = false;
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
            _savedConstantViaPusha = false;
            _runtimeValueInA = true; // Result of arithmetic is a runtime value
            return;
        }

        // Default: compile-time calculation only
        int compileTimeResult = isAdd ? baseValue + operand : baseValue - operand;
        Stack.Push(compileTimeResult);
        _lastLoadedLocalIndex = null;
    }
}
