using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Materializes selected stack values at their original evaluation points.
/// Callers supply a closed set of scalar producers; array identities stay in IL.
/// </summary>
static class ILExpressionSpiller
{
    public static ILInstruction[] Rewrite(
        ILInstruction[] instructions, ILValueAnalysis analysis, ISet<int> producers,
        ISet<int>? wordProducers = null)
    {
        if (producers.Count == 0)
            return instructions;

        int nextLocal = instructions.Select(i => i.GetLdlocIndex() ?? i.GetStlocIndex() ?? -1).DefaultIfEmpty(-1).Max() + 1;
        int nextOffset = Math.Min(-1, instructions.Min(i => i.Offset) - 1);
        var locals = new Dictionary<int, int>();
        foreach (int producer in producers.OrderBy(i => i))
        {
            if (!analysis.ProducesValue[producer] || analysis.Escapes[producer])
                throw new InvalidOperationException($"Cannot spill IL value at index {producer} across an unknown control-flow boundary.");
            if (instructions[producer].GetLdcValue() == null)
                locals.Add(producer, nextLocal++);
        }

        var result = new List<ILInstruction>();
        for (int i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var inputs = analysis.Inputs[i];
            bool reloads = false;
            foreach (int producer in inputs)
            {
                if (!producers.Contains(producer))
                {
                    if (reloads)
                        throw new InvalidOperationException($"Spilling IL inputs at index {i} would change operand order.");
                    continue;
                }
                reloads = true;
            }

            if (instruction.OpCode == ILOpCode.Dup && inputs.Length == 1 && producers.Contains(inputs[0]))
            {
                result.Add(new ILInstruction(ILOpCode.Nop, instruction.Offset));
                continue;
            }

            bool first = true;
            foreach (int producer in inputs)
            {
                if (!producers.Contains(producer))
                    continue;
                int offset = first ? instruction.Offset : nextOffset--;
                first = false;
                if (instructions[producer].GetLdcValue() != null)
                    result.Add(instructions[producer] with { Offset = offset });
                else
                    result.Add(new ILInstruction(ILOpCode.Ldloc_s, offset, locals[producer]));
            }
            if (!first)
                instruction = instruction with { Offset = nextOffset-- };

            if (producers.Contains(i) && instruction.GetLdcValue() != null)
            {
                result.Add(new ILInstruction(ILOpCode.Nop, instruction.Offset));
                continue;
            }
            result.Add(instruction);
            if (locals.TryGetValue(i, out int local))
            {
                if (wordProducers?.Contains(i) == true)
                    result.Add(new ILInstruction(ILOpCode.Conv_u2, nextOffset--));
                result.Add(new ILInstruction(ILOpCode.Stloc_s, nextOffset--, local));
            }
        }
        return result.ToArray();
    }
}
