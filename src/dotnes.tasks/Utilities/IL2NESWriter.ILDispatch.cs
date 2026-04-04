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
/// IL opcode dispatch — the main Write() overloads that process IL instructions.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// Checks if the current dup instruction starts a cascading if-else pattern
    /// (dup → ldc → bne/beq). Returns false for other dup patterns like dup → ldc → and → brfalse
    /// (button-check bitmasking) or newarr → dup → ldtoken (array init).
    /// </summary>
    bool IsDupCascadeStart()
    {
        if (Instructions is null || Index + 2 >= Instructions.Length)
            return false;

        var next1 = Instructions[Index + 1].OpCode;
        var next2 = Instructions[Index + 2].OpCode;

        bool isLdc = next1 is ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8
            or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_m1;

        bool isBranchCompare = next2 is ILOpCode.Bne_un_s or ILOpCode.Bne_un
            or ILOpCode.Beq_s or ILOpCode.Beq;

        return isLdc && isBranchCompare;
    }

    public void Write(ILInstruction instruction)
    {
        // Clear ldloc byte array label for non-ldloc instructions
        if (instruction.OpCode is not (ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
            or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s))
            _ldlocByteArrayLabel = null;

        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                break;
            case ILOpCode.Ret:
                // For user methods with early returns, emit JMP to epilogue.
                // Without this, early returns fall through to subsequent code.
                // Skip for the final ret (it naturally falls through to the epilogue).
                if (MethodName != null && Instructions != null && Index < Instructions.Length - 1)
                    EmitWithLabel(Opcode.JMP, AddressMode.Absolute, $"{MethodName}_epilogue");
                break;
            case ILOpCode.Dup:
                if (Stack.Count > 0)
                    Stack.Push(Stack.Peek());
                if (_dupCascadeActive)
                {
                    if (IsDupCascadeStart())
                    {
                        // Subsequent dup in cascading if-else: reload saved value from TEMP_HI
                        // (the DUP_TEMP scratch location). After an if-block runs (with JSR calls
                        // that modify A), A no longer holds the original comparison value. Reload
                        // it from TEMP_HI where the branch handler saved it.
                        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP_HI);
                        _dupPendingSave = true;
                    }
                    else
                    {
                        // Not a cascade continuation (e.g., dup for lo/hi byte extraction).
                        // Clear the stale cascade flag so it doesn't corrupt non-cascade dups.
                        _dupCascadeActive = false;
                        if (_ushortInAX) _dupPreservedUshortHi = true;
                    }
                }
                else if (IsDupCascadeStart())
                {
                    if (!_runtimeValueInA)
                    {
                        // The cascade value was pushed before intervening operations
                        // (e.g., stloc between ldelem and dup) that clobbered A.
                        // Check if HandleLdelemU1 saved it to TEMP ($17).
                        var block = CurrentBlock!;
                        for (int i = block.Count - 1; i >= 0; i--)
                        {
                            var instr = block[i];
                            if (instr.Opcode == Opcode.STA && instr.Mode == AddressMode.ZeroPage
                                && instr.Operand is ImmediateOperand op && op.Value == TEMP)
                            {
                                Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                                _runtimeValueInA = true;
                                break;
                            }
                        }
                    }
                    if (_runtimeValueInA)
                    {
                        // First dup in cascade: mark for saving in the branch handler.
                        // A holds the correct value (either natively or reloaded from TEMP).
                        _dupCascadeActive = true;
                        _dupPendingSave = true;
                    }
                }
                if (_ushortInAX && !_dupCascadeActive) _dupPreservedUshortHi = true;
                break;
            case ILOpCode.Pop:
                if (Stack.Count > 0)
                    Stack.Pop();
                _runtimeValueInA = false;
                _dupCascadeActive = false;
                _dupPendingSave = false;
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
                else if (previous == ILOpCode.Ldtoken || _pendingByteArrayFromIntrinsic)
                {
                    // Capture label for byte array reference
                    Locals[0] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    _pendingByteArrayFromIntrinsic = false;
                    _stloc0IsLdtokenPath = true;
                    Stack.Pop(); // Discard marker
                }
                else if (previous == ILOpCode.Newarr)
                {
                    HandleStlocAfterNewarr(0);
                }
                else
                {
                    bool isNew = !(Locals.TryGetValue(0, out var existing) && existing.Address is not null);
                    var addr = isNew ? (ushort)(local + LocalCount) : (ushort)existing!.Address!.Value;
                    WriteStloc(Locals[0] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(0) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult), isNewAllocation: isNew);
                }
                break;
            case ILOpCode.Stloc_1:
                if (_pendingIncDecLocal == 1)
                {
                    Locals[1] = new Local(Stack.Pop(), Locals[1].Address, IsWord: Locals[1].IsWord);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken || _pendingByteArrayFromIntrinsic)
                {
                    Locals[1] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    _pendingByteArrayFromIntrinsic = false;
                    Stack.Pop();
                }
                else if (previous == ILOpCode.Newarr)
                {
                    HandleStlocAfterNewarr(1);
                }
                else
                {
                    bool isNew = !(Locals.TryGetValue(1, out var existing) && existing.Address is not null);
                    var addr = isNew ? (ushort)(local + LocalCount) : (ushort)existing!.Address!.Value;
                    WriteStloc(Locals[1] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(1) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult), isNewAllocation: isNew);
                }
                break;
            case ILOpCode.Stloc_2:
                if (_pendingIncDecLocal == 2)
                {
                    Locals[2] = new Local(Stack.Pop(), Locals[2].Address, IsWord: Locals[2].IsWord);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken || _pendingByteArrayFromIntrinsic)
                {
                    Locals[2] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    _pendingByteArrayFromIntrinsic = false;
                    Stack.Pop();
                }
                else if (previous == ILOpCode.Newarr)
                {
                    HandleStlocAfterNewarr(2);
                }
                else
                {
                    bool isNew = !(Locals.TryGetValue(2, out var existing) && existing.Address is not null);
                    var addr = isNew ? (ushort)(local + LocalCount) : (ushort)existing!.Address!.Value;
                    WriteStloc(Locals[2] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(2) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult), isNewAllocation: isNew);
                }
                break;
            case ILOpCode.Stloc_3:
                if (_pendingIncDecLocal == 3)
                {
                    Locals[3] = new Local(Stack.Pop(), Locals[3].Address, IsWord: Locals[3].IsWord);
                    _pendingIncDecLocal = null;
                }
                else if (previous == ILOpCode.Ldtoken || _pendingByteArrayFromIntrinsic)
                {
                    Locals[3] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    _pendingByteArrayFromIntrinsic = false;
                    Stack.Pop();
                }
                else if (previous == ILOpCode.Newarr)
                {
                    HandleStlocAfterNewarr(3);
                }
                else
                {
                    bool isNew = !(Locals.TryGetValue(3, out var existing) && existing.Address is not null);
                    var addr = isNew ? (ushort)(local + LocalCount) : (ushort)existing!.Address!.Value;
                    WriteStloc(Locals[3] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(3) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult), isNewAllocation: isNew);
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
                    if (IsClosureMethod)
                    {
                        if (argIndex == ClosureArgIndex)
                        {
                            // This arg is the closure struct ref (always last param).
                            // Set flag so the next ldfld/stfld accesses closure fields.
                            _pendingClosureAccess = true;
                            break;
                        }
                        // Real params keep their original indices — no shifting needed
                    }
                    WriteLdarg(argIndex);
                }
                break;
            case ILOpCode.Conv_u1:
                // When truncating from ushort to byte, discard high byte
                if (_ushortInAX)
                    _ushortInAX = false;
                // A now holds a transformed value, no longer directly from a static field
                _lastStaticFieldAddress = null;
                break;
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_u4:
            case ILOpCode.Conv_u8:
            case ILOpCode.Conv_i1:
            case ILOpCode.Conv_i2:
            case ILOpCode.Conv_i4:
                // No-op: sign/zero extension is irrelevant on 8-bit 6502
                _lastStaticFieldAddress = null;
                break;
            case ILOpCode.Stelem_i1:
            case ILOpCode.Stelem_i2:
            case ILOpCode.Stelem_i4:
            case ILOpCode.Stelem_i8:
                // stelem.i*(stack: arrayref, index, value → stack: )
                HandleStelemI1();
                break;
            case ILOpCode.Ldind_u1:
                // ldind.u1: load byte through pointer (from ldelema System.Byte)
                HandleLdindU1();
                break;
            case ILOpCode.Stind_i1:
                // stind.i1: store byte through pointer (from ldelema System.Byte)
                HandleStindI1();
                break;
            case ILOpCode.Add:
                HandleAddSub(isAdd: true);
                break;
            case ILOpCode.Sub:
                HandleAddSub(isAdd: false);
                break;
            case ILOpCode.Mul:
                {
                    _lastStaticFieldAddress = null;
                    int val2 = Stack.Pop();
                    int val1 = Stack.Count > 0 ? Stack.Pop() : 0;

                    // Same class of bug as AND/Add/Sub: ldloc emits LDA $addr but doesn't set _runtimeValueInA
                    bool mulLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var mulLocal) && mulLocal.Address != null;

                    if (_runtimeValueInA || mulLocalInA)
                    {
                        // For ldloc;ldc;mul pattern, the last instruction is the constant load (LDA #N)
                        // Remove that LDA #constant to restore A to the local's runtime value
                        if (mulLocalInA && !_runtimeValueInA
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            // WriteLdloc emits LDA $local (Absolute), which sets LastLDA=false,
                            // so WriteLdc only emitted LDA #constant (no pusha). Remove 1.
                            RemoveLastInstructions(1);
                        }

                        if (_savedRuntimeToTemp)
                        {
                            // Two runtime values: first in TEMP, second in A
                            // 8-bit multiply: TEMP × A → A
                            //   STA TEMP2      ; multiplier
                            //   LDA #0         ; result = 0
                            //   LDX #8         ; 8 bits
                            //   LSR TEMP2      ; ← @loop: shift multiplier right
                            //   BCC @skip      ; +3 to skip CLC+ADC
                            //   CLC
                            //   ADC TEMP       ; result += multiplicand
                            //   ASL TEMP       ; ← @skip: shift multiplicand left
                            //   DEX
                            //   BNE @loop      ; -10 to LSR
                            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.LDA, AddressMode.Immediate, 0);
                            Emit(Opcode.LDX, AddressMode.Immediate, 8);
                            Emit(Opcode.LSR, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.BCC, AddressMode.Relative, 3); // skip CLC(1) + ADC_zp(2)
                            Emit(Opcode.CLC, AddressMode.Implied);
                            Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)TEMP);
                            Emit(Opcode.ASL, AddressMode.ZeroPage, (byte)TEMP);
                            Emit(Opcode.DEX, AddressMode.Implied);
                            Emit(Opcode.BNE, AddressMode.Relative, unchecked((byte)-12));
                            _savedRuntimeToTemp = false;
                        }
                        else
                        {
                            // Determine which operand is the compile-time constant
                            bool previousWasLdc =
                                previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                                or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                                or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                                or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8;
                            int constant = previousWasLdc ? val2 : val1;
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
                                    Emit(Opcode.STX, AddressMode.ZeroPage, TEMP); // TEMP = 0 (high byte)
                                    for (int s = 0; s < shifts; s++)
                                    {
                                        Emit(Opcode.ASL, AddressMode.Accumulator);
                                        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP);
                                    }
                                    Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP); // X = high byte
                                    _ushortInAX = true;
                                }
                                else
                                {
                                    // 8-bit shift only
                                    for (int s = 0; s < shifts; s++)
                                        Emit(Opcode.ASL, AddressMode.Accumulator);
                                }
                            }
                            else
                            {
                                // General non-power-of-2 multiply: A × constant → A
                                // Store runtime value as multiplicand, constant as multiplier
                                //   STA TEMP       ; multiplicand
                                //   LDA #constant  ; multiplier → TEMP2
                                //   STA TEMP2
                                //   LDA #0         ; result = 0
                                //   LDX #8         ; 8 bits
                                //   LSR TEMP2      ; ← @loop: shift multiplier right
                                //   BCC @skip      ; +3 to skip CLC+ADC
                                //   CLC
                                //   ADC TEMP       ; result += multiplicand
                                //   ASL TEMP       ; ← @skip: shift multiplicand left
                                //   DEX
                                //   BNE @loop      ; -10 to LSR
                                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.LDA, AddressMode.Immediate, checked((byte)constant));
                                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                                Emit(Opcode.LDX, AddressMode.Immediate, 8);
                                Emit(Opcode.LSR, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                                Emit(Opcode.BCC, AddressMode.Relative, 3); // skip CLC(1) + ADC_zp(2)
                                Emit(Opcode.CLC, AddressMode.Implied);
                                Emit(Opcode.ADC, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.ASL, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.DEX, AddressMode.Implied);
                                Emit(Opcode.BNE, AddressMode.Relative, unchecked((byte)-12));
                            }
                        }
                        Stack.Push(val1 * val2);
                        _runtimeValueInA = true;
                    }
                    else
                    {
                        Stack.Push(val1 * val2);
                    }
                }
                break;
            case ILOpCode.Div:
                {
                    _lastStaticFieldAddress = null;
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

                        if (_ushortInAX)
                        {
                            // 16-bit (ushort) division: A:X = lo:hi
                            if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                            {
                                // Power-of-2: 16-bit right shift
                                int shifts = 0;
                                int temp = divisor;
                                while (temp > 1) { temp >>= 1; shifts++; }
                                if (shifts >= 8)
                                {
                                    // Shift by 8+: move hi byte to A, clear X, shift remaining
                                    Emit(Opcode.TXA, AddressMode.Implied);
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0);
                                    for (int i = 0; i < shifts - 8; i++)
                                        Emit(Opcode.LSR, AddressMode.Accumulator);
                                    _ushortInAX = false;
                                }
                                else
                                {
                                    // 1-7 bit shifts: 16-bit right shift (same pattern as Shr_un)
                                    for (int i = 0; i < shifts; i++)
                                    {
                                        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                                        Emit(Opcode.LSR, AddressMode.ZeroPage, TEMP);
                                        Emit(Opcode.ROR, AddressMode.Accumulator);
                                        Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
                                    }
                                }
                            }
                            else
                            {
                                // Non-power-of-2: 16-bit binary long division
                                // Dividend in A:X (lo:hi), divisor is constant byte
                                // Uses shift-and-subtract: quotient built in TEMP:TEMP_HI
                                //   STA TEMP       ; save dividend lo
                                //   STX TEMP_HI    ; save dividend hi
                                //   LDA #0         ; remainder = 0
                                //   LDY #16        ; 16 bits to process
                                //   ASL TEMP       ; ← @loop: shift dividend left
                                //   ROL TEMP_HI    ;   (moves MSB into carry)
                                //   ROL A          ;   shift carry into remainder
                                //   CMP #divisor   ;   compare remainder with divisor
                                //   BCC @skip      ;   +4 to skip SBC+INC
                                //   SBC #divisor   ;   subtract divisor
                                //   INC TEMP       ;   set quotient bit (was 0 from ASL)
                                //   DEY            ; ← @skip: decrement counter
                                //   BNE @loop      ;   -16 back to ASL
                                //   LDA TEMP       ; quotient lo
                                //   LDX TEMP_HI    ; quotient hi
                                Emit(Opcode.STA, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.STX, AddressMode.ZeroPage, (byte)NESConstants.TEMP_HI);
                                Emit(Opcode.LDA, AddressMode.Immediate, 0);
                                Emit(Opcode.LDY, AddressMode.Immediate, 16);
                                Emit(Opcode.ASL, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.ROL, AddressMode.ZeroPage, (byte)NESConstants.TEMP_HI);
                                Emit(Opcode.ROL, AddressMode.Accumulator);
                                Emit(Opcode.CMP, AddressMode.Immediate, (byte)divisor);
                                Emit(Opcode.BCC, AddressMode.Relative, 4); // skip SBC(2) + INC(2)
                                Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
                                Emit(Opcode.INC, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.DEY, AddressMode.Implied);
                                Emit(Opcode.BNE, AddressMode.Relative, unchecked((byte)-16)); // -16 back to ASL
                                Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)TEMP);
                                Emit(Opcode.LDX, AddressMode.ZeroPage, (byte)NESConstants.TEMP_HI);
                            }
                        }
                        // Emit LSR A for power-of-2 divisors (8-bit)
                        else if (divisor > 0 && (divisor & (divisor - 1)) == 0)
                        {
                            int shifts = 0;
                            int temp = divisor;
                            while (temp > 1) { temp >>= 1; shifts++; }
                            for (int i = 0; i < shifts; i++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                        }
                        else if (_savedRuntimeToTemp)
                        {
                            // Runtime divisor (in A) and runtime dividend (in TEMP)
                            // Save divisor to TEMP2, load dividend from TEMP, then divide
                            Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                            Emit(Opcode.LDX, AddressMode.Immediate, 0xFF);
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.INX, AddressMode.Implied);
                            Emit(Opcode.SBC, AddressMode.ZeroPage, (byte)NESConstants.TEMP2);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-5));
                            Emit(Opcode.TXA, AddressMode.Implied);
                            _savedRuntimeToTemp = false;
                        }
                        else
                        {
                            // General 8-bit division via repeated subtraction
                            // A = dividend (runtime). Result (quotient) left in A.
                            //   LDX #$FF      ; 2 bytes (offset 0) quotient = -1
                            //   SEC            ; 1 byte  (offset 2) set carry
                            //   INX            ; 1 byte  (offset 3) ← @loop
                            //   SBC #divisor   ; 2 bytes (offset 4)
                            //   BCS @loop      ; 2 bytes (offset 6) → -5 to INX
                            //   TXA            ; 1 byte  (offset 8) quotient to A
                            Emit(Opcode.LDX, AddressMode.Immediate, 0xFF);
                            Emit(Opcode.SEC, AddressMode.Implied);
                            Emit(Opcode.INX, AddressMode.Implied);
                            Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
                            Emit(Opcode.BCS, AddressMode.Relative, unchecked((byte)-5));
                            Emit(Opcode.TXA, AddressMode.Implied);
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
                    _lastStaticFieldAddress = null;
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
                    _lastStaticFieldAddress = null;
                    int shiftCount = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool shrLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var shrLocal) && shrLocal.Address != null;

                    if (_runtimeValueInA || shrLocalInA || _ushortInAX || _dupPreservedUshortHi)
                    {
                        if (!_runtimeValueInA && !_ushortInAX
                            && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                            or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                            or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                        {
                            RemoveLastInstructions(1);
                        }
                        if ((_ushortInAX || _dupPreservedUshortHi) && shiftCount >= 8)
                        {
                            // ushort >> 8+: high byte to A, then shift remaining
                            Emit(Opcode.TXA, AddressMode.Implied);
                            for (int i = 0; i < shiftCount - 8; i++)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                            _ushortInAX = false;
                            _dupPreservedUshortHi = false;
                        }
                        else if (_ushortInAX)
                        {
                            // ushort >> N (N < 8): shift both bytes
                            for (int i = 0; i < shiftCount; i++)
                            {
                                Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                                Emit(Opcode.LSR, AddressMode.ZeroPage, TEMP);
                                Emit(Opcode.ROR, AddressMode.Accumulator);
                                Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
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
                    _lastStaticFieldAddress = null;
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
                    _lastStaticFieldAddress = null;
                    int mask = Stack.Pop();
                    int value = Stack.Count > 0 ? Stack.Pop() : 0;

                    // Check if the value came from a local variable load (runtime value)
                    bool localInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var andLocal) && andLocal.Address != null;

                    // 16-bit AND: runtime ushort in A:X with immediate mask
                    if (_ushortInAX && (_runtimeValueInA || localInA))
                    {
                        int immediateMask = mask;
                        if (mask == 0 && value != 0)
                            immediateMask = value;
                        // If localInA (not runtime), WriteLdc(ushort) emitted LDX #hi + LDA #lo
                        // which clobbered the local's A:X. Remove those 2 instructions.
                        if (!_runtimeValueInA && localInA)
                            RemoveLastInstructions(2);
                        Emit16BitBitwiseOp(Opcode.AND, immediateMask);
                        _runtimeValueInA = true;
                        Stack.Push(0);
                        break;
                    }

                    // Emit runtime AND if A has a runtime value
                    if (_padPollResultAvailable || _runtimeValueInA || localInA)
                    {
                        if (_savedRuntimeToTemp)
                        {
                            // Two runtime values: first in TEMP, second in A
                            Emit(Opcode.AND, AddressMode.ZeroPage, (byte)TEMP);
                            _savedRuntimeToTemp = false;
                        }
                        else
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

                            // If not first AND after pad_poll, need to reload pad value.
                            // Skip reload when A already holds the intended operand for
                            // this AND (e.g., from ldelem or arithmetic), so we don't
                            // overwrite it with a stale pad_poll result.
                            if (_padPollResultAvailable && !_firstAndAfterPadPoll && !_runtimeValueInA)
                            {
                                Emit(Opcode.LDA, AddressMode.Absolute, _padReloadAddress);
                            }

                            // AND is commutative: pick the non-zero operand as the mask
                            // (runtime values show as 0 placeholder on the stack)
                            int immediateMask = mask;
                            if (mask == 0 && value != 0)
                                immediateMask = value;

                            Emit(Opcode.AND, AddressMode.Immediate, checked((byte)immediateMask));
                        }
                        _firstAndAfterPadPoll = false;
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
                    _lastStaticFieldAddress = null;
                    int orMask = Stack.Pop();
                    int orValue = Stack.Count > 0 ? Stack.Pop() : 0;

                    // Check if the value came from a local variable load (runtime value)
                    bool orLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var orLocal) && orLocal.Address != null;

                    // 16-bit OR: runtime ushort in A:X with immediate mask
                    if (_ushortInAX && (_runtimeValueInA || orLocalInA))
                    {
                        int immediateOrMask = orMask;
                        if (orMask == 0 && orValue != 0)
                            immediateOrMask = orValue;
                        if (!_runtimeValueInA && orLocalInA)
                            RemoveLastInstructions(2);
                        Emit16BitBitwiseOp(Opcode.ORA, immediateOrMask);
                        _runtimeValueInA = true;
                        Stack.Push(0);
                        break;
                    }

                    if (_runtimeValueInA || orLocalInA)
                    {
                        if (_savedRuntimeToTemp)
                        {
                            // Two runtime values: first in TEMP, second in A
                            Emit(Opcode.ORA, AddressMode.ZeroPage, (byte)TEMP);
                            _savedRuntimeToTemp = false;
                        }
                        else
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

                            // OR is commutative: pick the non-zero operand as the mask
                            int immediateOrMask = orMask;
                            if (orMask == 0 && orValue != 0)
                                immediateOrMask = orValue;

                            Emit(Opcode.ORA, AddressMode.Immediate, checked((byte)immediateOrMask));
                        }
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
                {
                    _lastStaticFieldAddress = null;
                    int xorVal2 = Stack.Pop();
                    int xorVal1 = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool xorLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var xorLocal) && xorLocal.Address != null;

                    // 16-bit XOR: runtime ushort in A:X with immediate mask
                    if (_ushortInAX && (_runtimeValueInA || xorLocalInA))
                    {
                        int xorConst = xorVal2;
                        if (xorVal2 == 0 && xorVal1 != 0)
                            xorConst = xorVal1;
                        if (!_runtimeValueInA && xorLocalInA)
                            RemoveLastInstructions(2);
                        Emit16BitBitwiseOp(Opcode.EOR, xorConst);
                        _runtimeValueInA = true;
                        Stack.Push(0);
                        break;
                    }

                    if (_runtimeValueInA || xorLocalInA)
                    {
                        if (_savedRuntimeToTemp)
                        {
                            // Two runtime values: first in TEMP, second in A
                            Emit(Opcode.EOR, AddressMode.ZeroPage, (byte)TEMP);
                            _savedRuntimeToTemp = false;
                        }
                        else
                        {
                            // Remove the LDA #constant emitted by WriteLdc
                            if (!_runtimeValueInA
                                && previous is ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
                                or ILOpCode.Ldc_i4_m1
                                or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                                or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                                or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8)
                            {
                                RemoveLastInstructions(1);
                            }

                            // XOR is commutative: pick the non-zero operand as constant
                            int xorConst = xorVal2;
                            if (xorVal2 == 0 && xorVal1 != 0)
                                xorConst = xorVal1;

                            Emit(Opcode.EOR, AddressMode.Immediate, checked((byte)xorConst));
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0);
                    }
                    else
                    {
                        Stack.Push(xorVal1 ^ xorVal2);
                    }
                }
                break;
            case ILOpCode.Neg:
                {
                    int value = Stack.Pop();

                    bool negLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var negLocal) && negLocal.Address != null;

                    if (_runtimeValueInA || negLocalInA)
                    {
                        // Two's complement negation: EOR #$FF; CLC; ADC #$01
                        Emit(Opcode.EOR, AddressMode.Immediate, (byte)0xFF);
                        Emit(Opcode.CLC);
                        Emit(Opcode.ADC, AddressMode.Immediate, (byte)0x01);
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(-value);
                    }
                }
                break;
            case ILOpCode.Not:
                {
                    int value = Stack.Pop();

                    bool notLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var notLocal) && notLocal.Address != null;

                    if (_runtimeValueInA || notLocalInA)
                    {
                        // Bitwise NOT: EOR #$FF
                        Emit(Opcode.EOR, AddressMode.Immediate, (byte)0xFF);
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(~value);
                    }
                }
                break;
            case ILOpCode.Ceq:
                {
                    int ceqVal2 = Stack.Count > 0 ? Stack.Pop() : 0;
                    int ceqVal1 = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool ceqLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var ceqLocal) && ceqLocal.Address != null;

                    if (_runtimeValueInA || ceqLocalInA)
                    {
                        if (!EmitBranchCompare(ceqVal2))
                        {
                            // Out-of-range compare value: A (byte) can never equal ceqVal2
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);
                        }
                        else
                        {
                            // Set A = 1 if equal, 0 if not
                            // BEQ +4 skips: LDA #0 (2 bytes) + BEQ +2 (2 bytes)
                            Emit(Opcode.BEQ, AddressMode.Relative, (byte)4);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);
                            Emit(Opcode.BEQ, AddressMode.Relative, (byte)2);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)1);
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(ceqVal1 == ceqVal2 ? 1 : 0);
                    }
                }
                break;
            case ILOpCode.Cgt:
            case ILOpCode.Cgt_un:
                {
                    int cgtVal2 = Stack.Count > 0 ? Stack.Pop() : 0;
                    int cgtVal1 = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool cgtLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var cgtLocal) && cgtLocal.Address != null;

                    if (_runtimeValueInA || cgtLocalInA)
                    {
                        if (!EmitBranchCompare(cgtVal2, adjustValue: 1))
                        {
                            // val2+1 overflows: A > 255 is always false for bytes
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);
                        }
                        else
                        {
                            // BCS = A >= val2+1 = A > val2
                            Emit(Opcode.BCS, AddressMode.Relative, (byte)4);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);
                            Emit(Opcode.BEQ, AddressMode.Relative, (byte)2);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)1);
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(cgtVal1 > cgtVal2 ? 1 : 0);
                    }
                }
                break;
            case ILOpCode.Clt:
            case ILOpCode.Clt_un:
                {
                    int cltVal2 = Stack.Count > 0 ? Stack.Pop() : 0;
                    int cltVal1 = Stack.Count > 0 ? Stack.Pop() : 0;

                    bool cltLocalInA = _lastLoadedLocalIndex.HasValue &&
                        Locals.TryGetValue(_lastLoadedLocalIndex.Value, out var cltLocal) && cltLocal.Address != null;

                    if (_runtimeValueInA || cltLocalInA)
                    {
                        if (!EmitBranchCompare(cltVal2))
                        {
                            // Compare value out of byte range
                            if (cltVal2 > 255)
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)1);  // A < 256+ always true
                            else
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);  // A < negative always false
                        }
                        else
                        {
                            // BCC = A < val2 (carry clear)
                            Emit(Opcode.BCC, AddressMode.Relative, (byte)4);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)0);
                            Emit(Opcode.BEQ, AddressMode.Relative, (byte)2);
                            Emit(Opcode.LDA, AddressMode.Immediate, (byte)1);
                        }
                        _runtimeValueInA = true;
                        Stack.Push(0); // Runtime placeholder
                    }
                    else
                    {
                        Stack.Push(cltVal1 < cltVal2 ? 1 : 0);
                    }
                }
                break;
            case ILOpCode.Ldelem_u1:
                // ldelem.u1: pop array ref and index, push array[index]
                // Pattern: Ldloc_N (array), Ldloc_M (index), Ldelem_u1
                HandleLdelemU1();
                break;
            case ILOpCode.Endfinally:
                {
                    // End of a finally block — jump to the instruction after the handler,
                    // or fall through if it's already the next instruction.
                    var region = FindEnclosingHandlerRegion(instruction.Offset);
                    if (region != null)
                    {
                        int afterHandler = region.Value.HandlerOffset + region.Value.HandlerLength;
                        int nextOffset = instruction.Offset + 1; // endfinally is 1 byte
                        if (nextOffset != afterHandler)
                            EmitJMP(InstructionLabel(afterHandler));
                    }
                    Stack.Clear();
                    _accState = AccumulatorState.Empty;
                }
                break;
            default:
                throw new TranspileException(GetUnsupportedOpcodeMessage(instruction.OpCode), MethodName);
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
                {
                    int argIndex = operand;
                    if (IsClosureMethod)
                    {
                        if (argIndex == ClosureArgIndex)
                        {
                            _pendingClosureAccess = true;
                            break;
                        }
                        // Real params keep their original indices — no shifting needed
                    }
                    WriteLdarg(argIndex);
                }
                break;
            case ILOpCode.Ldc_i4:
            case ILOpCode.Ldc_i4_s:
                if (operand > ushort.MaxValue)
                {
                    throw new TranspileException($"Integer constant {operand} exceeds the maximum supported value of {ushort.MaxValue}. Only byte and ushort values are supported on the NES.", MethodName);
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
                    var labelName = InstructionLabel(instruction.Offset + operand + 2);
                    EmitJMP(labelName);
                }
                break;
            case ILOpCode.Br:
                // Long form unconditional branch (32-bit offset)
                {
                    var labelName = InstructionLabel(instruction.Offset + operand + 5);
                    EmitJMP(labelName);
                }
                break;
            case ILOpCode.Leave_s:
                // Exit try block (short form) — jump to finally handler start,
                // or fall through if the handler is the next instruction.
                {
                    var region = FindEnclosingTryRegion(instruction.Offset);
                    if (region != null)
                    {
                        int nextOffset = instruction.Offset + 2; // leave.s is 2 bytes
                        if (nextOffset != region.Value.HandlerOffset)
                            EmitJMP(InstructionLabel(region.Value.HandlerOffset));
                    }
                    else
                    {
                        // No try/finally context — treat as unconditional branch
                        operand = (sbyte)(byte)operand;
                        EmitJMP(InstructionLabel(instruction.Offset + operand + 2));
                    }
                    Stack.Clear();
                    _accState = AccumulatorState.Empty;
                }
                break;
            case ILOpCode.Leave:
                // Exit try block (long form) — jump to finally handler start,
                // or fall through if the handler is the next instruction.
                {
                    var region = FindEnclosingTryRegion(instruction.Offset);
                    if (region != null)
                    {
                        int nextOffset = instruction.Offset + 5; // leave is 5 bytes
                        if (nextOffset != region.Value.HandlerOffset)
                            EmitJMP(InstructionLabel(region.Value.HandlerOffset));
                    }
                    else
                    {
                        // No try/finally context — treat as unconditional branch
                        EmitJMP(InstructionLabel(instruction.Offset + operand + 5));
                    }
                    Stack.Clear();
                    _accState = AccumulatorState.Empty;
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
                    else if (previous == ILOpCode.Ldtoken || _pendingByteArrayFromIntrinsic)
                    {
                        // Initialized byte array from Ldtoken or meta_spr_2x2 intrinsic
                        Locals[localIdx] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                        _lastByteArrayLabel = null;
                        _pendingByteArrayFromIntrinsic = false;
                        Stack.Pop();
                    }
                    else
                    {
                        // Regular local — allocate address and store
                        bool isNew = !(Locals.TryGetValue(localIdx, out var existing) && existing.Address is not null);
                        var addr = isNew ? (ushort)(local + LocalCount) : (ushort)existing!.Address!.Value;
                        WriteStloc(Locals[localIdx] = new Local(Stack.Pop(), addr, IsWord: WordLocals.Contains(localIdx) || (_runtimeValueInA && _ushortInAX) || _ntadrRuntimeResult), isNewAllocation: isNew);
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
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        if (_dupPendingSave)
                            _dupPendingSave = false;
                        EmitBranch16Bit(cmpVal, labelName, Opcode.BNE, instruction.OpCode == ILOpCode.Bne_un_s);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal))
                    {
                        // Overflow (8-bit fallback): value in A can never equal 256+ → unconditional branch
                        if (_dupPendingSave)
                        {
                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP_HI);
                            _dupPendingSave = false;
                        }
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    else
                    {
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

                        // In a dup cascade, save A to DUP_TEMP after CMP so subsequent
                        // checks can reload it. STA does NOT affect processor flags,
                        // so the branch instruction still sees the CMP result.
                        // Only save on the branch that immediately follows dup+ldc
                        // (not on unrelated branches inside the if-body).
                        if (_dupPendingSave)
                        {
                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP_HI);
                            _dupPendingSave = false;
                        }
                    
                        if (instruction.OpCode == ILOpCode.Bne_un_s)
                            EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                        else
                        {
                            Emit(Opcode.BEQ, AddressMode.Relative, 3);
                            EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                        }
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
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        if (_dupPendingSave)
                            _dupPendingSave = false;
                        EmitBranch16Bit(cmpVal, labelName, Opcode.BEQ, instruction.OpCode == ILOpCode.Beq_s);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal))
                    {
                        // Overflow (8-bit fallback): value in A can never equal 256+ → skip branch (no-op)
                        if (_dupPendingSave)
                        {
                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP_HI);
                            _dupPendingSave = false;
                        }
                    }
                    else
                    {
                        if (_dupPendingSave)
                        {
                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP_HI);
                            _dupPendingSave = false;
                        }

                        if (instruction.OpCode == ILOpCode.Beq_s)
                            EmitWithLabel(Opcode.BEQ, AddressMode.Relative, labelName);
                        else
                        {
                            Emit(Opcode.BNE, AddressMode.Relative, 3);
                            EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                        }
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Brfalse_s:
                // Branch if value is zero/false (after AND test)
                {
                    operand = (sbyte)(byte)operand;
                    var labelName = InstructionLabel(instruction.Offset + operand + 2);
                    if (_ushortInAX)
                    {
                        // 16-bit zero check: combine A (lo) and X (hi) via ORA
                        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.ORA, AddressMode.ZeroPage, TEMP);
                        _ushortInAX = false;
                    }
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
                    var labelName = InstructionLabel(instruction.Offset + operand + 2);
                    if (_ushortInAX)
                    {
                        // 16-bit non-zero check: combine A (lo) and X (hi) via ORA
                        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.ORA, AddressMode.ZeroPage, TEMP);
                        _ushortInAX = false;
                    }
                    EmitWithLabel(Opcode.BNE, AddressMode.Relative, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Blt_s:
            case ILOpCode.Blt:
            case ILOpCode.Blt_un_s:
            case ILOpCode.Blt_un:
                // Branch if less than: value1 < value2
                {
                    bool isShort = instruction.OpCode is ILOpCode.Blt_s or ILOpCode.Blt_un_s;
                    int branchOffset = isShort ? (sbyte)(byte)operand : operand;
                    int instrSize = isShort ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        EmitBranch16Bit(cmpVal, labelName, Opcode.BCC, isShort);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal))
                    {
                        // Overflow (8-bit fallback): value in A is always < 256+ → unconditional branch
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    else if (isShort)
                        EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCS, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Ble_s:
            case ILOpCode.Ble:
            case ILOpCode.Ble_un_s:
            case ILOpCode.Ble_un:
                // Branch if less than or equal: CMP #(value2+1) + BCC/trampoline
                {
                    bool isShort = instruction.OpCode is ILOpCode.Ble_s or ILOpCode.Ble_un_s;
                    int branchOffset = isShort ? (sbyte)(byte)operand : operand;
                    int instrSize = isShort ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        int adjusted = cmpVal + 1;
                        if (adjusted > ushort.MaxValue)
                        {
                            EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                            _ushortInAX = false;
                            _runtimeValueInA = false;
                            break;
                        }
                        EmitBranch16Bit(adjusted, labelName, Opcode.BCC, isShort);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal, adjustValue: 1))
                    {
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    else if (isShort)
                        EmitWithLabel(Opcode.BCC, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCS, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Bge_s:
            case ILOpCode.Bge:
            case ILOpCode.Bge_un_s:
            case ILOpCode.Bge_un:
                // Branch if greater than or equal: CMP #value2 + BCS/trampoline
                {
                    bool isShort = instruction.OpCode is ILOpCode.Bge_s or ILOpCode.Bge_un_s;
                    int branchOffset = isShort ? (sbyte)(byte)operand : operand;
                    int instrSize = isShort ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        EmitBranch16Bit(cmpVal, labelName, Opcode.BCS, isShort);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal))
                    {
                        // Overflow (8-bit fallback): value in A is never >= 256+ → skip branch (no-op)
                    }
                    else if (isShort)
                        EmitWithLabel(Opcode.BCS, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCC, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Bgt_s:
            case ILOpCode.Bgt:
            case ILOpCode.Bgt_un_s:
            case ILOpCode.Bgt_un:
                // Branch if greater than: CMP #(value2+1) + BCS/trampoline
                {
                    bool isShort = instruction.OpCode is ILOpCode.Bgt_s or ILOpCode.Bgt_un_s;
                    int branchOffset = isShort ? (sbyte)(byte)operand : operand;
                    int instrSize = isShort ? 2 : 5;
                    int cmpVal = Stack.Count > 0 ? Stack.Pop() : 0;
                    if (Stack.Count > 0) Stack.Pop();
                    var labelName = InstructionLabel(instruction.Offset + branchOffset + instrSize);

                    if (_ushortInAX)
                    {
                        int adjusted = cmpVal + 1;
                        if (adjusted > ushort.MaxValue)
                        {
                            _ushortInAX = false;
                            _runtimeValueInA = false;
                            break;
                        }
                        EmitBranch16Bit(adjusted, labelName, Opcode.BCS, isShort);
                        break;
                    }

                    if (!EmitBranchCompare(cmpVal, adjustValue: 1))
                    {
                        // Overflow: x > 255 is always false for bytes → skip branch (no-op)
                    }
                    else if (isShort)
                        EmitWithLabel(Opcode.BCS, AddressMode.Relative, labelName);
                    else
                    {
                        Emit(Opcode.BCC, AddressMode.Relative, 3);
                        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    }
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Brtrue:
                // Long-form branch if non-zero — use trampoline: BEQ +3, JMP target
                {
                    var labelName = InstructionLabel(instruction.Offset + operand + 5);
                    if (_ushortInAX)
                    {
                        // 16-bit non-zero check: combine A (lo) and X (hi) via ORA
                        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.ORA, AddressMode.ZeroPage, TEMP);
                        _ushortInAX = false;
                    }
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
                    var labelName = InstructionLabel(instruction.Offset + operand + 5);
                    if (_ushortInAX)
                    {
                        // 16-bit zero check: combine A (lo) and X (hi) via ORA
                        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
                        Emit(Opcode.ORA, AddressMode.ZeroPage, TEMP);
                        _ushortInAX = false;
                    }
                    Emit(Opcode.BNE, AddressMode.Relative, 3); // skip JMP if non-zero
                    EmitWithLabel(Opcode.JMP, AddressMode.Absolute, labelName);
                    if (Stack.Count > 0)
                        Stack.Pop();
                    _runtimeValueInA = false;
                }
                break;
            case ILOpCode.Ldloca_s:
                // Load address of local variable — used for struct field access
                if (ClosureStructLocalIndex >= 0 && operand == ClosureStructLocalIndex
                    && Instructions is not null && ClosureFieldTypes != null)
                {
                    // Determine if this ldloca.s is for closure field init (stfld) or
                    // a method call. Scan forward: if we hit stfld/ldfld for a closure
                    // field first, it's initialization. If we hit call first, it's a
                    // method invocation — skip (closure ref is implicit).
                    bool isInit = false;
                    for (int k = Index + 1; k < Math.Min(Index + 12, Instructions.Length); k++)
                    {
                        if (Instructions[k].OpCode is ILOpCode.Stfld or ILOpCode.Ldfld
                            && Instructions[k].String is string fn
                            && ClosureFieldTypes.ContainsKey(fn))
                        {
                            isInit = true;
                            break;
                        }
                        if (Instructions[k].OpCode == ILOpCode.Call)
                            break;
                    }
                    if (!isInit)
                        break; // Skip — closure ref before a call
                }
                _pendingStructLocal = operand;
                break;
            case ILOpCode.Ldelema:
                HandleLdelema(instruction);
                break;
            case ILOpCode.Switch:
                HandleSwitch(instruction, operand);
                break;
            case ILOpCode.Sizeof:
                // Native integer size (nint/IntPtr) on the 6502 is 1 byte (8-bit CPU).
                // Fixed-size primitive types (byte, ushort, int, etc.) are folded by Roslyn at compile time
                // and typically never reach this opcode; this implementation only handles platform-dependent
                // native integer sizeof values and will always push 1 here.
                WriteLdc(1);
                break;
            default:
                throw new TranspileException(GetUnsupportedOpcodeMessage(instruction.OpCode), MethodName);
        }
        previous = instruction.OpCode;
    }

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
            case ILOpCode.Ldftn:
                // Function pointer: store the method name for the callback handler
                _lastLdftnMethod = operand;
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
                UsedMethods?.Add("pushax");
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

                            // Check if y is a runtime expression (e.g. row + 10):
                            // _runtimeValueInA is true AND there's a JSR pusha in the block
                            // (meaning x was pusha'd, and the expression result in A is y)
                            bool yIsExpression = false;
                            if (!yIsRuntime && _runtimeValueInA)
                            {
                                for (int bi = block.Count - 1; bi >= 0; bi--)
                                {
                                    if (block[bi].Opcode == Opcode.JSR &&
                                        block[bi].Operand is LabelOperand lbl && lbl.Label == "pusha")
                                    {
                                        yIsExpression = true;
                                        break;
                                    }
                                }
                            }

                            // Check if x (first arg) is runtime
                            bool xIsRuntime = false;
                            bool xFromFlag = false; // true when x is runtime via _runtimeValueInA (not LDA Absolute)
                            Instruction? xInstr = null;
                            if (!yIsRuntime && !yIsExpression && _runtimeValueInA)
                            {
                                // x came from a runtime expression (And/Or/Div etc.)
                                // WriteLdc for y was skipped, so no LDA #y in block
                                xIsRuntime = true;
                                xFromFlag = true;
                            }
                            else if (!yIsRuntime && !yIsExpression && block.Count >= 2)
                            {
                                var prevInstr = block[block.Count - 2];
                                if (prevInstr.Mode == AddressMode.Absolute && prevInstr.Opcode == Opcode.LDA)
                                {
                                    xIsRuntime = true;
                                    xInstr = prevInstr;
                                }
                            }

                            if (!yIsRuntime && !yIsExpression && !xIsRuntime)
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
                                if (yIsExpression)
                                {
                                    // Runtime y from expression (row + 10 etc.), x was pusha'd
                                    // A has the y expression result, cc65 stack has x
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
                                    EmitJSR("popa"); // A = x (from cc65 stack)
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP); // TEMP = x
                                    Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2); // A = y
                                }
                                else if (yIsRuntime)
                                {
                                    // Runtime y: block has [x load], JSR pusha, LDA $y_addr
                                    // x could be constant (ImmediateOperand) or runtime (AbsoluteOperand)
                                    if (block.Count >= 3
                                        && block[block.Count - 2].Opcode == Opcode.JSR
                                        && block[block.Count - 2].Operand is LabelOperand pushaLbl
                                        && pushaLbl.Label == "pusha")
                                    {
                                        var xLoadInstr = block[block.Count - 3];
                                        if (xLoadInstr.Operand is ImmediateOperand immX)
                                        {
                                            // Constant x (pusha'd), runtime y
                                            RemoveLastInstructions(3);
                                            Emit(Opcode.LDA, AddressMode.Immediate, immX.Value);
                                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                            block.Emit(lastInstr); // re-emit LDA $y_addr
                                        }
                                        else if (xLoadInstr.Mode == AddressMode.Absolute
                                            && xLoadInstr.Opcode == Opcode.LDA)
                                        {
                                            // Both runtime: directly load x and y without pusha/popa
                                            RemoveLastInstructions(3);
                                            block.Emit(xLoadInstr); // re-emit LDA $x_addr → A = x
                                            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP); // TEMP = x
                                            block.Emit(lastInstr); // re-emit LDA $y_addr → A = y
                                        }
                                        else
                                        {
                                            throw new TranspileException(
                                                $"Unsupported NTADR x argument pattern: {xLoadInstr.Operand?.GetType().Name}",
                                                MethodName);
                                        }
                                    }
                                    else if (block.Count >= 2)
                                    {
                                        // Scan backwards for JSR pusha or an LDA that represents x.
                                        // Block may have intervening STA/LDA from stloc (store-local)
                                        // when Roslyn inserts temp variables between the NTADR args.
                                        int pushaIdx2 = -1;
                                        int xLdaIdx = -1;
                                        for (int bi = block.Count - 2; bi >= 0; bi--)
                                        {
                                            if (block[bi].Opcode == Opcode.JSR
                                                && block[bi].Operand is LabelOperand staPushaLbl
                                                && staPushaLbl.Label == "pusha")
                                            {
                                                pushaIdx2 = bi;
                                                break;
                                            }
                                        }

                                        if (pushaIdx2 >= 0 && pushaIdx2 > 0)
                                        {
                                            // Found pusha with intervening stloc instructions
                                            var xLoadInstr2 = block[pushaIdx2 - 1];
                                            int instrToRemove2 = block.Count - (pushaIdx2 - 1);
                                            if (xLoadInstr2.Operand is ImmediateOperand immX2)
                                            {
                                                RemoveLastInstructions(instrToRemove2);
                                                Emit(Opcode.LDA, AddressMode.Immediate, immX2.Value);
                                                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                                block.Emit(lastInstr);
                                            }
                                            else if (xLoadInstr2.Mode == AddressMode.Absolute
                                                && xLoadInstr2.Opcode == Opcode.LDA)
                                            {
                                                RemoveLastInstructions(instrToRemove2);
                                                block.Emit(xLoadInstr2);
                                                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                                block.Emit(lastInstr);
                                            }
                                            else
                                            {
                                                throw new TranspileException(
                                                    $"Unsupported NTADR x argument pattern: {xLoadInstr2.Operand?.GetType().Name}",
                                                    MethodName);
                                            }
                                        }
                                        else
                                        {
                                            // No pusha found. Look for direct LDA (x) before the y load.
                                            // Scan backwards past STA/LDA pairs from stloc
                                            for (int bi = block.Count - 2; bi >= 0; bi--)
                                            {
                                                if (block[bi].Mode == AddressMode.Absolute
                                                    && block[bi].Opcode == Opcode.LDA)
                                                {
                                                    xLdaIdx = bi;
                                                    break;
                                                }
                                            }
                                            if (xLdaIdx >= 0)
                                            {
                                                var prevInstr = block[xLdaIdx];
                                                int instrToRemove2 = block.Count - xLdaIdx;
                                                RemoveLastInstructions(instrToRemove2);
                                                block.Emit(prevInstr); // re-emit LDA $x_addr
                                                Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                                block.Emit(lastInstr); // re-emit LDA $y_addr
                                            }
                                            else
                                            {
                                                throw new TranspileException(
                                                    $"Unsupported NTADR pattern: could not find x argument in block",
                                                    MethodName);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        throw new TranspileException(
                                            "Unsupported NTADR pattern: insufficient instructions in block for runtime y",
                                            MethodName);
                                    }
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
                    case nameof(NESLib.pad_poll):
                    case nameof(NESLib.pad_trigger):
                        // pad_poll/pad_trigger returns result in A — store to dynamically allocated temp
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
                        _lastByteArrayLabel = null;
                        _needsByteArrayLoadInCall = false;
                        break;
                    case nameof(NESLib.oam_spr_2x2):
                        EmitOamSpr2x2();
                        _lastByteArrayLabel = null;
                        _needsByteArrayLoadInCall = false;
                        break;
                    case nameof(NESLib.oam_meta_spr):
                        EmitOamMetaSpr();
                        break;
                    case nameof(NESLib.oam_meta_spr_pal):
                        EmitOamMetaSprPal();
                        break;
                    case nameof(NESLib.meta_spr_2x2):
                        HandleMetaSpr2x2(flip: false);
                        break;
                    case nameof(NESLib.meta_spr_2x2_flip):
                        HandleMetaSpr2x2(flip: true);
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
                    case nameof(NESLib.irq_set_callback):
                        {
                            // Function pointer path: ldftn already gave us the method name
                            string? labelName = _lastLdftnMethod;
                            _lastLdftnMethod = null;

                            if (labelName != null)
                            {
                                // User-defined methods use their name as-is; extern methods use _ prefix (cc65 convention)
                                bool isUserMethod = UserMethodNames != null && UserMethodNames.Contains(labelName);
                                string label = isUserMethod ? labelName : $"_{labelName}";
                                EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, label);
                                EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, label);
                            }
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            if (operand == nameof(NESLib.nmi_set_callback))
                                UsedMethods?.Add("nmi_set_callback");
                            if (operand == nameof(NESLib.irq_set_callback))
                                UsedMethods?.Add("irq_set_callback");
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.famitone_init):
                    case nameof(NESLib.sfx_init):
                        {
                            // String literal path: Ldstr emitted LDA #<string_N, LDX #>string_N, JSR pushax, LDX #0, LDA #len
                            string? labelName = null;
                            var block = CurrentBlock!;
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
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, $"_{operand}");
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.cli):
                        // Emit 6502 CLI instruction (enable CPU interrupts)
                        Emit(Opcode.CLI, AddressMode.Implied);
                        argsAlreadyPopped = true;
                        break;
                    case nameof(NESLib.sei):
                        // Emit 6502 SEI instruction (disable CPU interrupts)
                        Emit(Opcode.SEI, AddressMode.Implied);
                        argsAlreadyPopped = true;
                        break;
                    case nameof(NESLib.cnrom_set_chr_bank):
                        // CNROM (mapper 3) bank switch: write bank number to $8000
                        // The bank number is already in A from the argument load
                        Emit(Opcode.STA, AddressMode.Absolute, (ushort)0x8000);
                        _immediateInA = null;
                        _pokeLastValue = null;
                        break;
                    case nameof(NESLib.mmc3_set_chr_bank):
                        {
                            // mmc3_set_chr_bank(byte reg, byte bank) -> STA $8000 (reg), STA $8001 (bank)
                            // MMC3 CHR bank switching: write register number to $8000, then bank number to $8001.
                            // reg must be a compile-time constant (register selector 0-7).
                            if (Stack.Count >= 2)
                            {
                                int bank = Stack.Pop();
                                int reg = Stack.Pop();

                                // Verify reg was a constant by checking the block pattern:
                                // LDA #reg, JSR pusha, LDA #bank (or LDA $bank_addr)
                                var block = CurrentBlock!;
                                if (block.Count < 3
                                    || block[block.Count - 3].Opcode != Opcode.LDA
                                    || block[block.Count - 3].Mode != AddressMode.Immediate)
                                {
                                    throw new TranspileException(
                                        "mmc3_set_chr_bank: first argument (reg) must be a compile-time constant.",
                                        MethodName);
                                }

                                // Check if bank (last loaded arg) is from a local variable
                                Local? bankLocal = null;
                                bool bankIsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out bankLocal) &&
                                    bankLocal.Address.HasValue;

                                // Remove previously emitted instructions:
                                // LDA #reg, JSR pusha, LDA #bank (or LDA $bank_addr) = 3 instructions
                                RemoveLastInstructions(3);

                                // Write register number to $8000 (bank select)
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)reg);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC3_BANK_SELECT);

                                // Write bank number to $8001 (bank data)
                                if (bankIsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)bankLocal!.Address!.Value);
                                }
                                else
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)bank);
                                }
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC3_BANK_DATA);
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            _immediateInA = null;
                            _pokeLastValue = null;
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

                                // Check if the value is from a runtime local variable
                                Local? pokeLocal = null;
                                bool valueIsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out pokeLocal) &&
                                    pokeLocal.Address.HasValue;

                                // Check if the value is from a static field
                                bool valueIsStaticField = _lastStaticFieldAddress.HasValue;

                                // Remove previously emitted instructions:
                                // ushort addr: LDX #hi, LDA #lo, JSR pushax, LDA #value = 4 instructions
                                // byte addr:   LDA #lo, JSR pusha, LDA #value = 3 instructions
                                RemoveLastInstructions(addr > byte.MaxValue ? 4 : 3);

                                if (valueIsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)pokeLocal!.Address!.Value);
                                    _pokeLastValue = null;
                                    _immediateInA = null;
                                }
                                else if (valueIsStaticField)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, _lastStaticFieldAddress!.Value);
                                    _pokeLastValue = null;
                                    _immediateInA = null;
                                }
                                else if (_pokeLastValue != (byte)value)
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)value);
                                    _pokeLastValue = (byte)value;
                                    _immediateInA = (byte)value;
                                }
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr);
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.peek):
                        {
                            // peek(ushort addr) -> LDA abs addr
                            if (Stack.Count >= 1)
                            {
                                int addr = Stack.Pop();
                                // Remove previously emitted instructions:
                                // ushort addr: LDX #hi, LDA #lo = 2 instructions
                                // byte addr:   LDA #lo = 1 instruction
                                RemoveLastInstructions(addr > byte.MaxValue ? 2 : 1);
                                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)addr);
                                _runtimeValueInA = true;
                                _immediateInA = null;
                                _pokeLastValue = null;
                            }
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.mmc1_write):
                        {
                            // mmc1_write(ushort addr, byte value) -> serial shift register write
                            // MMC1 requires writing 5 bits one at a time via STA to the register address.
                            // Protocol: reset with bit 7 set, then write 5 bits using LSR A between each STA.
                            if (Stack.Count >= 2)
                            {
                                int value = Stack.Pop();
                                int addr = Stack.Pop();

                                // Check if the value is from a runtime local variable
                                Local? writeLocal = null;
                                bool valueIsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out writeLocal) &&
                                    writeLocal.Address.HasValue;

                                // Check if the value is from a static field
                                bool valueIsStaticField = _lastStaticFieldAddress.HasValue;

                                // Remove previously emitted arg setup instructions:
                                // LDX #hi, LDA #lo, JSR pusha/pushax, LDA #value = 4 instructions
                                RemoveLastInstructions(4);

                                // Reset the shift register by writing with bit 7 set
                                Emit(Opcode.LDA, AddressMode.Immediate, 0x80);
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr);

                                // Load the 5-bit value
                                if (valueIsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)writeLocal!.Address!.Value);
                                }
                                else if (valueIsStaticField)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, _lastStaticFieldAddress!.Value);
                                }
                                else
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)value);
                                }

                                // Write 5 bits via STA + LSR sequence
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr); // write 1 (bit 0)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr); // write 2 (bit 1)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr); // write 3 (bit 2)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr); // write 4 (bit 3)
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, (ushort)addr); // write 5 (bit 4, latches)
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            _pokeLastValue = null;
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.mmc1_set_prg_bank):
                        {
                            // mmc1_set_prg_bank(byte bank) -> mmc1_write(0xE000, bank)
                            if (Stack.Count >= 1)
                            {
                                int bank = Stack.Pop();

                                // Check if the value is from a runtime local variable
                                Local? bankLocal = null;
                                bool valueIsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out bankLocal) &&
                                    bankLocal.Address.HasValue;

                                // Check if the value is from a static field
                                bool valueIsStaticField = _lastStaticFieldAddress.HasValue;

                                // Remove previously emitted LDA #value instruction
                                RemoveLastInstructions(1);

                                // Reset the shift register
                                Emit(Opcode.LDA, AddressMode.Immediate, 0x80);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);

                                // Load the bank value
                                if (valueIsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)bankLocal!.Address!.Value);
                                }
                                else if (valueIsStaticField)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, _lastStaticFieldAddress!.Value);
                                }
                                else
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)bank);
                                }

                                // Write 5 bits to PRG bank register
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_PRG_BANK);
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            _pokeLastValue = null;
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.mmc1_set_chr_bank):
                        {
                            // mmc1_set_chr_bank(byte bank0, byte bank1) -> write to CHR bank 0 and CHR bank 1
                            if (Stack.Count >= 2)
                            {
                                int bank1 = Stack.Pop();
                                int bank0 = Stack.Pop();

                                // Check if bank1 (last loaded arg) is from a local or static field
                                Local? bank1Local = null;
                                bool bank1IsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out bank1Local) &&
                                    bank1Local.Address.HasValue;
                                bool bank1IsStaticField = _lastStaticFieldAddress.HasValue;
                                ushort? bank1StaticAddr = _lastStaticFieldAddress;

                                // Remove previously emitted instructions:
                                // LDA #bank0, JSR pusha, LDA #bank1 = 3 instructions
                                RemoveLastInstructions(3);

                                // Write bank0 to CHR bank 0 register ($A000)
                                // Note: bank0 local/static tracking is lost after pusha — only constants supported
                                Emit(Opcode.LDA, AddressMode.Immediate, 0x80);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);
                                Emit(Opcode.LDA, AddressMode.Immediate, (byte)bank0);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK0);

                                // Write bank1 to CHR bank 1 register ($C000)
                                Emit(Opcode.LDA, AddressMode.Immediate, 0x80);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                                if (bank1IsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)bank1Local!.Address!.Value);
                                }
                                else if (bank1IsStaticField)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, bank1StaticAddr!.Value);
                                }
                                else
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)bank1);
                                }
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CHR_BANK1);
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            _pokeLastValue = null;
                            _immediateInA = null;
                            argsAlreadyPopped = true;
                        }
                        break;
                    case nameof(NESLib.mmc1_set_mirroring):
                        {
                            // mmc1_set_mirroring(byte mode) -> mmc1_write(0x8000, mode)
                            if (Stack.Count >= 1)
                            {
                                int mode = Stack.Pop();

                                // Check if the value is from a runtime local variable
                                Local? modeLocal = null;
                                bool valueIsLocal = _lastLoadedLocalIndex.HasValue &&
                                    Locals.TryGetValue(_lastLoadedLocalIndex.Value, out modeLocal) &&
                                    modeLocal.Address.HasValue;

                                // Check if the value is from a static field
                                bool valueIsStaticField = _lastStaticFieldAddress.HasValue;

                                // Remove previously emitted LDA #value instruction
                                RemoveLastInstructions(1);

                                // Reset the shift register
                                Emit(Opcode.LDA, AddressMode.Immediate, 0x80);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);

                                // Load the mirroring mode value
                                if (valueIsLocal)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, (ushort)modeLocal!.Address!.Value);
                                }
                                else if (valueIsStaticField)
                                {
                                    Emit(Opcode.LDA, AddressMode.Absolute, _lastStaticFieldAddress!.Value);
                                }
                                else
                                {
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)mode);
                                }

                                // Write 5 bits to Control register
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);
                                Emit(Opcode.LSR, AddressMode.Accumulator);
                                Emit(Opcode.STA, AddressMode.Absolute, NESLib.MMC1_CONTROL);
                            }
                            _lastLoadedLocalIndex = null;
                            _lastStaticFieldAddress = null;
                            _pokeLastValue = null;
                            _immediateInA = null;
                            argsAlreadyPopped = true;
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
                    case nameof(NESLib.bcd_add):
                        // bcd_add(ushort a, ushort b) — first arg on cc65 stack via pushax,
                        // second arg in A:X. Both args are 16-bit, so when the source value
                        // is a byte constant, X must be cleared to 0.
                        {
                            var block = CurrentBlock!;
                            var lastInstr = block[block.Count - 1];

                            // Detect if second arg is ushort (LDX before/after LDA).
                            bool secondIsUshort = lastInstr.Opcode == Opcode.LDX
                                || (block.Count >= 2 && block[block.Count - 2].Opcode == Opcode.LDX
                                    && block[block.Count - 2].Mode == AddressMode.Immediate);
                            int secondArgSize = secondIsUshort ? 2 : 1;
                            int secondArgStart = block.Count - secondArgSize;

                            // Save second arg instructions before modifying the block
                            var savedSecond = new Instruction[secondArgSize];
                            for (int si = 0; si < secondArgSize; si++)
                                savedSecond[si] = block[secondArgStart + si];

                            // Check what pushed the first arg
                            int pushIdx = secondArgStart - 1;
                            bool alreadyPushax = pushIdx >= 0
                                && block[pushIdx].Opcode == Opcode.JSR
                                && block[pushIdx].Operand is LabelOperand bcdLbl && bcdLbl.Label == "pushax";
                            bool hasPusha = pushIdx >= 0
                                && block[pushIdx].Opcode == Opcode.JSR
                                && block[pushIdx].Operand is LabelOperand bcdLbl2 && bcdLbl2.Label == "pusha";

                            if (alreadyPushax)
                            {
                                // First arg already pushed correctly via pushax.
                                // For byte-sized second args, clear X.
                                RemoveLastInstructions(secondArgSize);
                                if (!secondIsUshort)
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                foreach (var si in savedSecond)
                                    block.Emit(si);
                            }
                            else if (hasPusha)
                            {
                                // First arg was byte-sized (pusha). Replace with LDX #$00 + pushax.
                                RemoveLastInstructions(secondArgSize + 1); // remove pusha + second arg
                                Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                EmitJSR("pushax");
                                if (!secondIsUshort)
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                foreach (var si in savedSecond)
                                    block.Emit(si);
                            }
                            else
                            {
                                // No push found — first arg is in registers.
                                // If preceding instruction is LDX absolute, first arg is ushort
                                // (A:X already set). Otherwise byte (X undefined, needs clearing).
                                bool firstIsUshort = pushIdx >= 0
                                    && block[pushIdx].Opcode == Opcode.LDX
                                    && block[pushIdx].Mode == AddressMode.Absolute;
                                RemoveLastInstructions(secondArgSize);
                                if (!firstIsUshort)
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                EmitJSR("pushax");
                                if (!secondIsUshort)
                                    Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
                                foreach (var si in savedSecond)
                                    block.Emit(si);
                            }
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                            UsedMethods?.Add("pushax");
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
                    case nameof(NESLib.vrambuf_put_vert):
                        {
                            var block = CurrentBlock!;
                            bool isVertical = operand == nameof(NESLib.vrambuf_put_vert);

                            // Detect byte array overload via _lastLoadedLocalIndex
                            Local? arrayLocal = null;
                            bool isByteArrayOverload = _lastLoadedLocalIndex.HasValue &&
                                Locals.TryGetValue(_lastLoadedLocalIndex.Value, out arrayLocal) &&
                                arrayLocal.ArraySize > 0;

                            if (isByteArrayOverload)
                            {
                                // vrambuf_put(addr, buf, len) — byte array overload
                                int len = checked((int)Stack.Pop());    // length
                                Stack.Pop();                            // array size placeholder
                                int addr = checked((int)Stack.Pop());   // NTADR result

                                ushort arrayAddr = (ushort)arrayLocal!.Address!;

                                // Remove ldloc buf (LDA $arrayAddr) + ldc len (LDA #len)
                                RemoveLastInstructions(2);

                                if (_ntadrRuntimeResult)
                                {
                                    // TEMP/TEMP2 already set by NTADR handler
                                    if (isVertical)
                                    {
                                        // OR TEMP (hi) with $80 for vertical sequential
                                        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
                                        Emit(Opcode.ORA, AddressMode.Immediate, NT_UPD_VERT);
                                        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                    }
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
                                    if (isVertical)
                                        Emit(Opcode.ORA, AddressMode.Immediate, NT_UPD_VERT);
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
                                    Emit(Opcode.LDA, AddressMode.Immediate, isVertical ? (byte)(addrHi | NT_UPD_VERT) : addrHi);
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
                                // Always call the vrambuf_put subroutine (shared)
                                if (isVertical)
                                    UsedMethods!.Add(nameof(NESLib.vrambuf_put));
                                EmitWithLabel(Opcode.JSR, AddressMode.Absolute, nameof(NESLib.vrambuf_put));
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
                                    Emit(Opcode.ORA, AddressMode.Immediate, NT_UPD_HORZ);
                                    Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
                                }
                                else
                                {
                                    byte addrHi = (byte)(addr >> 8);
                                    byte addrLo = (byte)(addr & 0xFF);
                                    RemoveLastInstructions(7);
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(addrHi | NT_UPD_HORZ));
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
                    case "IntPtr.get_Size":
                        // 6502 is an 8-bit CPU: native integer size is 1 byte
                        WriteLdc(1);
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
                        else if (_ldlocByteArrayLabel != null)
                        {
                            // pushax was kept (not a fastcall function) — track its usage
                            UsedMethods?.Add("pushax");
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
                                // vram_write/vram_read use cc65 calling convention: pointer on stack, size in A:X
                                if (operand is nameof(NESLib.vram_write) or nameof(NESLib.vram_read))
                                {
                                    EmitJSR("pushax");
                                    UsedMethods?.Add("pushax");
                                    Emit(Opcode.LDX, AddressMode.Immediate, (byte)(_lastByteArraySize >> 8));
                                    Emit(Opcode.LDA, AddressMode.Immediate, (byte)(_lastByteArraySize & 0xFF));
                                }
                                _needsByteArrayLoadInCall = false;
                                // Label consumed by this call — clear to prevent leaking
                                // to subsequent stlocs and function calls
                                _lastByteArrayLabel = null;
                                _lastByteArraySize = 0;
                            }
                        }
                        // Emit JSR — extern methods use cc65 _prefix convention
                        if (ExternMethodNames.Contains(operand))
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, $"_{operand}");
                        else
                            EmitWithLabel(Opcode.JSR, AddressMode.Absolute, operand);
                        _immediateInA = null;
                        _pokeLastValue = null;
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
                        // A now has a new return value; any previous pad_poll result is gone.
                        // pad_poll sets its own flag after this block, so this only clears
                        // the flag for non-pad_poll calls (e.g. rand8).
                        if (operand != nameof(NESLib.pad_poll) && operand != nameof(NESLib.pad_trigger))
                        {
                            _padPollResultAvailable = false;
                        }
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
                // Clear NTADR result flag for all non-NTADR calls (result consumed or discarded)
                if (operand is not (nameof(NTADR_A) or nameof(NTADR_B) or nameof(NTADR_C) or nameof(NTADR_D)))
                    _ntadrRuntimeResult = false;
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
                else if (previous == ILOpCode.Newarr)
                {
                    // newarr → stsfld in Main(): allocate RAM array at static field address
                    int arraySize = Stack.Count > 0 ? Stack.Pop() : 0;
                    ushort arrayAddr = (ushort)(local + LocalCount);
                    LocalCount += arraySize;
                    _staticFieldArrayLocals[operand] = new Local(arraySize, arrayAddr, ArraySize: arraySize);
                    GetOrAllocateStaticField(operand);
                    _runtimeValueInA = false;
                    _immediateInA = null;
                }
                else if (previous == ILOpCode.Ldtoken && _lastByteArrayLabel != null)
                {
                    // ldtoken → stsfld: ROM byte array stored to static field
                    _staticFieldArrayLocals[operand] = new Local(_lastByteArraySize, LabelName: _lastByteArrayLabel);
                    _lastByteArrayLabel = null;
                    if (Stack.Count > 0) Stack.Pop();
                    GetOrAllocateStaticField(operand);
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
                throw new TranspileException(GetUnsupportedOpcodeMessage(instruction.OpCode), MethodName);
        }
        previous = instruction.OpCode;
    }

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
                throw new TranspileException(GetUnsupportedOpcodeMessage(instruction.OpCode), MethodName);
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
            var labelName = InstructionLabel(absoluteTarget);

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
}
