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
        else if (lhs >= 0 && Instructions![lhs].OpCode == ILOpCode.Call && WordNumericType(NumericType(lhs))
            && PureNumericOperand(rhs, out int first) && first == lhs + 1 && rhs == Index - 1
            && !_numericValues.Escapes[lhs] && _numericValues.Consumers[lhs].Count == 1
            && !ILBranchTargets.HasEntryAfter(Instructions, lhs, Index)
            && _blockCountAtILOffset.TryGetValue(Instructions[first].Offset, out int blockStart))
        {
            // Capture the complete word return before re-emitting the adjacent pure operand.
            RemoveLastInstructions(GetBufferedBlockCount() - blockStart);
            _argStackAdjust = _numericArgAdjust[Instructions[first].Offset];
            CurrentBlock!.SetNextLabel(InstructionLabel(Instructions[first].Offset));
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP_HI);
            EmitNumericOperand(rhs);
            _savedState = SavedValueState.None;
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

    bool TryUnsignedWordDivision(ILInstruction instruction)
    {
        if (instruction.OpCode is not (ILOpCode.Div or ILOpCode.Rem) || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (!WordNumericType(NumericType(lhs)) && !WordNumericType(NumericType(rhs)) && !_ushortInAX)
            return false;
        if (SignedNumericType(NumericType(lhs)) || SignedNumericType(NumericType(rhs)))
            throw new TranspileException(
                $"Signed {instruction.OpCode} at IL_{instruction.Offset:X4} is not supported " +
                "by the unsigned word routine. Use a supported signed operation instead.", MethodName);
        if (rhs >= 0 && !Instructions![rhs].GetLdcValue().HasValue && NumericType(rhs) == PrimitiveTypeCode.Byte)
        {
            EmitWordDivisionByRuntimeByte(instruction);
            return true;
        }
        if (rhs < 0 || Instructions![rhs].GetLdcValue() is not int divisor || divisor <= 0 || divisor > ushort.MaxValue)
            throw new TranspileException(
                $"Word {instruction.OpCode} at IL_{instruction.Offset:X4} requires a positive constant divisor " +
                "in the range 1..65535 or a nonzero byte divisor. Runtime word divisors are not supported.", MethodName);
        // Retain the existing compact division when its eight-bit remainder is sufficient.
        if (instruction.OpCode == ILOpCode.Div && _ushortInAX
            && (divisor <= 128 || (divisor & (divisor - 1)) == 0))
            return false;

        CaptureNumericBinaryOperands(instruction);
        Emit(Opcode.LDA, AddressMode.Immediate, 0);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP3);
        Emit(Opcode.LDY, AddressMode.Immediate, 16);
        string loop = InstructionLabel(instruction.Offset) + "_divide_loop";
        string subtract = InstructionLabel(instruction.Offset) + "_divide_subtract";
        string next = InstructionLabel(instruction.Offset) + "_divide_next";
        CurrentBlock!.SetNextLabel(loop);
        Emit(Opcode.ASL, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP_HI);
        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP3);
        // A shifted remainder can have a seventeenth bit, even for a word divisor.
        EmitWithLabel(Opcode.BCS, AddressMode.Relative, subtract);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP3);
        Emit(Opcode.CMP, AddressMode.Immediate, (byte)(divisor >> 8));
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, next);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, subtract);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.CMP, AddressMode.Immediate, (byte)divisor);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, next);
        CurrentBlock.SetNextLabel(subtract);
        Emit(Opcode.SEC);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.SBC, AddressMode.Immediate, (byte)divisor);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP3);
        Emit(Opcode.SBC, AddressMode.Immediate, (byte)(divisor >> 8));
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP3);
        Emit(Opcode.INC, AddressMode.ZeroPage, TEMP);
        CurrentBlock.SetNextLabel(next);
        Emit(Opcode.DEY);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, loop);
        Emit(Opcode.LDA, AddressMode.ZeroPage, (byte)(instruction.OpCode == ILOpCode.Div ? TEMP : TEMP2));
        Emit(Opcode.LDX, AddressMode.ZeroPage, (byte)(instruction.OpCode == ILOpCode.Div ? TEMP_HI : TEMP3));
        FinishNumericBinary(instruction);
        _lastStaticFieldAddress = null;
        return true;
    }

    void EmitWordDivisionByRuntimeByte(ILInstruction instruction)
    {
        CaptureNumericBinaryOperands(instruction);
        string start = InstructionLabel(instruction.Offset) + "_divide_start";
        string zero = InstructionLabel(instruction.Offset) + "_divide_by_zero";
        string loop = InstructionLabel(instruction.Offset) + "_divide_loop";
        string subtract = InstructionLabel(instruction.Offset) + "_divide_subtract";
        string next = InstructionLabel(instruction.Offset) + "_divide_next";
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, start);
        CurrentBlock!.SetNextLabel(zero);
        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, zero);
        CurrentBlock.SetNextLabel(start);
        Emit(Opcode.LDA, AddressMode.Immediate, 0);
        Emit(Opcode.LDY, AddressMode.Immediate, 16);
        CurrentBlock.SetNextLabel(loop);
        Emit(Opcode.ASL, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP_HI);
        Emit(Opcode.ROL, AddressMode.Accumulator);
        // Carry is the remainder's ninth bit, not a bit that may be discarded.
        EmitWithLabel(Opcode.BCS, AddressMode.Relative, subtract);
        Emit(Opcode.CMP, AddressMode.ZeroPage, TEMP2);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, next);
        CurrentBlock.SetNextLabel(subtract);
        Emit(Opcode.SBC, AddressMode.ZeroPage, TEMP2);
        Emit(Opcode.INC, AddressMode.ZeroPage, TEMP);
        CurrentBlock.SetNextLabel(next);
        Emit(Opcode.DEY);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, loop);
        if (instruction.OpCode == ILOpCode.Div)
        {
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP_HI);
        }
        else
            Emit(Opcode.LDX, AddressMode.Immediate, 0);
        FinishNumericBinary(instruction);
        _lastStaticFieldAddress = null;
    }
}
