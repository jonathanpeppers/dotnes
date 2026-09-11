using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;

namespace dotnes;

partial class IL2NESWriter
{
    MethodNumericTypes? _numericTypes;
    IReadOnlyDictionary<string, MethodNumericTypes>? _numericMethods;
    IReadOnlyDictionary<string, PrimitiveTypeCode?>? _numericFields;
    ILValueAnalysis? _numericValues;
    Dictionary<int, PrimitiveTypeCode> _compactIntLocals = new();
    readonly Dictionary<int, int> _numericArgAdjust = new();

    internal void ConfigureNumericTypes(IReadOnlyDictionary<string, MethodNumericTypes> methods,
        IReadOnlyDictionary<string, PrimitiveTypeCode?>? fields = null)
    {
        _numericMethods = methods;
        _numericFields = fields;
        methods.TryGetValue(MethodName ?? "main", out _numericTypes);
        if (MethodName != null && _numericTypes != null)
        {
            for (int i = 0; i < _numericTypes.Parameters.Length; i++)
                if (_numericTypes.Parameters[i] is not (null or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte))
                    throw new TranspileException(
                        $"Parameter {i} has type {_numericTypes.Parameters[i]}, but user-method scalar arguments " +
                        "currently support byte and sbyte only. Keep word values in locals or use a supported " +
                        "native routine with a documented calling convention.", MethodName);
        }
        if (Instructions != null)
        {
            _numericValues = new ILValueAnalysis(Instructions, _reflectionCache);
            if (_numericTypes != null)
                _compactIntLocals = new NumericRangeAnalysis(Instructions, _numericTypes, methods, _reflectionCache, fields)
                    .GetCompactIntLocals(MethodName ?? "main");
        }
    }

    PrimitiveTypeCode? NumericType(int producer)
    {
        if (producer < 0 || Instructions == null || _numericValues == null)
            return null;
        var instruction = Instructions[producer];
        if (instruction.GetLdlocIndex() is int localIndex)
            return _compactIntLocals.TryGetValue(localIndex, out var compact) ? compact
                : _numericTypes != null && localIndex < _numericTypes.Locals.Length
                ? _numericTypes.Locals[localIndex] : null;
        if (NumericArgIndex(instruction) is int argIndex)
            return _numericTypes != null && argIndex < _numericTypes.Parameters.Length
                ? _numericTypes.Parameters[argIndex] : null;
        if (instruction.GetLdcValue() is int constant)
            return constant < sbyte.MinValue ? PrimitiveTypeCode.Int16
                : constant < 0 ? PrimitiveTypeCode.SByte
                : constant <= 255 ? PrimitiveTypeCode.Byte : PrimitiveTypeCode.UInt16;
        return instruction.OpCode switch
        {
            ILOpCode.Ldsfld when instruction.String is string field
                && _numericFields != null && _numericFields.TryGetValue(field, out var fieldType) => fieldType,
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
            ILOpCode.Div when _numericValues.Inputs[producer].Length == 2 =>
                SignedNumericType(NumericType(_numericValues.Inputs[producer][0]))
                    ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16,
            ILOpCode.Rem when _numericValues.Inputs[producer].Length == 2 =>
                NumericType(_numericValues.Inputs[producer][0]),
            ILOpCode.Mul or ILOpCode.Shl when _numericValues.Inputs[producer].Length == 2 =>
                _numericValues.Inputs[producer].Any(input => SignedNumericType(NumericType(input)))
                    ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16,
            ILOpCode.And when _numericValues.Inputs[producer].Length == 2
                && _numericValues.Inputs[producer].Any(input => NumericType(input) == PrimitiveTypeCode.Byte) =>
                    PrimitiveTypeCode.Byte,
            ILOpCode.And when _numericValues.Inputs[producer].Length == 2
                && _numericValues.Inputs[producer].Any(input => NumericType(input) == PrimitiveTypeCode.UInt16) =>
                    PrimitiveTypeCode.UInt16,
            ILOpCode.And or ILOpCode.Or or ILOpCode.Xor when _numericValues.Inputs[producer].Length == 2 =>
                _numericValues.Inputs[producer].Any(input => SignedNumericType(NumericType(input)))
                    ? PrimitiveTypeCode.Int16
                    : _numericValues.Inputs[producer].Any(input => WordNumericType(NumericType(input)))
                        ? PrimitiveTypeCode.UInt16 : PrimitiveTypeCode.Byte,
            ILOpCode.Add or ILOpCode.Sub when _numericValues.Inputs[producer].Length == 2
                && _numericValues.Inputs[producer].All(input => NumericType(input) is PrimitiveTypeCode.Byte
                    or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16) =>
                    instruction.OpCode == ILOpCode.Sub
                    || SignedNumericType(NumericType(_numericValues.Inputs[producer][0]))
                    || SignedNumericType(NumericType(_numericValues.Inputs[producer][1]))
                        ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16,
            ILOpCode.Shr or ILOpCode.Shr_un when _numericValues.Inputs[producer].Length == 2 =>
                NumericType(_numericValues.Inputs[producer][0]),
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
        if (instruction.OpCode == ILOpCode.Ldsfld && instruction.String is string field)
            return StaticFieldAddresses.ContainsKey(field)
                && NumericType(producer) is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte
                    or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16;
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
        if (lhs < 0 || rhs < 0)
            return false;
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
        else if (instruction.OpCode == ILOpCode.Ldsfld && instruction.String is string field)
        {
            ushort address = StaticFieldAddresses[field];
            Emit(Opcode.LDA, AddressMode.Absolute, address);
            if (WordStaticFields.Contains(field))
                Emit(Opcode.LDX, AddressMode.Absolute, (ushort)(address + 1));
            else
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

    void WriteNumericConversion(ILOpCode code)
    {
        int value = Stack.Count > 0 ? Stack.Pop() : 0;
        switch (code)
        {
            case ILOpCode.Conv_i1:
                Stack.Push(unchecked((sbyte)value));
                _ushortInAX = false;
                break;
            case ILOpCode.Conv_u2:
            case ILOpCode.Conv_i2:
                Stack.Push(code == ILOpCode.Conv_i2 ? unchecked((short)value) : unchecked((ushort)value));
                if (!_ushortInAX)
                {
                    int producer = _numericValues != null && _numericValues.Inputs[Index].Length == 1
                        ? _numericValues.Inputs[Index][0] : -1;
                    if (producer >= 0 && Instructions![producer].GetLdcValue() is int constant)
                        Emit(Opcode.LDX, AddressMode.Immediate, (byte)(constant >> 8));
                    else
                        EmitNumericExtension(SignedNumericType(NumericType(producer)));
                    _ushortInAX = true;
                }
                break;
        }
        _lastStaticFieldAddress = null;
    }

    internal void PrepareNumericReturn(ILInstruction instruction)
    {
        if (instruction.OpCode != ILOpCode.Ret || _numericTypes == null
            || !WordNumericType(_numericTypes.ReturnType) || _ushortInAX)
            return;
        int producer = _numericValues != null && _numericValues.Inputs[Index].Length == 1
            ? _numericValues.Inputs[Index][0] : -1;
        bool signed = SignedNumericType(NumericType(producer));
        if (producer >= 0 && Instructions![producer].GetLdcValue() is int constant)
            Emit(Opcode.LDX, AddressMode.Immediate, (byte)(constant >> 8));
        else
            EmitNumericExtension(signed);
        _ushortInAX = true;
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
        if (lhs < 0 || rhs < 0)
        {
            if (signed || word)
                throw new TranspileException(
                    $"Arithmetic at IL_{Instructions[Index].Offset:X4} has a merged operand whose numeric " +
                    "provenance cannot be represented. Store each conditional result in an explicitly typed " +
                    "byte, sbyte, short or ushort local before the arithmetic.", MethodName);
            return false;
        }
        bool wordResult = RequiresNumericWord(Index, new HashSet<int>());
        if (!signed && isAdd && leftType == PrimitiveTypeCode.Byte
            && Instructions[rhs].GetLdcValue() is > byte.MaxValue)
            return false;
        // Existing word-plus-constant emission is already correct and compact.
        if (!signed && (!wordResult && !word
            || WordNumericType(leftType) && Instructions[rhs].GetLdcValue().HasValue))
            return false;
        if (!TryNumericOperands(out int left, out int right))
        {
            if (wordResult && _runtimeValueInA && !_ushortInAX
                && Instructions[rhs].GetLdcValue().HasValue)
            {
                EmitNumericExtension(SignedNumericType(leftType));
                _ushortInAX = true;
            }
            return false;
        }

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
        _verifiedWordResults.Add(Index);
        if (leftType is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte
            && rightType is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte)
            _verifiedPromotedResults.Add(Index);
        return true;
    }

    void WriteLegacyNumericAddSub(bool isAdd)
    {
        bool verified = _ushortInAX && _numericValues != null && Instructions != null
            && _numericValues.Inputs[Index].Length == 2
            && _verifiedWordResults.Contains(_numericValues.Inputs[Index][0])
            && _numericValues.Inputs[Index][1] is >= 0 and int right
            && Instructions[right].GetLdcValue() is >= 0 and <= ushort.MaxValue;
        HandleAddSub(isAdd);
        if (verified && _ushortInAX)
            _verifiedWordResults.Add(Index);
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
        if (lhs < 0 || rhs < 0)
            return false;
        bool signedLeft = SignedNumericType(NumericType(lhs));
        bool signedRight = SignedNumericType(NumericType(rhs));
        if (!signedLeft && !signedRight
            && !(WordNumericType(NumericType(lhs)) && WordNumericType(NumericType(rhs))
                && Instructions![lhs].GetLdcValue() == null && Instructions[rhs].GetLdcValue() == null))
            return false;
        bool operandsReloaded = TryNumericOperands(out int left, out int right);
        if (!operandsReloaded)
        {
            if (!_ushortInAX || Instructions![rhs].GetLdcValue() == null)
                return false;
            left = lhs;
            right = rhs;
        }

        // CLR promotes both operands to int. A third sign byte keeps unsigned
        // ushort values above 32767 ordered correctly against negative shorts.
        if (operandsReloaded)
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

    internal bool TryNumericShift(ILInstruction instruction)
    {
        if (instruction.OpCode is not (ILOpCode.Shr or ILOpCode.Shr_un) || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (!SignedNumericType(NumericType(lhs)))
            return false;
        if (instruction.OpCode == ILOpCode.Shr_un)
            throw new TranspileException(
                $"Signed logical right shift at IL_{instruction.Offset:X4} requires CLR 32-bit promotion, " +
                "which this backend does not implement. Use an arithmetic right shift, or explicitly " +
                "convert to byte/ushort first only if that narrowing is intended.", MethodName);
        if (rhs < 0 || Instructions![rhs].GetLdcValue() is not int count)
            throw new TranspileException(
                $"Signed right shift at IL_{instruction.Offset:X4} requires a constant count. " +
                "Runtime signed shift counts are not supported by the NES numeric backend.", MethodName);
        if (TryNumericOperands(out int left, out _))
            EmitNumericOperand(left);
        else if (_runtimeValueInA)
        {
            if (!_ushortInAX)
                EmitNumericExtension(signed: true);
        }
        else
            throw new TranspileException(
                $"Signed right shift at IL_{instruction.Offset:X4} needs a materialized scalar operand. " +
                "Store the expression in a short or sbyte local before shifting.", MethodName);
        EmitSignedRightShift(Math.Min(count & 31, 16));
        FinishNumericBinary(instruction);
        return true;
    }

    void EmitSignedRightShift(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Emit(Opcode.STX, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.CPX, AddressMode.Immediate, 0x80);
            Emit(Opcode.ROR, AddressMode.ZeroPage, TEMP);
            Emit(Opcode.ROR, AddressMode.Accumulator);
            Emit(Opcode.LDX, AddressMode.ZeroPage, TEMP);
        }
    }

    bool TryNumericDivision(ILInstruction instruction)
    {
        if (instruction.OpCode != ILOpCode.Div || _numericValues == null
            || _numericValues.Inputs[Index].Length != 2)
            return false;
        int lhs = _numericValues.Inputs[Index][0], rhs = _numericValues.Inputs[Index][1];
        if (!SignedNumericType(NumericType(lhs)))
            return false;
        if (rhs < 0 || Instructions![rhs].GetLdcValue() is not int divisor || divisor <= 0
            || divisor > 32768 || (divisor & (divisor - 1)) != 0)
            throw new TranspileException(
                $"Signed division at IL_{instruction.Offset:X4} requires a positive power-of-two constant divisor.", MethodName);
        if (TryNumericOperands(out int left, out _))
            EmitNumericOperand(left);
        else if (lhs == Index - 2 && PureNumericOperand(lhs, out _))
            EmitNumericOperand(lhs);
        else if (_runtimeValueInA)
        {
            if (!_ushortInAX)
                EmitNumericExtension(signed: true);
        }
        else
            throw new TranspileException(
                $"Signed division at IL_{instruction.Offset:X4} needs a materialized scalar operand.", MethodName);
        string shift = $"{MethodName ?? "main"}_divide_{instruction.Offset:X4}";
        Emit(Opcode.CPX, AddressMode.Immediate, 0x80);
        EmitWithLabel(Opcode.BCC, AddressMode.Relative, shift);
        // Bias negative values so arithmetic shifting truncates toward zero.
        Emit(Opcode.CLC, AddressMode.Implied);
        Emit(Opcode.ADC, AddressMode.Immediate, (byte)(divisor - 1));
        Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);
        Emit(Opcode.TXA, AddressMode.Implied);
        Emit(Opcode.ADC, AddressMode.Immediate, (byte)((divisor - 1) >> 8));
        Emit(Opcode.TAX, AddressMode.Implied);
        Emit(Opcode.LDA, AddressMode.ZeroPage, TEMP);
        CurrentBlock!.SetNextLabel(shift);
        int count = 0;
        for (int value = divisor; value > 1; value >>= 1) count++;
        EmitSignedRightShift(count);
        FinishNumericBinary(instruction);
        return true;
    }

}
