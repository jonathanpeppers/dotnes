using System.Reflection.Metadata;

namespace dotnes;

static class ByteIndexDisplacement
{
    internal static bool TryMatch(ILInstruction[] instructions, ILValueAnalysis analysis, int read,
        out int arrayLocal, out int variable, out int displacement)
    {
        arrayLocal = variable = displacement = 0;
        if (read < 4 || read + 1 >= instructions.Length
            || instructions[read].OpCode != ILOpCode.Ldelem_u1
            || instructions[read + 1].GetStlocIndex() == null
            || !analysis.Inputs[read].SequenceEqual([read - 4, read - 1])
            || analysis.Outputs[read].Length != 1
            || !analysis.Consumers[read].SequenceEqual([read + 1])
            || instructions[read - 4].GetLdlocIndex() is not int array
            || instructions[read - 1].OpCode is not (ILOpCode.Add or ILOpCode.Sub)
            || !analysis.Inputs[read - 1].SequenceEqual([read - 3, read - 2])
            || ILBranchTargets.HasEntryAfter(instructions, read - 4, read + 1))
            return false;
        variable = read - 3;
        int constant = read - 2;
        if (instructions[read - 1].OpCode == ILOpCode.Add
            && instructions[variable].GetLdcValue().HasValue)
            (variable, constant) = (constant, variable);
        if (instructions[constant].GetLdcValue() is not int value || value is < 0 or > byte.MaxValue
            || instructions[variable].GetLdlocIndex() == null
            || !analysis.Consumers[variable].SequenceEqual([read - 1])
            || !analysis.Consumers[constant].SequenceEqual([read - 1]))
            return false;
        arrayLocal = array;
        displacement = instructions[read - 1].OpCode == ILOpCode.Sub ? -value : value;
        return true;
    }
}
