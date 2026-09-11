using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    void CaptureNumericBinaryOperands(ILInstruction instruction)
    {
        int lhs = _numericValues!.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (TryNumericOperands(out int left, out int right))
        {
            EmitNumericOperand(left);
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP_HI);
            EmitNumericOperand(right);
        }
        else if (lhs >= 0 && rhs == Index - 1 && Instructions![rhs].GetLdcValue().HasValue && _runtimeValueInA
            && (_ushortInAX || NumericType(lhs) is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte)
            && !ILBranchTargets.HasEntryAfter(Instructions, lhs, Index))
        {
            if (!_ushortInAX)
                EmitNumericExtension(SignedNumericType(NumericType(lhs)));
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP_HI);
            EmitNumericOperand(rhs);
        }
        else
            throw new TranspileException(
                $"Word {instruction.OpCode} at IL_{instruction.Offset:X4} needs materialized operands. " +
                "Store each operand in an explicitly typed byte, sbyte, short or ushort local.", MethodName);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP3);
    }

    bool TryNumericMultiply(ILInstruction instruction)
    {
        if (instruction.OpCode != ILOpCode.Mul || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2
            || !RequiresNumericWord(Index, new HashSet<int>()))
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (rhs >= 0 && Instructions![rhs].GetLdcValue() is > 0 and int factor
            && (factor & (factor - 1)) == 0 && NumericType(lhs) == PrimitiveTypeCode.Byte
            && Index + 1 < Instructions.Length
            && Instructions[Index + 1].OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2)
            return false;
        CaptureNumericBinaryOperands(instruction);
        Emit(Opcode.LDA, AddressMode.Immediate, 0);
        Emit(Opcode.TAX);
        Emit(Opcode.LDY, AddressMode.Immediate, 16);
        string loop = InstructionLabel(instruction.Offset) + "_multiply_loop";
        string skip = InstructionLabel(instruction.Offset) + "_multiply_skip";
        CurrentBlock!.SetNextLabel(loop);
        Emit(Opcode.LSR, AddressMode.ZeroPage, TEMP3);
        Emit(Opcode.ROR, AddressMode.ZeroPage, TEMP2);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, skip);
        Emit(Opcode.CLC);
        Emit(Opcode.ADC, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.PHA);
        Emit(Opcode.TXA);
        Emit(Opcode.ADC, AddressMode.ZeroPage, TEMP_HI);
        Emit(Opcode.TAX);
        Emit(Opcode.PLA);
        CurrentBlock.SetNextLabel(skip);
        Emit(Opcode.ASL, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP_HI);
        Emit(Opcode.DEY);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, loop);
        FinishNumericBinary(instruction);
        return true;
    }

    bool TryNumericBitwise(ILInstruction instruction)
    {
        if (instruction.OpCode is not (ILOpCode.And or ILOpCode.Or or ILOpCode.Xor)
            || _numericValues == null || _numericValues.Inputs[Index].Length != 2
            || Index + 1 < Instructions!.Length && _numericValues.Inputs[Index + 1].Length == 2
                && _numericValues.Inputs[Index + 1][1] == Index
                && Instructions[Index + 1].OpCode is ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un
            || !RequiresNumericWord(Index, new HashSet<int>()))
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (!WordNumericType(NumericType(lhs)) && !WordNumericType(NumericType(rhs))
            && (NumericType(Index) == PrimitiveTypeCode.Byte
                || !SignedNumericType(NumericType(lhs)) && !SignedNumericType(NumericType(rhs))))
            return false;
        var opcode = instruction.OpCode switch
        {
            ILOpCode.And => Opcode.AND,
            ILOpCode.Or => Opcode.ORA,
            _ => Opcode.EOR,
        };
        if (rhs >= 0 && Instructions![rhs].GetLdcValue() is int mask && _runtimeValueInA && _ushortInAX)
            Emit16BitBitwiseOp(opcode, mask);
        else
        {
            CaptureNumericBinaryOperands(instruction);
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
            Emit(opcode, AddressMode.ZeroPage, TEMP2);
            Emit(Opcode.PHA);
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP_HI);
            Emit(opcode, AddressMode.ZeroPage, TEMP3);
            Emit(Opcode.TAX);
            Emit(Opcode.PLA);
        }
        FinishNumericBinary(instruction);
        _lastStaticFieldAddress = null;
        return true;
    }
}
