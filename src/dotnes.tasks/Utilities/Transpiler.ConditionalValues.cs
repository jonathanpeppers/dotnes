using System.Reflection.Metadata;

namespace dotnes;

partial class Transpiler
{
    ILInstruction[] MaterializeConditionalValues(ILInstruction[] instructions, ReflectionCache reflection, string method,
        IReadOnlyDictionary<int, PrimitiveTypeCode>? compactInts = null,
        Func<ILInstruction[], ArrayStorageAnalysis>? arrayAliases = null)
    {
        if (!NumericTypes.TryGetValue(method, out var signature))
            return instructions;

        while (true)
        {
            var analysis = new ILValueAnalysis(instructions, reflection);
            var types = GetExpressionValueTypes(instructions, analysis, reflection, method, compactInts);
            var arrays = arrayAliases?.Invoke(instructions);
            var identities = new HashSet<(ILInstruction[] Method, int Producer)>();
            if (arrays is not null)
                for (int i = 0; i < instructions.Length; i++)
                {
                    bool arrayAccess = instructions[i].OpCode is ILOpCode.Ldelem_u1 or ILOpCode.Stelem_i1 ||
                        instructions[i].OpCode == ILOpCode.Ldelema && instructions[i].String is "Byte" or "byte";
                    bool mergedAlias = instructions[i].GetStlocIndex() is not null &&
                        analysis.Inputs[i].Length == 1 && analysis.Inputs[i][0] < 0;
                    if (!arrayAccess && !mergedAlias)
                        continue;
                    identities.Clear();
                    var storage = arrays.GetInputIdentity(instructions, i, 0, identities);
                    if ((storage != ArrayStorage.Unknown || arrayAccess) &&
                        (storage is not (ArrayStorage.Ram or ArrayStorage.Rom or ArrayStorage.Parameter) || identities.Count != 1))
                        throw new ObjectModel.TranspileException("A conditional array alias must retain one proven array identity.", method);
                }
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
                identities.Clear();
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
                    bool direct = predecessor + 1 == i
                        && !ILValueAnalysis.GetBranchTargets(instructions[predecessor]).Contains(instructions[i].Offset);
                    bool jumped = instructions[predecessor].OpCode is ILOpCode.Br or ILOpCode.Br_s;
                    bool supported = arrays is null
                        ? types[source] is PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte
                            or PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16
                        : types[source] is null && arrays.GetIdentity(instructions, source, identities)
                            is ArrayStorage.Ram or ArrayStorage.Rom or ArrayStorage.Parameter;
                    if ((!direct && !jumped) || !supported)
                    {
                        valid = false;
                        break;
                    }
                    sources.Add(source);
                }
                if (!valid || sources.Count < 2)
                    continue;
                if (arrays is not null && identities.Count != 1)
                    throw new ObjectModel.TranspileException("A conditional array alias must retain one proven array identity.", method);
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
            var edges = analysis.Predecessors[join].ToDictionary(
                predecessor => predecessor, predecessor => analysis.Outputs[predecessor].Last());
            void StoreArm(int source, int storeOffset)
            {
                // Consume the arm value on its edge, not at its definition.
                // The ordinary spill pass can then preserve all other uses,
                // including a postfix increment between the definition and edge.
                if (type is PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16)
                {
                    bool signed = types[source] is PrimitiveTypeCode.SByte or PrimitiveTypeCode.Int16;
                    rewritten.Add(new(signed ? ILOpCode.Conv_i2 : ILOpCode.Conv_u2, storeOffset));
                    storeOffset = offset--;
                }
                rewritten.Add(new(ILOpCode.Stloc_s, storeOffset, local));
            }
            for (int i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                if (i == join)
                {
                    rewritten.Add(new(ILOpCode.Ldloc_s, instruction.Offset, local));
                    instruction = ILExpressionSpiller.Relocate(instruction, offset--);
                }
                if (edges.TryGetValue(i, out int source)
                    && instruction.OpCode is ILOpCode.Br or ILOpCode.Br_s)
                {
                    StoreArm(source, instruction.Offset);
                    instruction = ILExpressionSpiller.Relocate(instruction, offset--);
                    rewritten.Add(instruction);
                }
                else
                {
                    rewritten.Add(instruction);
                    if (edges.TryGetValue(i, out source))
                        StoreArm(source, offset--);
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
