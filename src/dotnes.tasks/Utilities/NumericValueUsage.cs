using System.Reflection.Metadata;

namespace dotnes;

static class NumericValueUsage
{
    public static bool IsExplicitlyNarrowed(ILInstruction[] instructions, ILValueAnalysis values,
        int producer, bool byteOnly = false, Func<int, bool>? isCompactStore = null)
    {
        var visiting = new HashSet<int>();
        bool Visit(int node)
        {
            if (!visiting.Add(node) || values.Escapes[node])
                return false;
            foreach (int consumer in values.Consumers[node])
            {
                var instruction = instructions[consumer];
                if (instruction.OpCode is ILOpCode.Dup or ILOpCode.Conv_u1 or ILOpCode.Conv_i1)
                    continue;
                if (!byteOnly && instruction.OpCode is ILOpCode.Conv_u2 or ILOpCode.Conv_i2)
                    continue;
                if (instruction.OpCode is ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul
                    or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor or ILOpCode.Shl && Visit(consumer))
                    continue;
                if (instruction.GetStlocIndex() is int local && isCompactStore?.Invoke(local) == true)
                    continue;
                return false;
            }
            visiting.Remove(node);
            return true;
        }
        return Visit(producer);
    }
}
