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
            throw new TranspileException($"Local variable holds a constant value ({local.Value}) that exceeds the maximum supported size of ushort ({ushort.MaxValue}). Only byte and ushort values are supported on the NES.", MethodName);
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
        if (local.LabelName is not null)
        {
            // This local holds a byte array label reference
            EmitWithLabel(Opcode.LDA, AddressMode.Immediate_LowByte, local.LabelName);
            EmitWithLabel(Opcode.LDX, AddressMode.Immediate_HighByte, local.LabelName);
            EmitJSR("pushax");
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
                    _savedConstantViaPusha = true;
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
                throw new TranspileException($"Local variable holds a constant value ({local.Value}) that exceeds the maximum supported size of ushort ({ushort.MaxValue}). Only byte and ushort values are supported on the NES.", MethodName);
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
}
