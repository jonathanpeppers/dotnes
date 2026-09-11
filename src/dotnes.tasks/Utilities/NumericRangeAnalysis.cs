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
    readonly Dictionary<int, Range?> locals = new();
    readonly HashSet<int> visiting = new();

    readonly record struct Range(long Min, long Max);

    public NumericRangeAnalysis(ILInstruction[] instructions, MethodNumericTypes types,
        IReadOnlyDictionary<string, MethodNumericTypes> methods, ReflectionCache reflection)
    {
        this.instructions = instructions;
        this.types = types;
        this.methods = methods;
        this.reflection = reflection;
        values = new(instructions, reflection);
    }

    public Dictionary<int, PrimitiveTypeCode> GetCompactIntLocals(string methodName)
    {
        var result = new Dictionary<int, PrimitiveTypeCode>();
        for (int i = 0; i < types.Locals.Length; i++)
        {
            if (types.Locals[i] != PrimitiveTypeCode.Int32)
                continue;
            if (!instructions.Any(instruction => instruction.GetStlocIndex() == i))
                continue;
            var range = LocalRange(i);
            if (range is { Min: >= 0, Max: <= byte.MaxValue })
                result.Add(i, PrimitiveTypeCode.Byte);
            else if (range is { Min: >= 0, Max: <= ushort.MaxValue })
                result.Add(i, PrimitiveTypeCode.UInt16);
            else if (range is { Min: >= short.MinValue, Max: <= short.MaxValue })
                result.Add(i, PrimitiveTypeCode.Int16);
            else
            {
                var store = instructions.First(instruction => instruction.GetStlocIndex() == i);
                throw new TranspileException(
                    $"Int32 local {i} at IL_{store.Offset:X4} requires a range that cannot be proven to fit " +
                    "the NES byte/word backend. Full 32-bit local arithmetic is not supported. " +
                    "Use byte, sbyte, short or ushort with explicit conversions only if their range and " +
                    "truncation semantics are intended, or bound the counter before updating it.",
                    methodName);
            }
        }
        return result;
    }

    Range? LocalRange(int index)
    {
        if (index >= types.Locals.Length)
            return null;
        if (types.Locals[index] != PrimitiveTypeCode.Int32)
            return TypeRange(types.Locals[index]);
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
                var assigned = values.Inputs[store].Length == 1 ? ValueRange(values.Inputs[store][0]) : null;
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
        switch (instruction.OpCode)
        {
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
        var left = ValueRange(values.Inputs[producer][0]);
        var right = ValueRange(values.Inputs[producer][1]);
        if (left == null || right == null)
            return null;
        Range? result = instruction.OpCode switch
        {
            ILOpCode.Add => new(left.Value.Min + right.Value.Min, left.Value.Max + right.Value.Max),
            ILOpCode.Sub => new(left.Value.Min - right.Value.Max, left.Value.Max - right.Value.Min),
            ILOpCode.And when right.Value.Min == right.Value.Max && right.Value.Min >= 0 => new(0, right.Value.Max),
            _ => null,
        };
        return result is { Min: >= int.MinValue, Max: <= int.MaxValue } ? result : null;
    }

    Range? CounterRange(int local)
    {
        var stores = Enumerable.Range(0, instructions.Length)
            .Where(i => instructions[i].GetStlocIndex() == local).ToArray();
        if (stores.Length != 2 || stores[0] == 0 || stores[1] < 3
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
