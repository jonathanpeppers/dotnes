using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    bool TryNumericUnary(ILInstruction instruction)
    {
        if (_numericValues == null || Instructions == null || _numericValues.Inputs[Index].Length != 1
            || !RequiresNumericWord(Index, new HashSet<int>())
                && NumericValueUsage.IsExplicitlyNarrowed(Instructions, _numericValues, Index, byteOnly: true))
            return false;
        int operand = _numericValues.Inputs[Index][0];
        if (operand < 0 || NumericType(operand) is not (PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte
            or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16))
            throw new TranspileException(
                $"Unary {instruction.OpCode} at IL_{instruction.Offset:X4} requires a supported scalar operand. " +
                "Store the operand in a byte, sbyte, short or ushort local before the operation.", MethodName);
        if (operand == Index - 1 && PureNumericOperand(operand, out _))
            EmitNumericOperand(operand);
        else if (_runtimeValueInA)
        {
            if (!_ushortInAX)
                EmitNumericExtension(SignedNumericType(NumericType(operand)));
        }
        else
            throw new TranspileException(
                $"Unary {instruction.OpCode} at IL_{instruction.Offset:X4} needs a materialized scalar operand. " +
                "Store the operand in an explicitly typed local before the operation.", MethodName);

        Emit(Opcode.EOR, AddressMode.Immediate, 0xFF);
        if (instruction.OpCode == ILOpCode.Neg)
        {
            Emit(Opcode.CLC, AddressMode.Implied);
            Emit(Opcode.ADC, AddressMode.Immediate, 1);
        }
        Emit(Opcode.TAY, AddressMode.Implied);
        Emit(Opcode.TXA, AddressMode.Implied);
        Emit(Opcode.EOR, AddressMode.Immediate, 0xFF);
        if (instruction.OpCode == ILOpCode.Neg)
            Emit(Opcode.ADC, AddressMode.Immediate, 0);
        Emit(Opcode.TAX, AddressMode.Implied);
        Emit(Opcode.TYA, AddressMode.Implied);
        if (Stack.Count > 0) Stack.Pop();
        Stack.Push(0);
        _accState = AccumulatorState.RuntimeUshort;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        _verifiedWordResults.Add(Index);
        return true;
    }
}
