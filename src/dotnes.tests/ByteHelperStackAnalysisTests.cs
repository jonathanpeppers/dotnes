using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ByteHelperStackAnalysisTests
{
    [Fact]
    public void BoundsPendingArgumentsAcrossNestedCalls()
    {
        var program = new Program6502();
        program.AddMainProgram(new Block("main")
            .Emit(JSR("pusha")).Emit(JSR("helper")).Emit(JSR("pair"))
            .Emit(JMP_abs("done"), "done"));
        program.AddMainProgram(new Block("helper")
            .Emit(JSR("pusha")).Emit(JSR("incsp1")).Emit(RTS()));
        program.AddMainProgram(new Block("pair")
            .Emit(JSR("pusha")).Emit(JSR("incsp2")).Emit(RTS()));

        var analyzer = new Transpiler.ByteHelperStackAnalysis(program, NESConstants.LocalStackBase);
        Assert.True(analyzer.TryAnalyze("main", out var effect));
        Assert.Equal(new Transpiler.ByteHelperStackEffect(0, 0, 2), effect);
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("join")]
    [InlineData("recursion")]
    [InlineData("unknown")]
    [InlineData("sp-write")]
    [InlineData("dynamic-write")]
    [InlineData("unallocated-ram")]
    public void UnprovenEffectsFailClosed(string scenario)
    {
        var program = new Program6502();
        var main = new Block("main");
        switch (scenario)
        {
            case "loop":
                main.Emit(JSR("pusha")).Emit(JMP_abs("main"));
                break;
            case "join":
                main.Emit(BEQ("join")).Emit(JSR("pusha")).Emit(NOP(), "join");
                break;
            case "recursion":
                main.Emit(JSR("helper"));
                program.AddMainProgram(new Block("helper").Emit(JSR("helper")).Emit(RTS()));
                break;
            case "unknown":
                main.Emit(JSR("unproven"));
                break;
            case "sp-write":
                main.Emit(STA_zpg(NESConstants.sp));
                break;
            case "dynamic-write":
                main.Emit(STA_ind_Y(NESConstants.ptr1));
                break;
            case "unallocated-ram":
                main.Emit(STA_abs(0x07FF));
                break;
        }
        main.Emit(JMP_abs("done"), "done");
        program.AddMainProgram(main);
        var analyzer = new Transpiler.ByteHelperStackAnalysis(program, NESConstants.LocalStackBase);
        Assert.False(analyzer.TryAnalyze("main", out _));
    }

    [Theory]
    [InlineData(0x0B26)] // mirror of a prospective parameter home
    [InlineData(0x0822)] // software stack pointer
    [InlineData(0x1823)] // software stack pointer high byte
    [InlineData(0x0814)] // NMI callback
    [InlineData(0x1016)] // NMI callback high byte
    [InlineData(0x181E)] // IRQ callback
    public void MirroredRamReadsAndWritesFailClosed(ushort address)
    {
        foreach (var access in new[] { LDA_abs(address), STA_abs(address) })
        {
            var program = new Program6502();
            program.AddMainProgram(new Block("main").Emit(access).Emit(JMP_abs("done"), "done"));
            var analyzer = new Transpiler.ByteHelperStackAnalysis(program, NESConstants.LocalStackBase + 1);
            Assert.False(analyzer.TryAnalyze("main", out _));
        }
    }
}
