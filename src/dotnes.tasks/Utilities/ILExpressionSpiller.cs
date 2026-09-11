using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace dotnes;

/// <summary>
/// Materializes selected stack values at their original evaluation points.
/// Callers supply a closed set of scalar producers; array identities stay in IL.
/// </summary>
static class ILExpressionSpiller
{
    // Local/argument slot addresses are stable even when their contents change.
    internal static bool CanRematerialize(ILInstruction instruction) =>
        instruction.GetLdcValue() is not null ||
        instruction.OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s or ILOpCode.Ldarga or ILOpCode.Ldarga_s;

    public static ILInstruction[] Rewrite(
        ILInstruction[] instructions, ILValueAnalysis analysis, ISet<int> producers,
        ISet<int>? wordProducers = null, IDictionary<int, int>? spillLocals = null,
        int minimumLocalIndex = 0, ISet<int>? stableAddressProducers = null,
        ISet<int>? signedWordProducers = null)
    {
        if (producers.Count == 0)
            return instructions;

        bool Rematerialize(int producer) => CanRematerialize(instructions[producer])
            || stableAddressProducers?.Contains(producer) == true;

        int nextLocal = Math.Max(minimumLocalIndex, instructions.Select(i => i.GetLdlocIndex() ?? i.GetStlocIndex()
            ?? (i.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Ldloca_s ? i.Integer : null)
            ?? -1).DefaultIfEmpty(-1).Max() + 1);
        int nextOffset = Math.Min(-1, instructions.Min(i => i.Offset) - 1);
        var locals = new Dictionary<int, int>();
        foreach (int producer in producers.OrderBy(i => i))
        {
            if (!analysis.ProducesValue[producer] || analysis.Escapes[producer])
                throw new InvalidOperationException($"Cannot spill IL value at index {producer} across an unknown control-flow boundary.");
            if (!Rematerialize(producer))
            {
                spillLocals?.Add(producer, nextLocal);
                locals.Add(producer, nextLocal++);
            }
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
                if (Rematerialize(producer))
                    result.Add(instructions[producer] with { Offset = offset });
                else
                    result.Add(new ILInstruction(ILOpCode.Ldloc_s, offset, locals[producer]));
            }
            if (!first)
                instruction = Relocate(instruction, nextOffset--);

            if (producers.Contains(i) && Rematerialize(i))
            {
                result.Add(new ILInstruction(ILOpCode.Nop, instruction.Offset));
                continue;
            }
            result.Add(instruction);
            if (locals.TryGetValue(i, out int local))
            {
                if (wordProducers?.Contains(i) == true)
                    result.Add(new ILInstruction(signedWordProducers?.Contains(i) == true
                        ? ILOpCode.Conv_i2 : ILOpCode.Conv_u2, nextOffset--));
                result.Add(new ILInstruction(ILOpCode.Stloc_s, nextOffset--, local));
            }
        }
        return result.ToArray();
    }

    internal static ILInstruction Relocate(ILInstruction instruction, int offset)
    {
        if (instruction.OpCode == ILOpCode.Switch && instruction.Integer is int count)
        {
            var bytes = ILValueAnalysis.GetBranchTargets(instruction)
                .SelectMany(target => BitConverter.GetBytes(target - offset - 5 - count * 4))
                .ToImmutableArray();
            return instruction with { Offset = offset, Bytes = bytes };
        }
        if (ILValueAnalysis.GetBranchTarget(instruction) is not int target)
            return instruction with { Offset = offset };

        // Synthetic offsets need not be near the target. Promote a short
        // branch so its IL displacement is not truncated.
        var op = instruction.OpCode switch
        {
            ILOpCode.Br_s => ILOpCode.Br,
            ILOpCode.Brfalse_s => ILOpCode.Brfalse,
            ILOpCode.Brtrue_s => ILOpCode.Brtrue,
            ILOpCode.Beq_s => ILOpCode.Beq,
            ILOpCode.Bne_un_s => ILOpCode.Bne_un,
            ILOpCode.Bge_s => ILOpCode.Bge,
            ILOpCode.Bgt_s => ILOpCode.Bgt,
            ILOpCode.Ble_s => ILOpCode.Ble,
            ILOpCode.Blt_s => ILOpCode.Blt,
            ILOpCode.Bge_un_s => ILOpCode.Bge_un,
            ILOpCode.Bgt_un_s => ILOpCode.Bgt_un,
            ILOpCode.Ble_un_s => ILOpCode.Ble_un,
            ILOpCode.Blt_un_s => ILOpCode.Blt_un,
            ILOpCode.Leave_s => ILOpCode.Leave,
            _ => instruction.OpCode
        };
        return instruction with { OpCode = op, Offset = offset, Integer = target - offset - 5 };
    }
}
