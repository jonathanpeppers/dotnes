using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

partial class Transpiler
{
    static void EmitByteParameterCleanup(Block block, int bytes)
    {
        var cleanup = bytes == 2 ? BuiltInSubroutines.Incsp2() : BuiltInSubroutines.Addysp();
        if (bytes != 2)
            block.Emit(LDY(checked((byte)bytes)));
        // Keep the runtime's exact flags, registers, wrap handling, and relative
        // branches; the caller appends the final RTS at the same byte position.
        block.EmitRange(cleanup.InstructionsWithLabels.Take(cleanup.Count - 1).Select(i => i.Instruction));
    }
}
