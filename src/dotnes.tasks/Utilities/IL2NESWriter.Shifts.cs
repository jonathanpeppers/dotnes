using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    int? _variableShiftIndex;
    bool _variableShiftWord;

    void BeginVariableShiftCount()
    {
        if (_variableShiftIndex == Index + 2)
        {
            // The count load is live in A. Keep ldc.i4 31 from overwriting it.
            _runtimeValueInA = true;
            return;
        }
        if (Instructions is null || Index + 4 >= Instructions.Length ||
            Instructions[Index + 1].GetLdcValue() != 31 ||
            Instructions[Index + 2].OpCode != ILOpCode.And ||
            Instructions[Index + 3].OpCode is not (ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un) ||
            Instructions[Index + 4].OpCode != ILOpCode.Conv_u1)
            return;

        var count = Instructions[Index];
        bool isScalarLoad = count.GetLdlocIndex() is int localIndex &&
            Locals.TryGetValue(localIndex, out var local) && local.Address.HasValue &&
            local.ArraySize == 0 && local.LabelName is null;
        isScalarLoad |= count.OpCode is ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1
            or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or ILOpCode.Ldarg_s;
        isScalarLoad |= count.OpCode == ILOpCode.Ldsfld && count.String is not null &&
            !_staticFieldArrayLocals.ContainsKey(count.String);
        if (!isScalarLoad || Stack.Count == 0 || Index == 0)
            return;
        // A ushort cast does not prove that preceding arithmetic preserved its high bits.
        int valueIndex = Index - 1;
        while (valueIndex > 0 && Instructions[valueIndex].OpCode == ILOpCode.Conv_u2)
            valueIndex--;
        var value = Instructions[valueIndex];
        if (value.GetLdcValue() is < 0)
            throw new TranspileException("Variable shifts of negative integer constants are not supported.", MethodName);
        if (value.GetLdlocIndex() is not null || NumericArgIndex(value) is not null
            || value.OpCode == ILOpCode.Ldsfld)
        {
            var type = NumericType(valueIndex);
            bool explicitlyUnsigned = valueIndex < Index - 1
                && type is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16;
            if (!explicitlyUnsigned && SignedNumericType(type))
                throw new TranspileException(
                    "Variable shifts of signed values require a constant count. Runtime signed shift counts " +
                    "are not supported by the NES numeric backend.", MethodName);
            if (type is not (PrimitiveTypeCode.Byte or PrimitiveTypeCode.Boolean or PrimitiveTypeCode.UInt16)
                && !explicitlyUnsigned)
                throw new TranspileException(
                    $"Variable shifts require a proven byte/ushort source; source type '{type?.ToString() ?? "unknown"}' " +
                    "is not supported here. Use an explicit supported conversion only if its truncation is intended.",
                    MethodName);
        }
        if (ILBranchTargets.HasEntryAfter(Instructions, valueIndex, Index) ||
            (value.GetLdcValue() is null && value.GetLdlocIndex() is null &&
            value.OpCode is not (ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
                or ILOpCode.Ldarg_s or ILOpCode.Ldsfld or ILOpCode.Conv_u1) &&
            !(valueIndex < Index - 1 && HasVerifiedNumericProducer(valueIndex))
            && !HasVerifiedPromotedNumericProducer(valueIndex)))
            throw new TranspileException(
                "Variable shifts of promoted arithmetic expressions are not supported. Explicitly truncate to byte, or use supported 16-bit operands before shifting.",
                MethodName);

        // C# masks an int shift count with 31. Preserve the promoted value before
        // evaluating that count; its tracked Stack entry is not a runtime value.
        _variableShiftIndex = Index + 3;
        _variableShiftWord = _ushortInAX;
        Emit(Opcode.PHA);
        if (_variableShiftWord)
        {
            Emit(Opcode.TXA);
            Emit(Opcode.PHA);
        }
        _accState = AccumulatorState.Empty;
        _lastLoadedLocalIndex = null;
        _savedRuntimeToTemp = false;
        _savedConstantViaPusha = false;
    }

    bool EmitVariableShift(bool left)
    {
        if (_variableShiftIndex != Index)
        {
            if (Instructions is not null && Index + 1 < Instructions.Length &&
                Instructions[Index + 1].OpCode is ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un)
                throw new TranspileException(GetUnsupportedOpcodeMessage(Instructions[Index + 1].OpCode), MethodName);
            if (Instructions is not null && Index > 0 &&
                Instructions[Index - 1].GetLdcValue() is null)
                throw new TranspileException(
                    "Variable shifts require a scalar byte/ushort value, a local, parameter, or static-field count, and a byte result. More complex shift expressions are not supported.",
                    MethodName);
            return false;
        }

        Stack.Pop(); // count
        Stack.Pop(); // value
        Emit(Opcode.TAY);
        if (_variableShiftWord)
        {
            Emit(Opcode.PLA);
            Emit(Opcode.TAX);
        }
        Emit(Opcode.PLA);
        Emit(Opcode.CPY, AddressMode.Immediate, 0);
        string done = InstructionLabel(Instructions![Index].Offset) + "_shift_done";
        string loop = InstructionLabel(Instructions[Index].Offset) + "_shift_loop";
        EmitWithLabel(Opcode.BEQ, AddressMode.Relative, done);
        CurrentBlock!.SetNextLabel(loop);
        if (!left && _variableShiftWord)
        {
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.LSR, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.ROR, AddressMode.Accumulator);
            Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
        }
        else
        {
            Emit(left ? Opcode.ASL : Opcode.LSR, AddressMode.Accumulator);
        }
        Emit(Opcode.DEY);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, loop);
        CurrentBlock.SetNextLabel(done);
        Emit(Opcode.CMP, AddressMode.Immediate, 0);
        _variableShiftIndex = null;
        _ushortInAX = false;
        _runtimeValueInA = true;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        _savedRuntimeToTemp = false;
        _savedConstantViaPusha = false;
        Stack.Push(0);
        return true;
    }
}
