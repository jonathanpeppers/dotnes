using System.Reflection.Metadata;

namespace dotnes;

static class ILBranchTargets
{
    public static bool HasEntryAfter(ILInstruction[] instructions, int firstIndex, int lastIndex)
    {
        var interiorOffsets = new HashSet<int>(instructions.Skip(firstIndex + 1)
            .Take(lastIndex - firstIndex).Select(instruction => instruction.Offset));
        return instructions.SelectMany(GetTargets).Any(interiorOffsets.Contains);
    }

    public static int? GetTarget(ILInstruction instruction)
    {
        if (instruction.Integer is not int operand)
            return null;
        if (instruction.OpCode is >= ILOpCode.Br_s and <= ILOpCode.Blt_un_s or ILOpCode.Leave_s)
            return instruction.Offset + 2 + unchecked((sbyte)operand);
        if (instruction.OpCode is >= ILOpCode.Br and <= ILOpCode.Blt_un or ILOpCode.Leave)
            return instruction.Offset + 5 + operand;
        return null;
    }

    public static IEnumerable<int> GetTargets(ILInstruction instruction)
    {
        if (GetTarget(instruction) is int target)
            yield return target;
        else if (instruction.OpCode == ILOpCode.Switch && instruction.Bytes is { } bytes
            && instruction.Integer is int count)
        {
            int start = instruction.Offset + 5 + count * 4;
            var targets = bytes.ToArray();
            for (int i = 0; i < count; i++)
                yield return start + BitConverter.ToInt32(targets, i * 4);
        }
    }
}
