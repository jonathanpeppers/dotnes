using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    ILInstruction[] PreserveExpressionValues(ILInstruction[] instructions, ReflectionCache reflection, string method,
        IReadOnlyDictionary<int, PrimitiveTypeCode>? compactInts = null)
    {
        var analysis = new ILValueAnalysis(instructions, reflection);
        NumericTypes.TryGetValue(method, out var signature);
        var types = GetExpressionValueTypes(instructions, analysis, reflection, method, compactInts);
        var scalar = types.Select((type, i) => type is not null && type != PrimitiveTypeCode.Void && !analysis.Escapes[i]).ToArray();
        var words = new HashSet<int>(Enumerable.Range(0, types.Length).Where(i =>
            types[i] is PrimitiveTypeCode.UInt16 or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32));

        var arrayOperands = new HashSet<int>();
        void ProtectArrayOperand(int producer)
        {
            if (producer < 0 || !arrayOperands.Add(producer))
                return;
            foreach (int input in analysis.Inputs[producer])
                ProtectArrayOperand(input);
        }
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode is ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelema
                or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2)
                foreach (int producer in analysis.Inputs[i])
                    ProtectArrayOperand(producer);
        }

        var spills = new HashSet<int>();
        bool HasPreservedPadResult(int producer)
        {
            if (instructions[producer].OpCode != ILOpCode.Call
                || instructions[producer].String is not ("pad_poll" or "pad_trigger"))
                return false;
            var consumers = analysis.Consumers[producer];
            if (consumers.Any(c => instructions[c].OpCode != ILOpCode.Dup
                && ((instructions[c].OpCode != ILOpCode.And
                        && !(instructions[c].OpCode == ILOpCode.Call && instructions[c].String == "pad_pressed"))
                    || analysis.Inputs[c].Length != 2
                    || analysis.Inputs[c][0] != producer || analysis.Inputs[c][1] < 0
                    || instructions[analysis.Inputs[c][1]].GetLdcValue() == null)))
                return false;
            // The existing pad-mask lowering has its own persistent reload
            // local. Do not allocate a second snapshot for the same value.
            return !instructions.Skip(producer + 1).Take(consumers.Max() - producer - 1)
                .Any(i => i.OpCode == ILOpCode.Call && i.String is "pad_poll" or "pad_trigger");
        }

        for (int i = 0; i < instructions.Length; i++)
        {
            var inputs = analysis.Inputs[i];
            if (!arrayOperands.Contains(i) && IsScalarBinary(instructions[i].OpCode) && inputs.Length == 2
                && inputs.All(p => p >= 0 && scalar[p])
                && instructions[inputs[1]].GetLdcValue() == null
                && instructions[inputs[0]].GetLdcValue() == null
                && instructions[inputs[1]].OpCode is not (ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2))
            {
                // Adjacent loads have a direct memory-operand lowering. A computed
                // right operand instead needs snapshots before it overwrites A.
                bool adjacentLoads = inputs[0] == i - 2 && inputs[1] == i - 1
                    && instructions[inputs[0]].GetLdlocIndex() != null
                    && instructions[inputs[1]].GetLdlocIndex() != null;
                bool runtimeThenLoad = inputs[0] == i - 2 && inputs[1] == i - 1
                    && instructions[inputs[0]].GetLdlocIndex() == null
                    && instructions[inputs[0]].GetLdcValue() == null
                    && (instructions[inputs[1]].GetLdlocIndex() != null
                        || instructions[inputs[1]].OpCode is >= ILOpCode.Ldarg_0 and <= ILOpCode.Ldarg_3);
                if (!adjacentLoads && !runtimeThenLoad)
                {
                    foreach (int input in inputs) spills.Add(input);
                }
            }
            foreach (int producer in inputs)
            {
                if (producer < 0 || !scalar[producer] || arrayOperands.Contains(producer)
                    || HasPreservedPadResult(producer))
                    continue;
                for (int j = producer + 1; j < i; j++)
                {
                    if ((instructions[j].GetStlocIndex() != null && !analysis.Inputs[j].Contains(producer))
                        || instructions[j].OpCode is ILOpCode.Call or ILOpCode.Stsfld)
                    {
                        spills.Add(producer);
                        break;
                    }
                }
            }
        }

        // All operands above a spilled input must also leave the IL stack, so
        // reloading snapshots never swaps operands or duplicates evaluation.
        var closedSpills = new HashSet<int>();
        foreach (int seed in spills)
        {
            var closure = new HashSet<int> { seed };
            var pending = new Queue<int>();
            pending.Enqueue(seed);
            bool compatible = true;
            while (pending.Count > 0 && compatible)
            {
                int producer = pending.Dequeue();
                foreach (int consumer in analysis.Consumers[producer])
                {
                    var inputs = analysis.Inputs[consumer];
                    int first = Array.IndexOf(inputs, producer);
                    foreach (int input in inputs.Skip(first))
                    {
                        if (input < 0 || !scalar[input] || arrayOperands.Contains(input))
                        {
                            compatible = false;
                            break;
                        }
                        if (closure.Add(input))
                            pending.Enqueue(input);
                    }
                }
            }
            if (compatible)
                closedSpills.UnionWith(closure);
        }
        var spillLocals = new Dictionary<int, int>();
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis, closedSpills, words,
            spillLocals, signature?.Locals.Length ?? 0);
        if (signature != null && spillLocals.Count > 0)
        {
            var locals = signature.Locals.ToBuilder();
            foreach (var pair in spillLocals)
            {
                while (locals.Count <= pair.Value)
                    locals.Add(null);
                locals[pair.Value] = types[pair.Key];
            }
            NumericTypes[method] = signature with { Locals = locals.ToImmutable() };
        }
        return rewritten;
    }

    static bool IsScalarBinary(ILOpCode op) => op is ILOpCode.Add or ILOpCode.Sub
        or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor;

    static bool IsScalarExpression(ILOpCode op) => IsScalarBinary(op)
        || op is ILOpCode.Mul or ILOpCode.Div or ILOpCode.Rem or ILOpCode.Shl
            or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Neg or ILOpCode.Not
            or ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_u4;
}
