using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ByteHelperStackAnalysisTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void BatchedEntriesAndExactInlineCleanupHaveBalancedEffects(byte count)
    {
        var program = new Program6502();
        var main = ReserveFrame(count);
        for (byte slot = 0; slot < count; slot++)
            main.Emit(LDY(slot)).Emit(LDA(slot)).Emit(STA_ind_Y(NESConstants.sp));
        main.Emit(JSR("helper_parameters_ready")).Emit(JMP_abs("done"), "done");
        program.AddMainProgram(main);
        var helper = new Block("helper").Emit(JSR("pusha"))
            .Emit(NOP(), "helper_parameters_ready");
        AppendCleanup(helper, count);
        program.AddMainProgram(helper);
        var analyzer = new Transpiler.ByteHelperStackAnalysis(program, NESConstants.LocalStackBase,
            new Dictionary<string, string> { ["helper"] = "helper_parameters_ready" });
        Assert.True(analyzer.TryAnalyze("main", out var effect));
        Assert.Equal(new Transpiler.ByteHelperStackEffect(0, 0, count), effect);
    }

    [Theory]
    [InlineData("out-of-frame")]
    [InlineData("branch-past-y")]
    [InlineData("unknown-y")]
    [InlineData("broken-reservation")]
    [InlineData("broken-cleanup")]
    public void UnprovenBatchedFrameSequencesFailClosed(string scenario)
    {
        var program = new Program6502();
        var main = ReserveFrame(2);
        if (scenario == "broken-reservation")
            main.Replace(2, ADC(2));
        if (scenario == "branch-past-y")
            main.Emit(BEQ("value"));
        main.Emit(scenario == "unknown-y" ? TAY() : LDY(scenario == "out-of-frame" ? (byte)2 : (byte)0))
            .Emit(LDA(17), "value")
            .Emit(STA_ind_Y(NESConstants.sp));
        if (scenario == "broken-cleanup")
        {
            var cleanup = BuiltInSubroutines.Incsp2();
            main.EmitRange(cleanup.InstructionsWithLabels.Select(i => i.Instruction));
            main.Emit(NOP());
        }
        else
            main.Emit(JSR("incsp2")).Emit(JMP_abs("done"), "done");
        program.AddMainProgram(main);
        var analyzer = new Transpiler.ByteHelperStackAnalysis(program, NESConstants.LocalStackBase);
        Assert.False(analyzer.TryAnalyze("main", out _));
    }

    static Block ReserveFrame(byte count) => new Block("main")
        .Emit(LDA_zpg(NESConstants.sp)).Emit(SEC()).Emit(SBC(count))
        .Emit(STA_zpg(NESConstants.sp)).Emit(BCS(2)).Emit(DEC_zpg(NESConstants.sp + 1));

    static void AppendCleanup(Block block, byte count)
    {
        var cleanup = count == 2 ? BuiltInSubroutines.Incsp2() : BuiltInSubroutines.Addysp();
        if (count != 2)
            block.Emit(LDY(count));
        block.EmitRange(cleanup.InstructionsWithLabels.Select(i => i.Instruction));
    }

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
