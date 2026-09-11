using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    ILInstruction[] MaterializeConditionalValues(ILInstruction[] instructions, ReflectionCache reflection, string method)
    {
        if (!NumericTypes.TryGetValue(method, out var signature))
            return instructions;

        while (true)
        {
            var analysis = new ILValueAnalysis(instructions, reflection);
            var types = GetExpressionValueTypes(instructions, analysis, reflection, method);
            int join = -1;
            var sources = new HashSet<int>();
            PrimitiveTypeCode? type = null;
            for (int i = 0; i < instructions.Length; i++)
            {
                var predecessors = analysis.Predecessors[i];
                if (predecessors.Count < 2)
                    continue;
                int height = analysis.Outputs[predecessors[0]].Length;
                if (height == 0)
                    continue;
                sources.Clear();
                bool valid = true;
                foreach (int predecessor in predecessors)
                {
                    var values = analysis.Outputs[predecessor];
                    if (values.Length != height || values[height - 1] < 0)
                    {
                        valid = false;
                        break;
                    }
                    int source = values[height - 1];
                    // Each arm must finish by producing the value, optionally
                    // followed by its unconditional jump directly to the join.
                    bool direct = predecessor == source && predecessor + 1 == i;
                    bool jumped = predecessor == source + 1
                        && instructions[predecessor].OpCode is ILOpCode.Br or ILOpCode.Br_s;
                    if ((!direct && !jumped) || analysis.Consumers[source].Count != 0
                        || types[source] is not (PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte
                            or PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16))
                    {
                        valid = false;
                        break;
                    }
                    sources.Add(source);
                }
                if (!valid || sources.Count < 2)
                    continue;
                var sourceTypes = sources.Select(source => types[source]).Distinct().ToArray();
                type = sourceTypes.Length == 1 ? sourceTypes[0]
                    : sourceTypes.All(t => t is PrimitiveTypeCode.Byte or PrimitiveTypeCode.Boolean)
                        ? PrimitiveTypeCode.Byte
                        : sourceTypes.Any(t => t is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16)
                            ? PrimitiveTypeCode.Int16 : PrimitiveTypeCode.UInt16;
                join = i;
                break;
            }
            if (join < 0)
                return instructions;

            int local = Math.Max(signature.Locals.Length, instructions.Max(i =>
                i.GetLdlocIndex() ?? i.GetStlocIndex()
                ?? (i.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Ldloca_s ? i.Integer : null) ?? -1) + 1);
            int offset = Math.Min(-1, instructions.Min(i => i.Offset) - 1);
            var rewritten = new List<ILInstruction>();
            for (int i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                if (i == join)
                {
                    rewritten.Add(new(ILOpCode.Ldloc_s, instruction.Offset, local));
                    instruction = ILExpressionSpiller.Relocate(instruction, offset--);
                }
                rewritten.Add(instruction);
                if (sources.Contains(i))
                {
                    if (type is PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16)
                        rewritten.Add(new(types[i] is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16
                            ? ILOpCode.Conv_i2 : ILOpCode.Conv_u2, offset--));
                    rewritten.Add(new(ILOpCode.Stloc_s, offset--, local));
                }
            }
            var locals = signature.Locals.ToBuilder();
            while (locals.Count < local)
                locals.Add(null);
            locals.Add(type);
            signature = signature with { Locals = locals.ToImmutable() };
            NumericTypes[method] = signature;
            instructions = rewritten.ToArray();
        }
    }
}
