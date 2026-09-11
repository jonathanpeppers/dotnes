using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    MethodNumericTypes? _numericTypes;
    IReadOnlyDictionary<string, MethodNumericTypes>? _numericMethods;
    ILValueAnalysis? _numericValues;
    readonly Dictionary<int, int> _numericArgAdjust = new();

    internal void ConfigureNumericTypes(IReadOnlyDictionary<string, MethodNumericTypes> methods)
    {
        _numericMethods = methods;
        methods.TryGetValue(MethodName ?? "main", out _numericTypes);
        if (Instructions != null)
            _numericValues = new ILValueAnalysis(Instructions, _reflectionCache);
    }

    PrimitiveTypeCode? NumericType(int producer)
    {
        if (producer < 0 || Instructions == null || _numericValues == null)
            return null;
        var instruction = Instructions[producer];
        if (instruction.GetLdlocIndex() is int localIndex)
            return _numericTypes != null && localIndex < _numericTypes.Locals.Length
                ? _numericTypes.Locals[localIndex] : null;
        if (NumericArgIndex(instruction) is int argIndex)
            return _numericTypes != null && argIndex < _numericTypes.Parameters.Length
                ? _numericTypes.Parameters[argIndex] : null;
        if (instruction.GetLdcValue() is int constant)
            return constant < 0 ? PrimitiveTypeCode.Int16
                : constant <= 255 ? PrimitiveTypeCode.Byte : PrimitiveTypeCode.UInt16;
        return instruction.OpCode switch
        {
            ILOpCode.Conv_i1 => PrimitiveTypeCode.SByte,
            ILOpCode.Conv_u1 => PrimitiveTypeCode.Byte,
            ILOpCode.Conv_i2 => PrimitiveTypeCode.Int16,
            ILOpCode.Conv_u2 => PrimitiveTypeCode.UInt16,
            ILOpCode.Call when instruction.String != null
                && _numericMethods != null && _numericMethods.TryGetValue(instruction.String, out var method) => method.ReturnType,
            ILOpCode.Call when instruction.String != null && _reflectionCache.TryReturns16Bit(instruction.String) => PrimitiveTypeCode.UInt16,
            ILOpCode.Call when instruction.String != null && _reflectionCache.HasReturnValue(instruction.String) => PrimitiveTypeCode.Byte,
            ILOpCode.Ldelem_u1 or ILOpCode.Ldind_u1 => PrimitiveTypeCode.Byte,
            ILOpCode.Ldelem_i1 or ILOpCode.Ldind_i1 => PrimitiveTypeCode.SByte,
            ILOpCode.Ldelem_u2 or ILOpCode.Ldind_u2 => PrimitiveTypeCode.UInt16,
            ILOpCode.Ldelem_i2 or ILOpCode.Ldind_i2 => PrimitiveTypeCode.Int16,
            _ => null,
        };
    }

    internal static int? NumericArgIndex(ILInstruction instruction) => instruction.OpCode switch
    {
        ILOpCode.Ldarg_0 => 0,
        ILOpCode.Ldarg_1 => 1,
        ILOpCode.Ldarg_2 => 2,
        ILOpCode.Ldarg_3 => 3,
        ILOpCode.Ldarg_s or ILOpCode.Ldarg => instruction.Integer,
        _ => null,
    };

    static bool SignedNumericType(PrimitiveTypeCode? type) =>
        type is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16;

    static bool WordNumericType(PrimitiveTypeCode? type) =>
        type is PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int16;

    bool PureNumericOperand(int producer, out int first)
    {
        first = producer;
        if (producer < 0 || Instructions == null || _numericValues == null)
            return false;
        var instruction = Instructions[producer];
        if (instruction.GetLdcValue() is int constant)
            return constant >= short.MinValue && constant <= ushort.MaxValue;
        if (instruction.GetLdlocIndex() is int localIndex)
            return Locals.TryGetValue(localIndex, out var value)
                && value.Address.HasValue && value.LabelName == null && value.ArraySize == 0;
        if (NumericArgIndex(instruction) is int argIndex)
            return argIndex < ParamIsArray.Length && !ParamIsArray[argIndex]
                && NumericType(producer) is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte;
        if (instruction.OpCode is ILOpCode.Conv_i1 or ILOpCode.Conv_u1 or ILOpCode.Conv_i2 or ILOpCode.Conv_u2
            && _numericValues.Inputs[producer].Length == 1 && _numericValues.Inputs[producer][0] == producer - 1)
            return PureNumericOperand(_numericValues.Inputs[producer][0], out first);
        return false;
    }

    bool TryNumericOperands(out int left, out int right)
    {
        left = right = -1;
        if (_numericValues == null || Instructions == null || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (!PureNumericOperand(lhs, out int first)
            || !PureNumericOperand(rhs, out int second)
            || lhs + 1 != second || rhs + 1 != Index)
            return false;
        for (int i = 0; i < first; i++)
            if (_numericValues.Consumers[i].Any(consumer => consumer >= Index))
                return false;
        for (int i = first; i < Index; i++)
            if (_numericValues.Escapes[i] || _numericValues.Consumers[i].Count > 1)
                return false;
        if (!_blockCountAtILOffset.TryGetValue(Instructions[first].Offset, out int blockStart))
            return false;
        RemoveLastInstructions(GetBufferedBlockCount() - blockStart);
        _argStackAdjust = _numericArgAdjust[Instructions[first].Offset];
        CurrentBlock!.SetNextLabel(InstructionLabel(Instructions[first].Offset));
        _accState = AccumulatorState.Empty;
        _savedState = SavedValueState.None;
        _lastLoadedLocalIndex = null;
        _lastStaticFieldAddress = null;
        left = lhs;
        right = rhs;
        return true;
    }

    void EmitNumericOperand(int producer)
    {
        var instruction = Instructions![producer];
        if (instruction.GetLdcValue() is int constant)
        {
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)constant);
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(constant >> 8));
        }
        else if (instruction.GetLdlocIndex() is int localIndex)
        {
            var value = Locals[localIndex];
            Emit(Opcode.LDA, AddressMode.Absolute, (ushort)value.Address!.Value);
            if (value.IsWord)
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(value.Address.Value + 1));
            else
                EmitNumericExtension(NumericType(producer) == PrimitiveTypeCode.SByte);
        }
        else if (NumericArgIndex(instruction) is int argIndex)
        {
            int offset = _argStackAdjust;
            for (int j = argIndex + 1; j < MethodParamCount; j++)
                offset += ParamIsArray[j] ? 2 : 1;
            Emit(Opcode.LDY, AddressMode.Immediate, checked((byte)offset));
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, sp);
            EmitNumericExtension(NumericType(producer) == PrimitiveTypeCode.SByte);
        }
        else
        {
            EmitNumericOperand(_numericValues!.Inputs[producer][0]);
            if (instruction.OpCode is ILOpCode.Conv_i1 or ILOpCode.Conv_u1)
                EmitNumericExtension(instruction.OpCode == ILOpCode.Conv_i1);
        }
    }

    void EmitNumericExtension(bool signed)
    {
        if (signed)
            Emit(Opcode.CMP, AddressMode.Immediate, 0x80);
        Emit(Opcode.LDX, AddressMode.Immediate, 0);
        if (signed)
        {
            Emit(Opcode.BCC, AddressMode.Relative, 1);
            Emit(Opcode.DEX, AddressMode.Implied);
        }
    }

    bool TryNumericAddSub(bool isAdd)
    {
        if (_numericValues == null || Instructions == null || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        var leftType = NumericType(lhs);
        var rightType = NumericType(rhs);
        bool signed = SignedNumericType(leftType) || SignedNumericType(rightType);
        bool word = WordNumericType(leftType) || WordNumericType(rightType);
        bool wordResult = Index + 1 < Instructions.Length
            && Instructions[Index + 1].OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2;
        if (!signed && isAdd && leftType == PrimitiveTypeCode.Byte
            && Instructions[rhs].GetLdcValue() is > byte.MaxValue)
            return false;
        // Existing word-plus-constant emission is already correct and compact.
        if (!signed && (!wordResult && !word
            || WordNumericType(leftType) && Instructions[rhs].GetLdcValue().HasValue))
            return false;
        if (!TryNumericOperands(out int left, out int right))
            return false;

        EmitNumericOperand(left);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP2);
        EmitNumericOperand(right);
        if (isAdd)
        {
            Emit(Opcode.CLC, AddressMode.Implied);
            Emit(Opcode.ADC, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.TXA, AddressMode.Implied);
            Emit(Opcode.ADC, AddressMode.ZeroPage, TEMP2);
        }
        else
        {
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP3);
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.SEC, AddressMode.Implied);
            Emit(Opcode.SBC, AddressMode.ZeroPage, TEMP3);
            Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP3);
            Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP2);
            Emit(Opcode.SBC, AddressMode.ZeroPage, TEMP3);
        }
        Emit(Opcode.TAX, AddressMode.Implied);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
        if (Stack.Count > 0) Stack.Pop();
        if (Stack.Count > 0) Stack.Pop();
        Stack.Push(0);
        _accState = AccumulatorState.RuntimeUshort;
        return true;
    }

    internal bool TryNumericComparison(ILInstruction instruction)
    {
        var code = instruction.OpCode;
        bool less = code is ILOpCode.Clt or ILOpCode.Clt_un
            or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s
            or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s;
        bool greater = code is ILOpCode.Cgt or ILOpCode.Cgt_un
            or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
            or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s;
        bool equal = code is ILOpCode.Ceq or ILOpCode.Beq or ILOpCode.Beq_s
            or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
            or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s;
        bool unequal = code is ILOpCode.Bne_un or ILOpCode.Bne_un_s;
        bool unsigned = code is ILOpCode.Clt_un or ILOpCode.Cgt_un
            or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
            or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s;
        if (!(less || greater || equal || unequal) || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        bool signedLeft = SignedNumericType(NumericType(lhs));
        bool signedRight = SignedNumericType(NumericType(rhs));
        if (!signedLeft && !signedRight
            && !(WordNumericType(NumericType(lhs)) && WordNumericType(NumericType(rhs))
                && Instructions![lhs].GetLdcValue() == null && Instructions[rhs].GetLdcValue() == null))
            return false;
        if (!TryNumericOperands(out int left, out int right))
            return false;

        // CLR promotes both operands to int. A third sign byte keeps unsigned
        // ushort values above 32767 ordered correctly against negative shorts.
        EmitNumericOperand(left);
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.STX, AddressMode.ZeroPage, TEMP2);
        EmitNumericSign(signedLeft, unsigned);
        Emit(Opcode.STY, AddressMode.ZeroPage, TEMP3);
        EmitNumericOperand(right);
        EmitNumericSign(signedRight, unsigned);

        string prefix = $"{MethodName ?? "main"}_numeric_{instruction.Offset:X4}";
        string yes = prefix + "_true", no = prefix + "_false", done = prefix + "_done";
        string onLess = less || unequal ? yes : no;
        string onGreater = greater || unequal ? yes : no;
        string onEqual = equal ? yes : no;
        Emit(Opcode.CPY, AddressMode.ZeroPage, TEMP3);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, onGreater);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, onLess);
        Emit(Opcode.CPX, AddressMode.ZeroPage, TEMP2);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, onGreater);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, onLess);
        Emit(Opcode.CMP, AddressMode.ZeroPage, TEMP);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, onGreater);
        EmitWithLabel(Opcode.BNE, AddressMode.Relative, onLess);
        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, onEqual);
        CurrentBlock!.SetNextLabel(yes);
        Emit(Opcode.LDA, AddressMode.Immediate, 1);
        EmitWithLabel(Opcode.JMP, AddressMode.Absolute, done);
        CurrentBlock.SetNextLabel(no);
        Emit(Opcode.LDA, AddressMode.Immediate, 0);
        CurrentBlock.SetNextLabel(done);
        Emit(Opcode.CMP, AddressMode.Immediate, 0);
        if (Stack.Count > 0) Stack.Pop();
        if (Stack.Count > 0) Stack.Pop();
        if (ILValueAnalysis.IsBranch(code))
        {
            EmitWithLabel(Opcode.BNE, AddressMode.Relative,
                InstructionLabel(ILValueAnalysis.GetBranchTarget(instruction)
                    ?? throw new InvalidOperationException("Numeric branch has no target.")));
            _accState = AccumulatorState.Empty;
        }
        else
        {
            Stack.Push(0);
            _accState = AccumulatorState.Runtime;
        }
        previous = code;
        return true;
    }

    void EmitNumericSign(bool signed, bool unsigned)
    {
        Emit(Opcode.LDY, AddressMode.Immediate, (byte)(unsigned ? 0 : 0x80));
        if (signed)
        {
            Emit(Opcode.CPX, AddressMode.Immediate, 0x80);
            Emit(Opcode.BCC, AddressMode.Relative, 1);
            Emit(Opcode.DEY, AddressMode.Implied);
        }
    }
}
