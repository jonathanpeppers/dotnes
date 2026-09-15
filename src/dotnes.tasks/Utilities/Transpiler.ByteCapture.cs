using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

partial class Transpiler
{
    void ElideCapturedByteFrame(string name, ILInstruction[] il, Block block, int localEnd)
    {
        if (!NumericTypes.TryGetValue(name, out var types)
            || !types.Parameters.SequenceEqual([PrimitiveTypeCode.Byte])
            || types.ReturnType != PrimitiveTypeCode.Void
            || _closureMethodArgIndex.ContainsKey(name)
            || il.Length < 2 || il[0].GetLdargIndex() != 0
            || (il[1].GetStlocIndex() == null && il[1].OpCode != ILOpCode.Stsfld)
            || il.Skip(1).Any(i => i.GetLdargIndex() != null
                || i.OpCode is ILOpCode.Starg or ILOpCode.Starg_s or ILOpCode.Ldarga or ILOpCode.Ldarga_s)
            || ILBranchTargets.HasEntryAfter(il, -1, 1)
            || block.Count < 6 || !Calls(block[0], "pusha")
            || block[1] != LDY(0)
            || block[2] is not { Opcode: Opcode.LDA, Mode: AddressMode.IndirectIndexed,
                Operand: ImmediateOperand { Value: NESConstants.sp } }
            || block[3].Opcode != Opcode.STA || block[3].Mode != AddressMode.Absolute
            || !Calls(block[block.Count - 2], "incsp1") || block[block.Count - 1] != RTS())
            return;

        var labels = InstructionLabels(block);
        for (int i = 3; i < block.Count - 2; i++)
        {
            var instruction = block[i];
            if (instruction.Opcode is Opcode.JSR or Opcode.RTS or Opcode.RTI or Opcode.BRK
                or Opcode.PHA or Opcode.PLA or Opcode.PHP or Opcode.PLP or Opcode.TSX or Opcode.TXS)
                return;
            if (instruction.Mode is AddressMode.Absolute)
            {
                if (instruction.Opcode == Opcode.JMP)
                {
                    if (instruction.Operand is not LabelOperand { Label: string target }
                        || !labels.TryGetValue(target, out int destination) || destination < 3)
                        return;
                }
                else if (instruction.Operand is not AbsoluteOperand { Address: ushort address }
                    || (address < 0x2000 && (address < NESConstants.LocalStackBase
                        || address >= localEnd || address >= 0x0800)))
                    return;
            }
            else if (instruction.Mode == AddressMode.Relative)
            {
                if (instruction.Operand is not RelativeOperand { Label: string target }
                    || !labels.TryGetValue(target, out int destination) || destination < 3)
                    return;
            }
            else if (instruction.Mode is not (AddressMode.Immediate or AddressMode.Implied or AddressMode.Accumulator))
                return;
        }

        // The byte is captured before any effect; this leaf cannot observe either
        // stack or call opaque code. Its existing local/field storage is unchanged.
        var original = block.InstructionsWithLabels.ToArray();
        block.Clear();
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i].Label is string label)
                block.SetNextLabel(label);
            if (i == 0 || i == original.Length - 2)
                continue;
            block.Emit(i == 2 ? ORA(0) : original[i].Instruction);
        }
    }
}
