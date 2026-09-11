namespace dotnes;

static class ILBranchTargets
{
    public static bool HasEntryAfter(ILInstruction[] instructions, int firstIndex, int lastIndex)
    {
        var interiorOffsets = new HashSet<int>(instructions.Skip(firstIndex + 1)
            .Take(lastIndex - firstIndex).Select(instruction => instruction.Offset));
        return instructions.SelectMany(ILValueAnalysis.GetBranchTargets).Any(interiorOffsets.Contains);
    }
}
