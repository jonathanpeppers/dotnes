using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    bool RequiresNumericWord(int producer, HashSet<int> visiting)
    {
        if (!visiting.Add(producer))
            return false;
        return _numericValues!.Consumers[producer].Any(consumer =>
            ILValueAnalysis.IsBranch(Instructions![consumer].OpCode)
            || Instructions[consumer].OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2
                or ILOpCode.Ceq or ILOpCode.Clt or ILOpCode.Cgt
                or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Div or ILOpCode.Rem
            || Instructions[consumer].GetStlocIndex() is int local && WordLocals.Contains(local)
            || Instructions[consumer].OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul
                or ILOpCode.Shl or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                && RequiresNumericWord(consumer, visiting));
    }

    void FinishNumericBinary(ILInstruction instruction)
    {
        if (Stack.Count > 0) Stack.Pop();
        if (Stack.Count > 0) Stack.Pop();
        Stack.Push(0);
        _accState = AccumulatorState.RuntimeUshort;
        _verifiedWordResults.Add(Index);
        previous = instruction.OpCode;
    }

    bool TryNumericLeftShift(ILInstruction instruction)
    {
        if (instruction.OpCode is not (ILOpCode.Shl or ILOpCode.Mul) || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2
            || !RequiresNumericWord(Index, new HashSet<int>()))
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (rhs < 0 || Instructions![rhs].GetLdcValue() is not int count)
            return false;
        if (instruction.OpCode == ILOpCode.Mul)
        {
            if (count <= 0 || count > ushort.MaxValue || (count & (count - 1)) != 0)
                return false;
            if (NumericType(lhs) == PrimitiveTypeCode.Byte && Index + 1 < Instructions.Length
                && Instructions[Index + 1].OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2)
                return false;
            int factor = count;
            count = 0;
            while (factor > 1) { factor >>= 1; count++; }
        }
        if (TryNumericOperands(out int left, out _))
            EmitNumericOperand(left);
        else if (_runtimeValueInA)
        {
            if (!_ushortInAX)
                EmitNumericExtension(SignedNumericType(NumericType(lhs)));
        }
        else
            throw new TranspileException(
                $"Word left shift at IL_{instruction.Offset:X4} needs a materialized scalar operand.", MethodName);
        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
        for (int i = 0; i < Math.Min(count & 31, 16); i++)
        {
            Emit(Opcode.ASL, AddressMode.Accumulator);
            Emit(Opcode.ROL, AddressMode.ZeroPage, TEMP);
        }
        Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
        FinishNumericBinary(instruction);
        return true;
    }
}
