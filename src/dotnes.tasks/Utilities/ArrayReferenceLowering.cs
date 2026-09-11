using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Replaces captured byte-element references across calls with explicit accesses.
/// Identity, index, and the old element value keep their original evaluation time.
/// </summary>
static class ArrayReferenceLowering
{
    public static ILInstruction[] Rewrite(ILInstruction[] instructions, ReflectionCache reflection)
    {
        if (!instructions.Any(i => i.OpCode == ILOpCode.Ldelema && i.String == "Byte"))
            return instructions;
        var analysis = new ILValueAnalysis(instructions, reflection);
        var replacements = new Dictionary<int, ILInstruction[]>();
        int nextOffset = Math.Min(-1, instructions.Min(i => i.Offset) - 1);

        bool CapturedLoad(int producer) => producer >= 0 &&
            (instructions[producer].GetLdlocIndex() is not null || instructions[producer].GetLdcValue() is not null);

        bool ExclusiveInput(int consumer, int input, int producer) =>
            analysis.Inputs[consumer][input] == producer &&
            analysis.Consumers[producer].All(c => c == consumer);

        bool UnchangedLocal(int producer, int end)
        {
            if (instructions[producer].GetLdlocIndex() is not int local)
                return instructions[producer].GetLdcValue() is not null;
            return !instructions.Skip(producer + 1).Take(end - producer).Any(i =>
                i.GetStlocIndex() == local ||
                i.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s && i.Integer == local);
        }

        void Replace(int index, params ILInstruction[] sequence)
        {
            replacements.Add(index, sequence.Select((instruction, i) =>
                ILExpressionSpiller.Relocate(instruction, i == 0 ? instructions[index].Offset : nextOffset--)).ToArray());
        }

        var nop = new ILInstruction(ILOpCode.Nop);
        for (int reference = 2; reference < instructions.Length; reference++)
        {
            if (instructions[reference].OpCode != ILOpCode.Ldelema || instructions[reference].String != "Byte")
                continue;
            var consumers = analysis.Consumers[reference];
            if (consumers.Count == 0 ||
                !instructions.Skip(reference + 1).Take(consumers.Max() - reference).Any(i => i.OpCode == ILOpCode.Call))
                continue;

            var stores = consumers.Where(i => instructions[i].OpCode == ILOpCode.Stind_i1).ToArray();
            int array = reference - 2, index = reference - 1;
            if (analysis.Escapes[reference] || stores.Length != 1 ||
                consumers.Any(i => instructions[i].OpCode is not (ILOpCode.Dup or ILOpCode.Ldind_u1 or ILOpCode.Stind_i1)) ||
                analysis.Inputs[reference].Length != 2 || instructions[array].GetLdlocIndex() is null || !CapturedLoad(index) ||
                !ExclusiveInput(reference, 0, array) || !ExclusiveInput(reference, 1, index) ||
                analysis.Predecessors[index].Any(p => p != array) ||
                analysis.Predecessors[reference].Any(p => p != index))
                throw new TranspileException("Byte array references across calls require captured identity and index operands.");

            int store = stores[0], value = store - 1;
            if (store <= reference || !UnchangedLocal(array, store) || !UnchangedLocal(index, store) ||
                !CapturedLoad(value) || analysis.Inputs[store].Length != 2 ||
                !ExclusiveInput(store, 1, value) || analysis.Predecessors[store].Any(p => p != value))
                throw new TranspileException("Byte array references across calls require unchanged captured operands and a captured store value.");

            Replace(array, nop);
            Replace(index, nop);
            Replace(reference, nop);
            foreach (int consumer in consumers)
            {
                if (instructions[consumer].OpCode == ILOpCode.Dup)
                    Replace(consumer, nop);
                else if (instructions[consumer].OpCode == ILOpCode.Ldind_u1)
                    Replace(consumer, instructions[array], instructions[index], new ILInstruction(ILOpCode.Ldelem_u1));
                else
                {
                    Replace(value, instructions[array], instructions[index], instructions[value]);
                    Replace(store, new ILInstruction(ILOpCode.Stelem_i1));
                }
            }
        }
        return replacements.Count == 0 ? instructions : instructions.SelectMany((instruction, i) =>
            replacements.TryGetValue(i, out var sequence) ? sequence : new[] { instruction }).ToArray();
    }
}
