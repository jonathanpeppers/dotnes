using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static NES.NESLib;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;
using Local = dotnes.LocalVariableManager.Local;

namespace dotnes;

/// <summary>
/// Local variable management — zero-page allocation, load/store operations.
/// </summary>
partial class IL2NESWriter
{
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

    void WriteStloc(Local local, bool isNewAllocation = true)
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
            if (isNewAllocation) LocalCount += 2;
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
                if (isNewAllocation) LocalCount += 2;
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.STX, AddressMode.Absolute, (ushort)(local.Address + 1));
            }
            else
            {
                // A has the runtime value — just store it
                if (isNewAllocation) LocalCount += 1;
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            }
            _runtimeValueInA = false;
            _savedRuntimeToTemp = false;
            _immediateInA = null;
        }
        else if (local.IsWord)
        {
            if (isNewAllocation) LocalCount += 2;
            // Word local (e.g. ushort x = 0): store low byte in A, high byte = 0
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
            Emit(Opcode.LDA, AddressMode.Immediate, 0x00);
            Emit(Opcode.STA, AddressMode.Absolute, (ushort)(local.Address + 1));
            _immediateInA = 0x00;
        }
        else if (local.Value <= byte.MaxValue)
        {
            if (isNewAllocation) LocalCount += 1;
            if (DeferredByteArrayMode)
            {
                // New pattern: just emit STA (keep the LDA from WriteLdc)
                Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                // STA doesn't change A, so _immediateInA stays valid
                _needsByteArrayLoadInCall = true;
            }
            else
            {
                if (LastLDA)
                {
                    // Store scalar local: remove previous LDA #constant, re-emit with STA
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
                }
                else
                {
                    // Previous instruction loaded a value into A (e.g., ldloc from another local)
                    // Don't remove it — just emit STA to store it
                    Emit(Opcode.STA, AddressMode.Absolute, (ushort)local.Address);
                }
                _immediateInA = null;
            }
        }
        else if (local.Value <= ushort.MaxValue)
        {
            if (isNewAllocation) LocalCount += 2;
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
            throw new TranspileException($"Local variable holds a constant value ({local.Value}) that exceeds the maximum supported size of ushort ({ushort.MaxValue}). Only byte and ushort values are supported on the NES.", MethodName);
        }
    }

    void WriteLdc(ushort operand)
    {
        _lastStaticFieldAddress = null;
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
            UsedMethods?.Add("pushax");
            _ushortInAX = false;
            _immediateInA = null;
        }
        if (_runtimeValueInA)
        {
            // Don't emit LDA — the runtime value in A must be preserved.
            // The constant is tracked on the Stack for the next operation (AND, ADD, SUB, etc.)
            // NOTE: This check must come BEFORE the LastLDA/pusha check below.
            // When _runtimeValueInA is true, we return early without emitting a new LDA,
            // so there's no need to save A via pusha (it won't be overwritten).
            // Emitting pusha here would leak stack bytes that are never popped.
            Stack.Push(operand);
            return;
        }
        else if (LastLDA)
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
        _ushortInAX = false;
        _savedConstantViaPusha = false;
        _lastStaticFieldAddress = null;
        if (local.LabelName is not null)
        {
            // This local holds a byte array label reference
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, local.LabelName);
            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, local.LabelName);
            EmitJSR("pushax");
            UsedMethods?.Add("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(local.Value >> 8));
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(local.Value & 0xFF)); // Size of array (16-bit)
            _immediateInA = (byte)(local.Value & 0xFF);
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
            else if (local.Value <= byte.MaxValue)
            {
                if (_runtimeValueInA && !LastLDA)
                {
                    Emit(Opcode.STA, AddressMode.ZeroPage, (byte)NESConstants.TEMP);
                    _savedRuntimeToTemp = true;
                }
                else if (LastLDA)
                {
                    EmitJSR("pusha");
                    _savedConstantViaPusha = true;
                }
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                _immediateInA = null;
                // Look ahead: if the next instruction loads another value and the
                // upcoming Call is to a user-defined function, push the current A
                // value to the cc65 stack so it survives the next load.
                // Only applies to user-defined functions — NESLib built-ins have
                // dedicated Call handlers that manage their own arguments.
                if (Instructions is not null && Index + 1 < Instructions.Length
                    && UserMethodNames is { Count: > 0 })
                {
                    var nextOp = Instructions[Index + 1].OpCode;
                    bool nextIsLoad = nextOp is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
                        or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s
                        or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                        or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                        or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8
                        or ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4_m1
                        or ILOpCode.Ldsfld;
                    if (nextIsLoad)
                    {
                        // Track IL stack depth to distinguish between:
                        // 1. Arithmetic that consumes our loaded value (depth drops to 0) → not a call arg
                        // 2. Arithmetic that only consumes later-loaded values (depth > 0) → computed arg
                        // depth starts at 1 for the load at Index+1
                        int depth = 1;
                        // Scan ahead to find the Call instruction that consumes these args
                        for (int scan = Index + 2; scan < Instructions.Length; scan++)
                        {
                            var scanOp = Instructions[scan].OpCode;
                            if (scanOp == ILOpCode.Call)
                            {
                                string? callTarget = Instructions[scan].String;
                                if (callTarget != null && UserMethodNames.Contains(callTarget))
                                {
                                    EmitJSR("pusha");
                                }
                                break;
                            }
                            // Track loads — each pushes one value onto the IL stack
                            if (scanOp is ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1
                                or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s
                                or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                                or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5
                                or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8
                                or ILOpCode.Ldc_i4 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4_m1
                                or ILOpCode.Ldsfld)
                            {
                                depth++;
                                continue;
                            }
                            // Binary arithmetic: pops 2, pushes 1 → net -1.
                            // If depth drops to 0, the arithmetic consumes our value → not a call arg.
                            if (scanOp is ILOpCode.Add or ILOpCode.Sub
                                or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un
                                or ILOpCode.Rem or ILOpCode.Rem_un
                                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                                or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un)
                            {
                                depth--;
                                if (depth <= 0)
                                    break;
                                continue;
                            }
                            // Unary conversions: pop 1, push 1 → net 0. Continue scanning.
                            if (scanOp is ILOpCode.Conv_u1 or ILOpCode.Conv_i4
                                or ILOpCode.Conv_u2 or ILOpCode.Conv_i2
                                or ILOpCode.Conv_i1 or ILOpCode.Conv_u4)
                                continue;
                            // Stop at branches/stores — not in argument loading
                            if (scanOp is ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                                or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s
                                or ILOpCode.Br or ILOpCode.Br_s
                                or ILOpCode.Brfalse or ILOpCode.Brfalse_s
                                or ILOpCode.Brtrue or ILOpCode.Brtrue_s
                                or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                                or ILOpCode.Beq or ILOpCode.Beq_s
                                or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
                                or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
                                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
                                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s
                                or ILOpCode.Ret)
                                break;
                        }
                    }
                }
            }
            else if (local.Value <= ushort.MaxValue)
            {
                EmitJSR("pusha");
                Emit(Opcode.LDA, AddressMode.Absolute, (ushort)local.Address);
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(local.Address + 1));
                _immediateInA = null;
                _ushortInAX = true;
            }
            else
            {
                throw new TranspileException($"Local variable holds a constant value ({local.Value}) that exceeds the maximum supported size of ushort ({ushort.MaxValue}). Only byte and ushort values are supported on the NES.", MethodName);
            }
        }
        else
        {
            // This is more like an inline constant value
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(local.Value & 0xff));
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(local.Value >> 8));
            EmitJSR("pushax");
            UsedMethods?.Add("pushax");
            Emit(Opcode.LDX, AddressMode.Immediate, 0x00);
            Emit(Opcode.LDA, AddressMode.Immediate, 0x40);
            _immediateInA = 0x40;
        }
        Stack.Push(local.Value);
    }

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

        // Calculate cc65 stack offset considering byte[] params take 2 bytes
        int offset = _argStackAdjust;
        for (int j = argIndex + 1; j < MethodParamCount; j++)
            offset += (j < ParamIsArray.Length && ParamIsArray[j]) ? 2 : 1;

        bool isArray = argIndex < ParamIsArray.Length && ParamIsArray[argIndex];
        if (isArray)
        {
            // byte[] param: load 16-bit pointer into A:X (lo in A, hi in X)
            Emit(Opcode.LDY, AddressMode.Immediate, (byte)(offset + 1));
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp); // high byte
            Emit(Opcode.TAX, AddressMode.Implied);                   // X = high byte
            Emit(Opcode.LDY, AddressMode.Immediate, (byte)offset);
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp); // A = low byte
            _ushortInAX = true;
        }
        else
        {
            Emit(Opcode.LDY, AddressMode.Immediate, (byte)offset);
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp);
        }
        _immediateInA = null;
        _runtimeValueInA = true;
        Stack.Push(0); // placeholder for runtime value
    }
}
