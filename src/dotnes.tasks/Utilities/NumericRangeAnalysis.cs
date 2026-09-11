using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Proves compact storage for Int32 locals without changing their CLR range.
/// Cyclic or unknown data flow is not a proof that a value fits in a byte/word.
/// </summary>
sealed class NumericRangeAnalysis
{
    readonly ILInstruction[] instructions;
    readonly ILValueAnalysis values;
    readonly MethodNumericTypes types;
    readonly IReadOnlyDictionary<string, MethodNumericTypes> methods;
    readonly ReflectionCache reflection;
    readonly IReadOnlyDictionary<string, PrimitiveTypeCode?>? fields;
    readonly Dictionary<int, Range?> locals = new();
    readonly HashSet<int> visiting = new();

    readonly record struct Range(long Min, long Max);

    public NumericRangeAnalysis(ILInstruction[] instructions, MethodNumericTypes types,
        IReadOnlyDictionary<string, MethodNumericTypes> methods, ReflectionCache reflection,
        IReadOnlyDictionary<string, PrimitiveTypeCode?>? fields = null)
    {
        this.instructions = instructions;
        this.types = types;
        this.methods = methods;
        this.reflection = reflection;
        this.fields = fields;
        values = new(instructions, reflection);
    }

    public Dictionary<int, PrimitiveTypeCode> GetCompactIntLocals(string methodName)
    {
        var result = new Dictionary<int, PrimitiveTypeCode>();
        for (int i = 0; i < types.Locals.Length; i++)
        {
            if (types.Locals[i] is null or PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Byte
                or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16)
                continue;
            int firstUse = Array.FindIndex(instructions, instruction =>
                instruction.GetStlocIndex() == i || instruction.GetLdlocIndex() == i
                || instruction.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s && instruction.Integer == i);
            if (firstUse < 0)
                continue;
            if (types.Locals[i] != PrimitiveTypeCode.Int32)
                throw new TranspileException(
                    $"Local {i} at IL_{instructions[firstUse].Offset:X4} has unsupported primitive type {types.Locals[i]}. " +
                    "Use an explicitly supported storage type (byte, sbyte, short or ushort) with conversions " +
                    "only when its range and truncation semantics are intended.", methodName);
            var range = LocalRange(i);
            if (range is { Min: >= 0, Max: <= byte.MaxValue })
                result.Add(i, PrimitiveTypeCode.Byte);
            else if (range is { Min: >= 0, Max: <= ushort.MaxValue })
                result.Add(i, PrimitiveTypeCode.UInt16);
            else if (range is { Min: >= short.MinValue, Max: <= short.MaxValue })
                result.Add(i, PrimitiveTypeCode.Int16);
            else
            {
                throw new TranspileException(
                    $"Int32 local {i} at IL_{instructions[firstUse].Offset:X4} requires a range that cannot be proven to fit " +
                    "the NES byte/word backend. Full 32-bit local arithmetic is not supported. " +
                    "Use byte, sbyte, short or ushort with explicit conversions only if their range and " +
                    "truncation semantics are intended, or bound the counter before updating it.",
                    methodName);
            }
        }
        return result;
    }

    public void ValidatePromotedArithmetic(string methodName)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            for (int operand = 0; operand < values.Inputs[i].Length; operand++)
            {
                if (values.Inputs[i][operand] >= 0 || InputRange(i, operand) is not { } merged
                    || FitsWord(merged)
                    || instructions[i].OpCode is ILOpCode.Conv_u1 or ILOpCode.Conv_i1 or ILOpCode.Conv_u2 or ILOpCode.Conv_i2)
                    continue;
                if (instructions[i].OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul
                    or ILOpCode.Shl or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                    && NumericValueUsage.IsExplicitlyNarrowed(instructions, values, i))
                    continue;
                throw new TranspileException(
                    $"Conditional operand at IL_{instructions[i].Offset:X4} needs a promoted result wider " +
                    "than the supported word representation. Narrow the conditional value explicitly before " +
                    "using it only if word wrapping is intended.", methodName);
            }
            if (instructions[i].OpCode is ILOpCode.Div or ILOpCode.Rem
                && Enumerable.Range(0, values.Inputs[i].Length).Any(input => InputRange(i, input) is { Min: < 0 })
                && !(instructions[i].OpCode == ILOpCode.Div && values.Inputs[i].Length == 2
                    && values.Inputs[i][1] >= 0 && instructions[values.Inputs[i][1]].GetLdcValue() is > 0 and int divisor
                    && divisor <= 32768 && (divisor & (divisor - 1)) == 0))
                throw new TranspileException(
                    $"Signed {instructions[i].OpCode} at IL_{instructions[i].Offset:X4} is not supported " +
                    "by the NES numeric backend. Use nonnegative byte/ushort operands only when that " +
                    "range matches the intended computation.", methodName);
            if (instructions[i].OpCode is not (ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Shl
                or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor))
                continue;
            if (instructions[i].OpCode is ILOpCode.Add or ILOpCode.Sub
                && values.Inputs[i].Any(IsManagedAddress))
                continue;
            if (values.Consumers[i].Any(consumer => instructions[consumer].OpCode is
                ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i1_un
                or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i2_un))
                continue;
            var range = ValueRange(i);
            if (instructions[i].OpCode == ILOpCode.Shl && values.Inputs[i].Length == 2
                && values.Inputs[i][1] >= 0 && instructions[values.Inputs[i][1]].GetLdcValue() is int shift
                && (shift & 31) > 15 && InputRange(i, 0) is { } shifted
                && (shifted.Min != 0 || shifted.Max != 0))
                range = new(int.MinValue, int.MaxValue);
            if (range is { Min: >= 0, Max: <= ushort.MaxValue }
                or { Min: >= short.MinValue, Max: <= short.MaxValue })
                continue;
            if (!NumericValueUsage.IsExplicitlyNarrowed(instructions, values, i))
            {
                if (range == null)
                    throw new TranspileException(
                        $"Arithmetic at IL_{instructions[i].Offset:X4} has an unproven promoted range. " +
                        "Store bounded operands in explicitly typed byte, sbyte, short or ushort locals, " +
                        "or narrow before observing the result only if that truncation is intended.", methodName);
                throw new TranspileException(
                    $"Arithmetic at IL_{instructions[i].Offset:X4} needs a promoted result wider than the " +
                    "supported word representation. An explicit conversion after a comparison or shift cannot " +
                    "restore a lost carry/sign bit. Narrow before that operation only if word wrapping is intended.",
                    methodName);
            }
        }
    }

    Range? LocalRange(int index)
    {
        if (index >= types.Locals.Length)
            return null;
        if (types.Locals[index] != PrimitiveTypeCode.Int32)
            return TypeRange(types.Locals[index]);
        // Direct stores and loop bounds cannot constrain writes through an exposed address.
        if (instructions.Any(instruction => instruction.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s
            && instruction.Integer == index))
            return null;
        if (locals.TryGetValue(index, out var known))
            return known;
        if (!visiting.Add(index))
            return null;
        Range? result = CounterRange(index);
        if (result == null)
        {
            foreach (int store in Enumerable.Range(0, instructions.Length)
                .Where(i => instructions[i].GetStlocIndex() == index))
            {
                var assigned = values.Inputs[store].Length == 1 ? InputRange(store, 0) : null;
                if (assigned == null)
                {
                    result = null;
                    break;
                }
                result = result == null ? assigned
                    : new Range(Math.Min(result.Value.Min, assigned.Value.Min), Math.Max(result.Value.Max, assigned.Value.Max));
            }
        }
        visiting.Remove(index);
        locals[index] = result;
        return result;
    }

    Range? ValueRange(int producer)
    {
        if (producer < 0)
            return null;
        var instruction = instructions[producer];
        if (instruction.GetLdcValue() is int constant)
            return new(constant, constant);
        if (instruction.GetLdlocIndex() is int local)
            return LocalRange(local);
        if (IL2NESWriter.NumericArgIndex(instruction) is int argument && argument < types.Parameters.Length)
            return TypeRange(types.Parameters[argument]);
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldsfld when instruction.String is string field
                && fields != null && fields.TryGetValue(field, out var fieldType):
                return TypeRange(fieldType);
            case ILOpCode.Conv_u1: case ILOpCode.Ldelem_u1: case ILOpCode.Ldind_u1:
                return new(0, byte.MaxValue);
            case ILOpCode.Conv_i1: case ILOpCode.Ldelem_i1: case ILOpCode.Ldind_i1:
                return new(sbyte.MinValue, sbyte.MaxValue);
            case ILOpCode.Conv_u2: case ILOpCode.Ldelem_u2: case ILOpCode.Ldind_u2:
                return new(0, ushort.MaxValue);
            case ILOpCode.Conv_i2: case ILOpCode.Ldelem_i2: case ILOpCode.Ldind_i2:
                return new(short.MinValue, short.MaxValue);
            case ILOpCode.Ceq: case ILOpCode.Clt: case ILOpCode.Clt_un: case ILOpCode.Cgt: case ILOpCode.Cgt_un:
                return new(0, 1);
            case ILOpCode.Call when instruction.String != null:
                if (methods.TryGetValue(instruction.String, out var method))
                    return TypeRange(method.ReturnType);
                var returnType = reflection.GetMethod(instruction.String).ReturnType;
                if (returnType.IsEnum)
                    returnType = Enum.GetUnderlyingType(returnType);
                if (returnType == typeof(byte)) return new(0, byte.MaxValue);
                if (returnType == typeof(sbyte)) return new(sbyte.MinValue, sbyte.MaxValue);
                if (returnType == typeof(ushort)) return new(0, ushort.MaxValue);
                if (returnType == typeof(short)) return new(short.MinValue, short.MaxValue);
                return null;
        }
        if (values.Inputs[producer].Length != 2)
            return null;
        int rhs = values.Inputs[producer][1];
        if (instruction.OpCode == ILOpCode.And && rhs >= 0
            && instructions[rhs].GetLdcValue() is >= 0 and int mask)
            return new(0, mask);
        var left = InputRange(producer, 0);
        var right = InputRange(producer, 1);
        if (left == null || right == null)
            return null;
        Range? result = instruction.OpCode switch
        {
            ILOpCode.Add => new(left.Value.Min + right.Value.Min, left.Value.Max + right.Value.Max),
            ILOpCode.Sub => new(left.Value.Min - right.Value.Max, left.Value.Max - right.Value.Min),
            ILOpCode.Mul => new(
                new[] { left.Value.Min * right.Value.Min, left.Value.Min * right.Value.Max,
                    left.Value.Max * right.Value.Min, left.Value.Max * right.Value.Max }.Min(),
                new[] { left.Value.Min * right.Value.Min, left.Value.Min * right.Value.Max,
                    left.Value.Max * right.Value.Min, left.Value.Max * right.Value.Max }.Max()),
            ILOpCode.Shl when right.Value.Min == right.Value.Max && (right.Value.Min & 31) <= 15 =>
                new(left.Value.Min << (int)(right.Value.Min & 31), left.Value.Max << (int)(right.Value.Min & 31)),
            ILOpCode.Shr when right.Value.Min == right.Value.Max =>
                new(left.Value.Min >> (int)(right.Value.Min & 31), left.Value.Max >> (int)(right.Value.Min & 31)),
            ILOpCode.Shr_un when left.Value.Min >= 0 && right.Value.Min == right.Value.Max =>
                new(left.Value.Min >> (int)(right.Value.Min & 31), left.Value.Max >> (int)(right.Value.Min & 31)),
            ILOpCode.Shr => new(Math.Min(left.Value.Min, 0), Math.Max(left.Value.Max, 0)),
            ILOpCode.Shr_un when left.Value.Min >= 0 => new(0, left.Value.Max),
            ILOpCode.Div when right.Value.Min > 0 => new(
                new[] { left.Value.Min / right.Value.Min, left.Value.Min / right.Value.Max,
                    left.Value.Max / right.Value.Min, left.Value.Max / right.Value.Max }.Min(),
                new[] { left.Value.Min / right.Value.Min, left.Value.Min / right.Value.Max,
                    left.Value.Max / right.Value.Min, left.Value.Max / right.Value.Max }.Max()),
            ILOpCode.Rem when right.Value.Min > 0 => new(
                left.Value.Min >= 0 ? 0 : -right.Value.Max + 1,
                left.Value.Max <= 0 ? 0 : right.Value.Max - 1),
            ILOpCode.And when right.Value.Min == right.Value.Max && right.Value.Min >= 0 => new(0, right.Value.Max),
            ILOpCode.And when left.Value.Min >= 0 => new(0, left.Value.Max),
            ILOpCode.And when right.Value.Min >= 0 => new(0, right.Value.Max),
            ILOpCode.And or ILOpCode.Or or ILOpCode.Xor => BitwiseRange(left.Value, right.Value),
            _ => null,
        };
        return result == null || result is { Min: >= int.MinValue, Max: <= int.MaxValue }
            ? result : new(int.MinValue, int.MaxValue);
    }

    static Range BitwiseRange(Range left, Range right)
    {
        long min = Math.Min(left.Min, right.Min), max = Math.Max(left.Max, right.Max);
        if (min >= 0)
            return new(0, max <= byte.MaxValue ? byte.MaxValue : max <= ushort.MaxValue ? ushort.MaxValue : int.MaxValue);
        if (min >= sbyte.MinValue && max <= sbyte.MaxValue)
            return new(sbyte.MinValue, sbyte.MaxValue);
        return min >= short.MinValue && max <= short.MaxValue
            ? new(short.MinValue, short.MaxValue) : new(int.MinValue, int.MaxValue);
    }

    bool IsManagedAddress(int producer)
    {
        if (producer < 0)
            return false;
        var code = instructions[producer].OpCode;
        return code is ILOpCode.Ldloca or ILOpCode.Ldloca_s or ILOpCode.Ldflda or ILOpCode.Ldsflda or ILOpCode.Ldelema
            || code is ILOpCode.Conv_i or ILOpCode.Conv_u or ILOpCode.Add or ILOpCode.Sub
                && values.Inputs[producer].Any(IsManagedAddress);
    }

    static bool FitsWord(Range range) => range is { Min: >= 0, Max: <= ushort.MaxValue }
        or { Min: >= short.MinValue, Max: <= short.MaxValue };

    Range? InputRange(int consumer, int operand)
    {
        int producer = values.Inputs[consumer][operand];
        return producer >= 0 ? ValueRange(producer)
            : IncomingRange(consumer, values.Inputs[consumer].Length - operand - 1, new());
    }

    Range? IncomingRange(int consumer, int depth, HashSet<(int, int)> path)
    {
        if (!path.Add((consumer, depth)))
            return null;
        Range? result = null;
        foreach (int predecessor in values.Predecessors[consumer])
        {
            int slot = values.Outputs[predecessor].Length - depth - 1;
            if (slot < 0)
                return null;
            int producer = values.Outputs[predecessor][slot];
            Range? incoming;
            if (producer >= 0)
                incoming = ValueRange(producer);
            else
            {
                // Trace only a carried stack slot, not a newly computed value.
                int beforeDepth = instructions[predecessor].OpCode == ILOpCode.Dup
                    ? Math.Max(0, depth - 1)
                    : depth + values.Inputs[predecessor].Length - (values.ProducesValue[predecessor] ? 1 : 0);
                incoming = IncomingRange(predecessor, beforeDepth, path);
            }
            if (incoming == null)
                return null;
            result = result == null ? incoming
                : new Range(Math.Min(result.Value.Min, incoming.Value.Min), Math.Max(result.Value.Max, incoming.Value.Max));
        }
        path.Remove((consumer, depth));
        return result;
    }

    Range? CounterRange(int local)
    {
        var stores = Enumerable.Range(0, instructions.Length)
            .Where(i => instructions[i].GetStlocIndex() == local).ToArray();
        if (stores.Length != 2 || stores[0] == 0 || stores[1] < 3
            || values.Inputs[stores[0]].Length != 1 || values.Inputs[stores[0]][0] != stores[0] - 1
            || instructions[stores[0] - 1].GetLdcValue() is not int initial || initial < 0
            || instructions[stores[1] - 3].GetLdlocIndex() != local
            || instructions[stores[1] - 2].GetLdcValue() != 1
            || instructions[stores[1] - 1].OpCode != ILOpCode.Add)
            return null;
        int check = stores[1] + 1;
        if (check + 2 >= instructions.Length || instructions[check].GetLdlocIndex() != local
            || instructions[check + 1].GetLdcValue() is not int limit || limit < initial || limit > ushort.MaxValue
            || instructions[check + 2].OpCode is not (ILOpCode.Blt or ILOpCode.Blt_s))
            return null;
        int? body = ILValueAnalysis.GetBranchTarget(instructions[check + 2]);
        int entry = stores[0] + 1;
        if (body == null || body <= instructions[entry].Offset || body > instructions[stores[1] - 3].Offset
            || instructions[entry].OpCode is not (ILOpCode.Br or ILOpCode.Br_s)
            || ILValueAnalysis.GetBranchTarget(instructions[entry]) != instructions[check].Offset)
            return null;
        // No alternate entry or control transfer may bypass the loop guard.
        for (int i = entry + 1; i < check + 2; i++)
            if (ILValueAnalysis.IsBranch(instructions[i].OpCode))
                return null;
        for (int i = 0; i < instructions.Length; i++)
        {
            int? target = ILValueAnalysis.GetBranchTarget(instructions[i]);
            if (i != check + 2 && target >= body && target < instructions[check].Offset)
                return null;
        }
        return new(initial, limit);
    }

    static Range? TypeRange(PrimitiveTypeCode? type) => type switch
    {
        PrimitiveTypeCode.Boolean => new(0, 1),
        PrimitiveTypeCode.Byte => new(0, byte.MaxValue),
        PrimitiveTypeCode.SByte => new(sbyte.MinValue, sbyte.MaxValue),
        PrimitiveTypeCode.UInt16 => new(0, ushort.MaxValue),
        PrimitiveTypeCode.Int16 => new(short.MinValue, short.MaxValue),
        _ => null,
    };
}
