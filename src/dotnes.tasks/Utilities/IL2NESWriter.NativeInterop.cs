using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

partial class IL2NESWriter
{
    string GetDirectCallbackMethod(string setter)
    {
        var instructions = Instructions ?? throw new InvalidOperationException("Instructions must be set.");
        int pointerIndex = Index - 1;
        while (pointerIndex >= 0 && instructions[pointerIndex].OpCode == ILOpCode.Nop)
            pointerIndex--;

        TranspileException InvalidCallback() => new(
            $"{setter} requires a direct function address (&Handler) of a static managed or extern method, " +
            "not a conditional expression or function-pointer variable.", MethodName);

        if (pointerIndex < 0 || instructions[pointerIndex].OpCode != ILOpCode.Ldftn ||
            instructions[pointerIndex].String is not string method)
            throw InvalidCallback();

        int pointerOffset = instructions[pointerIndex].Offset;
        int setterOffset = instructions[Index].Offset;
        bool EntersAfterPointer(int target) => target > pointerOffset && target <= setterOffset;

        // Adjacent ldftn/call is insufficient: another conditional arm may branch to the call.
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode.IsBranch() && instruction.Integer is int operand)
            {
                int size = instruction.OpCode.GetBranchOperandSize();
                int displacement = size == 1 ? (sbyte)(byte)operand : operand;
                if (EntersAfterPointer(instruction.Offset + 1 + size + displacement))
                    throw InvalidCallback();
            }
            else if (instruction.OpCode == ILOpCode.Switch && instruction.Bytes is { } targets)
            {
                int end = instruction.Offset + 5 + targets.Length;
                for (int i = 0; i < targets.Length; i += 4)
                {
                    int displacement = targets[i] | targets[i + 1] << 8 |
                        targets[i + 2] << 16 | targets[i + 3] << 24;
                    if (EntersAfterPointer(end + displacement))
                        throw InvalidCallback();
                }
            }
        }
        return method;
    }
}
