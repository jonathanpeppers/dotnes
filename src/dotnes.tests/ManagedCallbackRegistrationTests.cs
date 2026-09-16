using dotnes.ObjectModel;
using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ManagedCallbackRegistrationTests(ITestOutputHelper output) : RoslynTests(output)
{
    const string MapR7 = "poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 1);";
    const string NativeCallbacks = """
        .segment "CODE"
        bank_nmi:
        _NativeNmi:
            lda #5
            sta $8000
            lda #$33
            sta $8001
            ldx #$AA
            ldy #$BB
            sec
            rts
        bank_irq:
        _NativeIrq:
            lda #4
            sta $8000
            lda #$44
            sta $8001
            ldx #$CC
            ldy #$DD
            sec
            rts
        """;

    BankedCompilation Compile(string before, string after = "", string native = NativeCallbacks)
    {
        var assembly = CompileAssembly($$"""
            ppu_use_native_renderer();
            {{before}}
            unsafe { nmi_set_callback(&NativeNmi); irq_set_callback(&NativeIrq); }
            {{after}}
            Audio.Tick();
            poke(0x600F, 0xA5);
            while (true) ;
            static extern void NativeNmi();
            static extern void NativeIrq();
            [NESCodeBank("audio")]
            static class Audio
            {
                public static void Tick() { poke(0x6000, 0x42); }
            }
            """, allowUnsafe: true);
        string path = Path.Combine(Path.GetTempPath(), $"dotnes-callbacks-{Guid.NewGuid():N}.s");
        try
        {
            File.WriteAllText(path, native);
            var options = new CompilationOptions
            {
                Mapper = 4, PrgBanks = 3, Mmc3BankedLayout = true, Mmc3ManagedHomeBank = 0,
                Mmc3ManagedInterruptContract = Mmc3ManagedInterruptContract.NonNestingChrCallbacks,
                ManagedCodeBanks = { new() { Name = "audio", Bank = 2, Size = 0x1000 } },
                PrgBankAssets = { new() { Path = path, Bank = 1, CpuAddress = 0xA000, Offset = 0x0D00 } },
            };
            return NesCompiler.CompileBanked(assembly, options, logger: _logger);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(MapR7)]
    public void PublicRegistrationBindsPermanentNativeCallbacks(string after)
    {
        var result = Compile(MapR7, after);
        var native = Assert.IsType<Program6502>(Assert.Single(result.PrgAssets).Program);
        Assert.Equal(0xAD00, native.BaseAddress);
        Assert.Equal(native.GetLabels()["bank_nmi"], result.FixedProgram.GetLabels()["_NativeNmi"]);
        Assert.Equal(native.GetLabels()["bank_irq"], result.FixedProgram.GetLabels()["_NativeIrq"]);
        byte[] fixedBytes = result.FixedProgram.ToBytes();
        byte[] nativeBytes = native.ToBytes();
        result.ResolveAndRelax();
        Assert.Equal(fixedBytes, result.FixedProgram.ToBytes());
        Assert.Equal(nativeBytes, native.ToBytes());
    }

    [Theory]
    [InlineData("", MapR7)]
    [InlineData("poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 2);", "")]
    [InlineData("if (peek(0x6200) == 0) { " + MapR7 + " }", "")]
    [InlineData("if (peek(0x6200) == 0) { " + MapR7 + " } else { poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 2); }", "")]
    [InlineData("poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, peek(0x6200));", "")]
    public void RegistrationRequiresCorrectMappingOnEveryIncomingPath(string before, string after)
    {
        var error = Assert.Throws<TranspileException>(() => Compile(before, after));
        Assert.Contains("registration requires proven R7 bank 1", error.Message);
    }

    [Theory]
    [InlineData("poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 2);", "changes R7")]
    [InlineData("if (peek(0x6200) != 0) { poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 2); }", "changes R7")]
    [InlineData("poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, 2); " + MapR7, "changes R7")]
    [InlineData("poke(MMC3_BANK_SELECT, 7); poke(MMC3_BANK_DATA, peek(0x6200));", "PRG R6/R7 data must be constant")]
    public void LaterR7ChangesRemainRejected(string after, string diagnostic)
    {
        var error = Assert.Throws<TranspileException>(() => Compile(MapR7, after));
        Assert.Contains(diagnostic, error.Message);
    }

    [Theory]
    [InlineData(6, "changes PRG R6/R7")]
    [InlineData(7, "native R7 code requires physical bank 1")]
    public void CallbackCannotChangeItsPrgMapping(int register, string diagnostic)
    {
        var error = Assert.Throws<TranspileException>(() => Compile(MapR7, native:
            NativeCallbacks.Replace("lda #5", $"lda #{register}")));
        Assert.Contains(diagnostic, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationDoesNotImportForegroundRegistersOrRam(bool memory)
    {
        string native = $"""
            _NativeNmi:
                {(memory ? "lda $6002" : "nop")}
                sta $8000
                rts
            _NativeIrq:
                rts
            """;
        var error = Assert.Throws<TranspileException>(() =>
            Compile(MapR7 + " poke(0x6002, 5);", native: native));
        Assert.Contains("dynamic MMC3 selector", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnknownOrSplitCallbackAddressIsNotARegistrationProof(int kind)
    {
        var fixedProgram = new Program6502 { BaseAddress = 0xC000 };
        var main = fixedProgram.CreateBlock("main")
            .Emit(LDA(7)).Emit(STA_abs(0x8000)).Emit(LDA(1)).Emit(STA_abs(0x8001));
        main.Emit(kind == 0 ? LDA_abs(0x6000) :
            new Instruction(Opcode.LDA, AddressMode.Immediate_LowByte, new LowByteOperand("_Nmi")));
        main.Emit(kind == 1 ? LDX_abs(0x6001) :
            new Instruction(Opcode.LDX, AddressMode.Immediate_HighByte, new HighByteOperand(kind == 2 ? "_Other" : "_Nmi")));
        if (kind == 3)
            main.Emit(LDA(0));
        main.Emit(JSR(nameof(NESLib.nmi_set_callback))).Emit(RTS());
        var setter = BuiltInSubroutines.NmiSetCallback();
        fixedProgram.AddBlock(setter);
        var native = new Program6502 { BaseAddress = 0xAD00 };
        native.AddNativeBlock(new Block("_Nmi").Emit(RTS()));
        native.AddNativeBlock(new Block("_Other").Emit(RTS()));
        var asset = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xA000, Offset = 0x0D00 }, native, null);
        BankedCompilation.LinkPrograms([fixedProgram, native]);

        var error = Assert.Throws<TranspileException>(() => ManagedMapperSafety.Prepare(
            [fixedProgram, native], ["_Nmi", "_Other"], ["main"], [], 0x500, 0,
            managedBlocks: [main], compilerOwnedBlocks: [setter], prgAssets: [asset]));

        Assert.Contains("direct symbolic native function address", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredR7CallbackExecutesThroughStockDispatcherDuringManagedGate(bool irq)
    {
        var result = Compile(MapR7);
        var machine = new ManagedBankExecutionTests.BankMachine(result, 0);
        var labels = result.FixedProgram.GetLabels();
        ushort callback = labels[irq ? "_NativeIrq" : "_NativeNmi"];
        ushort banked = Assert.Single(result.Regions).Program.GetBlockAddress(
            Assert.Single(result.Regions[0].Program.Blocks));
        bool injected = false, entered = false, resumed = false;
        (ushort PC, byte A, byte X, byte Y, byte SP, byte Status, ushort SoftwareSP) saved = default;
        machine.Cpu.BeforeInstruction = cpu =>
        {
            if (cpu.PC == callback)
            {
                Assert.Equal(1, machine.R7Bank);
                entered = true;
            }
            if (injected && cpu.PC == saved.PC && cpu.SP == saved.SP)
            {
                Assert.Equal(saved, (cpu.PC, cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer));
                resumed = true;
            }
            if (injected || cpu.PC != banked)
                return;
            saved = (cpu.PC, cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer);
            injected = true;
            if (irq)
                Assert.True(cpu.Irq());
            else
                cpu.Nmi();
        };

        machine.Run();

        Assert.True(injected && entered && resumed);
        Assert.Equal(1, machine.Cpu.InterruptCount);
        Assert.Equal(1, machine.R7Bank);
        Assert.Equal(0, machine.Bank);
        Assert.Equal(irq ? 0x44 : 0x33, machine.Chr[irq ? 4 : 5]);
        Assert.Equal(0x42, machine.Cpu.Memory[0x6000]);
        Assert.Equal(0xFF, machine.Cpu.SP);
        Assert.Equal(0x0800, machine.Cpu.SoftwareStackPointer);
    }
}
