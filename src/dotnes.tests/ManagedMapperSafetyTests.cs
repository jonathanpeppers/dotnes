using dotnes.ObjectModel;
using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ManagedMapperSafetyTests
{
    const ushort Shadow = 0x500;

    static Program6502 Program(ushort address = 0xc000) => new() { BaseAddress = address };

    static Block Native(Program6502 program, string name, params Instruction[] instructions)
    {
        var block = new Block(name).EmitRange(instructions);
        program.AddNativeBlock(block);
        return block;
    }

    public class ManagedMapperSafetyCompilerTests(ITestOutputHelper output) : RoslynTests(output)
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PoisonedSoftwareStackIsRejectedBeforeBankedArgumentPushes(bool native)
        {
            string source = $$"""
                {{(native ? "Poison();" : "poke(0x22, 2); poke(0x23, 0x80);")}}
                byte result = Audio.Add(peek(0x6200), peek(0x6201));
                poke(0x6000, result);
                while (true) ;
                static extern void Poison();
                [NESCodeBank("audio")]
                static class Audio
                {
                    public static byte Add(byte left, byte right) => (byte)(left + right);
                }
                """;
            using var assembly = CompileAssembly(source);
            using var nativeSource = new AssemblyReader(new StringReader("""
                _Poison:
                    lda #2
                    sta $22
                    lda #$80
                    sta $23
                    rts
                """));
            var options = new CompilationOptions
            {
                Mapper = 4,
                PrgBanks = 3,
                Mmc3BankedLayout = true,
                Mmc3ManagedHomeBank = 0,
                ManagedCodeBanks = { new() { Name = "audio", Bank = 2, Size = 0x1000 } },
            };
            var exception = Assert.Throws<TranspileException>(() =>
                NesCompiler.CompileBanked(assembly, options, [nativeSource], _logger));
            Assert.Contains("compiler-owned software-stack pointer $22/$23", exception.Message);
            Assert.Contains(native ? "_Poison" : "main", exception.Message);
        }

        [Theory]
        [InlineData("__nesbank_selector")]
        [InlineData("__nesbank_saved_selector")]
        public void CallbackCannotCorruptCompilerSelectorContext(string label)
        {
            using var assembly = CompileAssembly("""
                unsafe { nmi_set_callback(&Nmi); }
                poke(0x6000, Audio.Tick(peek(0x6200)));
                while (true) ;
                static extern void Nmi();
                [NESCodeBank("audio")]
                static class Audio
                {
                    public static byte Tick(byte input) => input;
                }
                """, allowUnsafe: true);
            using var nativeSource = new AssemblyReader(new StringReader($"""
                _Nmi:
                    lda #$40
                    sta {label}
                    rts
                """));
            var options = new CompilationOptions
            {
                Mapper = 4,
                PrgBanks = 3,
                Mmc3BankedLayout = true,
                Mmc3ManagedHomeBank = 0,
                Mmc3ManagedInterruptContract = Mmc3ManagedInterruptContract.NonNestingChrCallbacks,
                ManagedCodeBanks = { new() { Name = "audio", Bank = 2, Size = 0x1000 } },
            };
            var exception = Assert.Throws<TranspileException>(() =>
                NesCompiler.CompileBanked(assembly, options, [nativeSource], _logger));
            Assert.Contains("compiler-owned selector context", exception.Message);
            Assert.Contains("_Nmi", exception.Message);
        }
    }

    static void Prepare(Program6502 program, string[]? callbacks = null, string[]? foreground = null, string[]? banked = null) =>
        ManagedMapperSafety.Prepare([program], callbacks ?? [], foreground ?? [], banked ?? [], Shadow, 0);

    static NativeRamCode RamCode() => new()
    {
        Name = "Renderer", Address = 0x7420, Size = 4,
        Contract = NativeRamCodeContract.ForegroundRtsPreservesMapperContext
    };

    [Theory]
    [InlineData(Opcode.STA, 0x8000)]
    [InlineData(Opcode.STX, 0x9ffe)]
    [InlineData(Opcode.STY, 0x8002)]
    public void ForegroundPublishesSameRegisterBeforeSelector(Opcode opcode, int address)
    {
        var program = Program();
        var block = Native(program, "_init", LDA(0x86), TAX(), TAY(),
            new Instruction(opcode, AddressMode.Absolute, new AbsoluteOperand((ushort)address)),
            LDA(0), STA_abs(0x8001), RTS());
        block.SetLabel(3, "@select");
        Prepare(program, foreground: ["_init"]);
        Assert.Equal(new Instruction(opcode, AddressMode.Absolute, new AbsoluteOperand(Shadow)),
            block[3] with { Comment = null });
        Assert.Equal("@select", block.GetLabelAt(3));
        Assert.Null(block.GetLabelAt(4));
        Assert.Equal((ushort)address, Assert.IsType<AbsoluteOperand>(block[4].Operand).Address);
    }

    [Fact]
    public void CallbackInstructionsRemainByteIdentical()
    {
        var program = Program();
        var callback = Native(program, "_callback", LDA(0x80), STA_abs(0x8000),
            LDA_abs(0x300), STA_abs(0x8001), RTS());
        var init = Native(program, "_init", LDA(0x86), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        byte[] original = program.GetMainBlock("_callback");
        Prepare(program, ["_callback"], ["_init"]);
        Assert.Equal(5, callback.Count);
        Assert.Equal(6, init.Count);
        Assert.Equal(original, program.GetMainBlock("_callback"));
    }

    [Fact]
    public void NumericBranchToSelectorTargetsPublication()
    {
        var program = Program();
        var block = Native(program, "_init", LDA(6), new Instruction(Opcode.BCC, AddressMode.Relative, new RelativeByteOperand(1)),
            NOP(), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        Prepare(program, foreground: ["_init"]);
        var branch = Assert.IsType<RelativeOperand>(block[1].Operand);
        program.ResolveAddresses();
        Assert.Equal(program.GetInstructionAddress(block, 3), program.Labels.Resolve(branch.Label));
        Assert.Equal(Shadow, Assert.IsType<AbsoluteOperand>(block[3].Operand).Address);
        Assert.Equal(0x8000, Assert.IsType<AbsoluteOperand>(block[4].Operand).Address);
    }

    [Fact]
    public void NumericBranchOverSelectorKeepsOriginalTarget()
    {
        var program = Program();
        var block = Native(program, "_init", LDA(6), new Instruction(Opcode.BCC, AddressMode.Relative, new RelativeByteOperand(3)),
            STA_abs(0x8000), RTS());
        Prepare(program, foreground: ["_init"]);
        var branch = Assert.IsType<RelativeOperand>(block[1].Operand);
        program.ResolveAddresses();
        Assert.Equal(program.GetInstructionAddress(block, 4), program.Labels.Resolve(branch.Label));
        Assert.Equal(Opcode.RTS, block[4].Opcode);
        Assert.Equal(6, unchecked((sbyte)program.GetMainBlock("_init")[3]));
    }

    [Fact]
    public void CallbackCanCallSourceHelperAcrossStableImages()
    {
        var fixedProgram = Program();
        var callback = Native(fixedProgram, "_callback", JSR("_helper"), RTS());
        var asset = Program(0xe000);
        var helper = Native(asset, "_helper", LDA(0), STA_abs(0x8000), LDA_abs(0x300), STA_abs(0x8001), RTS());
        ManagedMapperSafety.Prepare([fixedProgram, asset], ["_callback"], [], [], Shadow, 0);
        Assert.Equal(2, callback.Count);
        Assert.Equal(5, helper.Count);
    }

    [Fact]
    public void FollowsNativeFallthroughAcrossBlocks()
    {
        var program = Program();
        Native(program, "_init", LDA(6));
        var continuation = Native(program, "continuation", STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        Prepare(program, foreground: ["_init"]);
        Assert.Equal(Shadow, Assert.IsType<AbsoluteOperand>(continuation[0].Operand).Address);
    }

    [Fact]
    public void SharedMapperWritingHelperIsRejected()
    {
        var program = Program();
        Native(program, "_callback", JSR("_helper"), RTS());
        Native(program, "_foreground", JSR("_helper"), RTS());
        Native(program, "_helper", LDA(0), STA_abs(0x8000), LDA_abs(0x300), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"], ["_foreground"]));
        Assert.Contains("shared", exception.Message);
    }

    [Fact]
    public void SharedRamOnlyHelperIsAllowed()
    {
        var program = Program();
        Native(program, "_callback", JSR("_helper"), RTS());
        Native(program, "_foreground", JSR("_helper"), RTS());
        Native(program, "_helper", STA_abs(0x300), RTS());
        Prepare(program, ["_callback"], ["_foreground"]);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void CallbackCannotChangePrg(int register)
    {
        var program = Program();
        Native(program, "_callback", LDA((byte)register), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("changes PRG", exception.Message);
    }

    [Fact]
    public void ForegroundMustPreserveHomeBank()
    {
        var program = Program();
        Native(program, "_init", LDA(6), STA_abs(0x8000), LDA(2), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, foreground: ["_init"]));
        Assert.Contains("home bank", exception.Message);
    }

    [Fact]
    public void R7CanOnlyHaveOneConstantInitialization()
    {
        var program = Program();
        Native(program, "_init", LDA(7), STA_abs(0x8000), LDA(1), STA_abs(0x8001), LDA(2), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, foreground: ["_init"]));
        Assert.Contains("R7 after initialization", exception.Message);
    }

    [Fact]
    public void BankedNativeCalleeCannotChangePrg()
    {
        var program = Program();
        program.CreateBlock("banked").Emit(JSR("_helper")).Emit(RTS());
        Native(program, "_helper", LDA(6), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, banked: ["banked"]));
        Assert.Contains("changes PRG", exception.Message);
    }

    [Fact]
    public void CallbackCannotEnterBankedManagedCode()
    {
        var program = Program();
        Native(program, "_callback", JSR("banked"), RTS());
        program.CreateBlock("banked").Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"], banked: ["banked"]));
        Assert.Contains("banked managed", exception.Message);
    }

    [Fact]
    public void DirectManagedMapperStoreIsInstrumented()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(LDA(0)).Emit(STA_abs(0x8000)).Emit(RTS());
        Prepare(program);
        Assert.Equal(Shadow, Assert.IsType<AbsoluteOperand>(main[1].Operand).Address);
    }

    [Fact]
    public void CompilerOwnedHelperIsNotInstrumented()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR("compiler_helper")).Emit(RTS());
        var helper = program.CreateBlock("compiler_helper").Emit(STA_abs(0x8000)).Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], [], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: [helper]);
        Assert.Equal(2, helper.Count);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0xc0)]
    public void PrgModeOneIsRejected(int selector)
    {
        var program = Program();
        Native(program, "_callback", LDA((byte)selector), STA_abs(0x8000), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("PRG mode 1", exception.Message);
    }

    [Fact]
    public void DynamicSelectorIsRejectedWithActionableDiagnostic()
    {
        var program = Program();
        Native(program, "_callback", LDA_abs(0x300), STA_abs(0x8000), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("load a constant selector", exception.Message);
    }

    [Fact]
    public void SelectorConstantsPropagateThroughHelperAndBitOperations()
    {
        var program = Program();
        var entry = Native(program, "_init", LDX(0x87), TXA(), AND(0xfe), PHA(), JSR("_helper"),
            PLA(), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        Native(program, "_helper", LDA(100), RTS());
        Prepare(program, foreground: ["_init"]);
        Assert.Equal(11, entry.Count);
    }

    [Fact]
    public void CallbackAndForegroundChrInversionMustAgree()
    {
        var program = Program();
        Native(program, "_callback", LDA(0x80), STA_abs(0x8000), RTS());
        Native(program, "_init", LDA(6), STA_abs(0x8000), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"], ["_init"]));
        Assert.Contains("CHR inversion", exception.Message);
    }

    [Fact]
    public void CallbackAllowsBoundedRamAndIndirectBuffers()
    {
        var program = Program();
        Native(program, "_callback", LDA(2), STA_zpg(0x11),
            new Instruction(Opcode.STA, AddressMode.AbsoluteX, new AbsoluteOperand(0x400)),
            new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(0x10)), RTS());
        Prepare(program, ["_callback"]);
    }

    [Fact]
    public void UnknownIndirectStoreIsRejected()
    {
        var program = Program();
        Native(program, "_callback",
            new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(0x10)), RTS());
        Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
    }

    [Theory]
    [InlineData(Opcode.RTI)]
    [InlineData(Opcode.BRK)]
    [InlineData(Opcode.TXS)]
    public void InvalidCallbackReturnMechanismsAreRejected(Opcode opcode)
    {
        var program = Program();
        Native(program, "_callback", new Instruction(opcode, AddressMode.Implied));
        Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
    }

    [Fact]
    public void CallbackIndirectTailJumpIsRejected()
    {
        var program = Program();
        Native(program, "_callback", new Instruction(Opcode.JMP, AddressMode.Indirect, new AbsoluteOperand(0x10)));
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("indirect", exception.Message);
    }

    [Fact]
    public void CallbackClosedLoopIsRejected()
    {
        var program = Program();
        Native(program, "_callback", JMP("_callback"));
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("cannot return", exception.Message);
    }

    [Fact]
    public void CalleeCannotPopCallersSavedAccumulator()
    {
        var program = Program();
        Native(program, "_callback", PHA(), JSR("_helper"), PLA(), RTS());
        Native(program, "_helper", PLA(), PHA(), RTS());
        Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
    }

    [Fact]
    public void AmbiguousNumericCrossImageCallIsRejected()
    {
        var program = Program();
        Native(program, "_callback", JSR(0xb000), RTS());
        var first = Program(0xb000);
        var second = Program(0xb000);
        Native(first, "first", RTS());
        Native(second, "second", RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program, first, second], ["_callback"], [], [], Shadow, 0));
        Assert.Contains("ambiguous", exception.Message);
    }

    [Fact]
    public void SymbolicIdentityDisambiguatesOverlappingImages()
    {
        var program = Program();
        Native(program, "_callback", JSR("second"), RTS());
        var first = Program(0xe000);
        var second = Program(0xe000);
        Native(first, "first", RTI());
        Native(second, "second", RTS());
        ManagedMapperSafety.Prepare([program, first, second], ["_callback"], [], [], Shadow, 0);
    }

    [Fact]
    public void NativeUnresolvedPathIsRejected()
    {
        var program = Program();
        Native(program, "_callback", JSR("missing"), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("unresolved", exception.Message);
    }

    [Fact]
    public void BranchesMayMergeDifferentConstantChrSelectors()
    {
        var program = Program();
        var callback = Native(program, "_callback",
            BEQ("@other"), LDA(2), STA_abs(0x8000), JMP("@data"),
            LDA(4), STA_abs(0x8000), LDA_abs(0x300), STA_abs(0x8001), RTS());
        callback.SetLabel(4, "@other");
        callback.SetLabel(6, "@data");
        Prepare(program, ["_callback"]);
        Assert.Equal(9, callback.Count);
    }

    [Fact]
    public void NativeEntryCannotAssumeCurrentSelector()
    {
        var program = Program();
        Native(program, "_helper", LDA(0), STA_abs(0x8001), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, foreground: ["_helper"]));
        Assert.Contains("unknown selector", exception.Message);
    }

    [Fact]
    public void AuthoredManagedCallPropagatesMapperContext()
    {
        var program = Program();
        program.CreateBlock("main").Emit(JSR("select")).Emit(LDA(1)).Emit(STA_abs(0x8001)).Emit(RTS());
        var select = program.CreateBlock("select").Emit(LDA(0)).Emit(STA_abs(0x8000)).Emit(RTS());
        Prepare(program, foreground: ["select"]);
        Assert.Equal(4, select.Count);
    }

    [Fact]
    public void ExistingAliasAndBlockOffsetContinueToTargetPublication()
    {
        var program = Program();
        var entry = Native(program, "_init", LDA(6), JMP("@selector"), NOP(), STA_abs(0x8000), RTS());
        entry.SetLabel(3, "@selector");
        entry.AdditionalLabels = ["_selector=_init:@selector"];
        entry.LabelOffset = 2;
        // Start at the prefix, since _init itself deliberately addresses its JMP.
        entry.SetLabel(0, "_prefix");
        Prepare(program, foreground: ["_prefix"]);
        program.ResolveAddresses();
        Assert.Equal(program.GetInstructionAddress(entry, 3), program.Labels.Resolve("_selector"));
        Assert.Equal(program.GetInstructionAddress(entry, 1), program.Labels.Resolve("_init"));
        Assert.Equal(Shadow, Assert.IsType<AbsoluteOperand>(entry[3].Operand).Address);
    }

    [Fact]
    public void CallbackProvablyInfiniteConditionalLoopIsRejected()
    {
        var program = Program();
        Native(program, "_callback", LDA(1), BNE("_callback"), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("cannot return", exception.Message);
    }

    [Fact]
    public void ProvenUnreachablePrgWriteDoesNotInvalidateCallback()
    {
        var program = Program();
        var callback = Native(program, "_callback", LDA(0), BEQ("@return"),
            LDA(6), STA_abs(0x8000), LDA(0), STA_abs(0x8001), RTS());
        callback.SetLabel(6, "@return");
        Prepare(program, ["_callback"]);
        Assert.Equal(7, callback.Count);
    }

    [Theory]
    [InlineData("__nesbank_gate___nesbank_method_00000001_Tick")]
    [InlineData("__nesbank_enter")]
    [InlineData("__nesbank_leave")]
    public void FixedManagedCallsDoNotInspectCompilerOwnedBanking(string label)
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR(label)).Emit(RTS());
        var helper = program.CreateBlock(label).Emit(STA_abs(0x8000)).Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: [helper]);
        Assert.Equal(2, helper.Count);
    }

    [Theory]
    [InlineData("__nesbank_gate___nesbank_method_00000001_Tick")]
    [InlineData("__nesbank_enter")]
    [InlineData("__nesbank_leave")]
    public void CallbackCannotEnterCompilerOwnedBanking(string label)
    {
        var program = Program();
        Native(program, "_callback", JSR(label), RTS());
        program.CreateBlock(label).Emit(STA_abs(0x8000)).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("native control flow enters managed/compiler code", exception.Message);
    }

    [Fact]
    public void BankedRootAlsoListedAsForegroundRetainsBankedContext()
    {
        var program = Program();
        const string method = "__nesbank_method_00000001_Tick";
        program.CreateBlock(method).Emit(LDA(0)).Emit(STA_abs(0x8001)).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            Prepare(program, foreground: [method], banked: [method]));
        Assert.Contains("changes PRG", exception.Message);
    }

    [Fact]
    public void ManagedArgumentStoreUsesCompilerStackBounds()
    {
        var program = Program();
        var main = program.CreateBlock("main");
        main.Emit(LDA_zpg(NESConstants.sp)).Emit(SEC()).Emit(SBC(2)).Emit(STA_zpg(NESConstants.sp))
            .Emit(new Instruction(Opcode.BCS, AddressMode.Relative, new RelativeByteOperand(2)))
            .Emit(new Instruction(Opcode.DEC, AddressMode.ZeroPage, new ImmediateOperand(NESConstants.sp + 1)))
            .Emit(LDY(0)).Emit(LDA(240))
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp)))
            .Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main]);
        Assert.Equal(10, main.Count);
    }

    [Fact]
    public void NativeArgumentStoreDoesNotInheritManagedStackProof()
    {
        var program = Program();
        program.CreateBlock("main").Emit(JSR("_native")).Emit(RTS());
        Native(program, "_native",
            new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp)), RTS());
        Assert.Throws<TranspileException>(() => Prepare(program));
    }

    [Fact]
    public void NativeCalleeCannotInvalidateManagedSoftwareStack()
    {
        var program = Program();
        program.CreateBlock("main").Emit(JSR("_native"))
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp))).Emit(RTS());
        Native(program, "_native", LDA(0x80), STA_zpg(NESConstants.sp + 1), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program));
        Assert.Contains("compiler-owned software-stack pointer", exception.Message);
    }

    [Fact]
    public void ManagedArbitraryPointerIsNotTreatedAsSoftwareStack()
    {
        var program = Program();
        program.CreateBlock("main")
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(0x20))).Emit(RTS());
        Assert.Throws<TranspileException>(() => Prepare(program));
    }

    [Fact]
    public void ManagedExplicitStackPointerCorruptionIsRejected()
    {
        var program = Program();
        program.CreateBlock("main").Emit(LDA(0x80)).Emit(STA_zpg(NESConstants.sp + 1))
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp))).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program));
        Assert.Contains("compiler-owned software-stack pointer", exception.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeForegroundCannotEnterBankedBodiesOrParameterReadyLabels(bool parameterReady, bool jump)
    {
        var fixedProgram = Program();
        const string method = "__nesbank_method_00000001_Tick";
        string target = parameterReady ? method + ":@parameters_ready" : method;
        Native(fixedProgram, "_native", jump ? JMP(target) : JSR(target), RTS());
        var region = Program(0x8000);
        var block = region.CreateBlock(method).Emit(NOP()).Emit(RTS());
        block.SetLabel(1, "@parameters_ready");
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([fixedProgram, region], [], ["_native"], [method], Shadow, 0));
        Assert.Contains(parameterReady ? "native control flow enters managed/compiler code" : "banked managed", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBankAssetCannotEnterGeneratedGate(bool jump)
    {
        var fixedProgram = Program();
        const string gate = "__nesbank_gate___nesbank_method_00000001_Tick";
        fixedProgram.CreateBlock(gate).Emit(RTS());
        fixedProgram.CreateBlock("main").Emit(LDA(7)).Emit(STA_abs(0x8000))
            .Emit(LDA(1)).Emit(STA_abs(0x8001)).Emit(JSR("_native")).Emit(RTS());
        var asset = Program(0xb000);
        Native(asset, "_native", jump ? JMP(gate) : JSR(gate), RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([fixedProgram, asset], [], ["main", "_native"], [], Shadow, 0,
                prgAssets: [new(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null)]));
        Assert.Contains("native control flow enters managed/compiler code", exception.Message);
    }

    [Fact]
    public void DeclaredForegroundRamCallPreservesSelectorButNotRegisters()
    {
        var program = Program();
        Native(program, "_render", LDA(0), STA_abs(0x8000), JSR(0x7420),
            LDA(0x33), STA_abs(0x8001), RTS());
        ManagedMapperSafety.Prepare([program], [], ["_render"], [], Shadow, 0, [RamCode()]);
        Assert.Equal(7, program.GetBlock("_render")!.Count);
    }

    [Fact]
    public void DeclaredForegroundRamCallInvalidatesPreCallValueFacts()
    {
        var program = Program();
        Native(program, "_render", LDA(0), JSR(0x7420), STA_abs(0x8000), RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["_render"], [], Shadow, 0, [RamCode()]));
        Assert.Contains("dynamic MMC3 selector", exception.Message);
    }

    [Theory]
    [InlineData(0x7421)]
    [InlineData(0x7440)]
    public void InteriorAndUndeclaredRamCallsAreRejected(int address)
    {
        var program = Program();
        Native(program, "_render", JSR((ushort)address), RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["_render"], [], Shadow, 0, [RamCode()]));
        Assert.Contains("exact NativeRamCode entry", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RamEntryIsNotAllowedFromBankedOrCallbackContext(bool callback)
    {
        var program = Program();
        if (callback)
            Native(program, "_entry", JSR(0x7420), RTS());
        else
            program.CreateBlock("_entry").Emit(JSR(0x7420)).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], callback ? ["_entry"] : [], [],
                callback ? [] : ["_entry"], Shadow, 0, [RamCode()]));
        Assert.Contains("foreground-only", exception.Message);
    }

    [Fact]
    public void RamTailJumpReturnsThroughOriginalCaller()
    {
        var program = Program();
        program.CreateBlock("main").Emit(JSR("_render")).Emit(LDA(0)).Emit(STA_abs(0x8000)).Emit(RTS());
        Native(program, "_render", JMP(0x7420));
        ManagedMapperSafety.Prepare([program], [], ["_render"], [], Shadow, 0, [RamCode()]);
        Assert.Equal(5, program.GetBlock("main")!.Count);
    }

    [Fact]
    public void RamTailJumpRequiresBalancedHardwareStack()
    {
        var program = Program();
        Native(program, "_render", PHA(), JMP(0x7420));
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["_render"], [], Shadow, 0, [RamCode()]));
        Assert.Contains("unbalanced", exception.Message);
    }

    [Fact]
    public void RamAliasCanBeDeclaredForegroundExternButNotCallback()
    {
        var program = Program();
        program.DefineExternalLabel("_thunk", 0x7420);
        program.CreateBlock("main").Emit(JSR("_thunk")).Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], ["_thunk"], [], Shadow, 0, [RamCode()]);
        Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], ["_thunk"], [], [], Shadow, 0, [RamCode()]));
    }

    [Fact]
    public void SourceVisibleRamCallbackCannotBypassContextRestriction()
    {
        var program = Program(0x7420);
        Native(program, "_callback", RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], ["_callback"], [], [], Shadow, 0, [RamCode()]));
        Assert.Contains("foreground-only", exception.Message);
    }

    [Fact]
    public void UnusedSourceNativeBlocksAreNotAnalyzed()
    {
        var program = Program();
        program.CreateBlock("main").Emit(RTS());
        Native(program, "_unused", JSR(0x7440), RTI());
        ManagedMapperSafety.Prepare([program], [], [], [], Shadow, 0, [RamCode()]);
    }

    [Fact]
    public void ContractedRamCallKeepsManagedSoftwareStackProof()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR(0x7420))
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp))).Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, [RamCode()], managedBlocks: [main]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InlinedCompilerStackReleasePreservesManagedBounds(int kind)
    {
        var program = Program();
        Block release = kind == 1 ? BuiltInSubroutines.Incsp1() :
            kind == 2 ? BuiltInSubroutines.Incsp2() : BuiltInSubroutines.Addysp();
        var main = program.CreateBlock("main").Emit(LDY(2))
            .EmitRange(release.InstructionsWithLabels.Select(item => item.Instruction));
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: []);
    }

    [Fact]
    public void ExplicitCompilerBlockIdentityAuthorizesOnlyThatBlock()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR("__nesbank_gate_fake")).Emit(RTS());
        var helper = program.CreateBlock("__nesbank_gate_fake").Emit(STA_abs(0x8000)).Emit(RTS());
        Assert.Throws<TranspileException>(() => Prepare(program));
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: []));
        Assert.Contains("registered compiler-owned helper", exception.Message);
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: [helper]);
    }

    [Fact]
    public void ExplicitBlockSetsCannotGrantNativeCodeCompilerProvenance()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR("__nesbank_gate_fake")).Emit(RTS());
        var native = Native(program, "__nesbank_gate_fake", STA_abs(0x8000), RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: [native]));
        Assert.Contains("source-native block cannot be declared", exception.Message);
        Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main, native], compilerOwnedBlocks: []));
    }

    [Fact]
    public void UnaffectedNumericBranchDoesNotChangeCompilerInstructionLabels()
    {
        var program = Program();
        var runtime = program.CreateBlock("runtime").Emit(new Instruction(Opcode.BNE, AddressMode.Relative, new RelativeByteOperand(0))).Emit(RTS());
        var original = runtime.InstructionsWithLabels.ToArray();
        program.CreateBlock("main").Emit(LDA(0)).Emit(STA_abs(0x8000)).Emit(RTS());
        Prepare(program);
        Assert.Equal(original, runtime.InstructionsWithLabels);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void NativeR7JumpTableRequiresMatchingPhysicalMapping(byte bank, bool valid)
    {
        var program = Program();
        Native(program, "_init", LDA(6), STA_abs(0x8000), LDA(0), STA_abs(0x8001),
            LDA(7), STA_abs(0x8000), LDA(bank), STA_abs(0x8001), JSR(0xb000), RTS());
        var asset = Program(0xb000);
        Native(asset, "_exports", JMP_abs(0xb006), JMP_abs(0xb006), RTS());
        var placement = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null);
        void Run() => ManagedMapperSafety.Prepare([program, asset], [], ["_init"], [], Shadow, 0, prgAssets: [placement]);
        if (valid)
            Run();
        else
            Assert.Contains("requires physical bank 1", Assert.Throws<TranspileException>(Run).Message);
    }

    [Fact]
    public void NativeR7CallRequiresInitializationOnEveryPath()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(BCC("@call")).Emit(LDA(7)).Emit(STA_abs(0x8000))
            .Emit(LDA(1)).Emit(STA_abs(0x8001)).Emit(JSR("_asset")).Emit(RTS());
        main.SetLabel(5, "@call");
        var asset = Program(0xb000);
        Native(asset, "_asset", RTS());
        var placement = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null);
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program, asset], [], ["main"], [], Shadow, 0, prgAssets: [placement]));
        Assert.Contains("initialize R7", exception.Message);
    }

    [Fact]
    public void ReachedNativeR7ExternUsesItsActualForegroundCallContext()
    {
        var program = Program();
        program.CreateBlock("main").Emit(LDA(7)).Emit(STA_abs(0x8000))
            .Emit(LDA(1)).Emit(STA_abs(0x8001)).Emit(JSR("_asset")).Emit(RTS());
        var asset = Program(0xb000);
        Native(asset, "_asset", RTS());
        var placement = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null);
        ManagedMapperSafety.Prepare([program, asset], [], ["_asset", "main"], [], Shadow, 0, prgAssets: [placement]);
    }

    [Fact]
    public void NativeSwitchableCodeWithoutPhysicalMetadataIsRejected()
    {
        var program = Program();
        Native(program, "_init", JSR(0xb000), RTS());
        var asset = Program(0xb000);
        Native(asset, "_asset", RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program, asset], [], ["_init"], [], Shadow, 0));
        Assert.Contains("physical PRG asset placement", exception.Message);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("__nesbank_method_fake")]
    public void RootNamesDoNotGrantManagedSoftwareStackProof(string name)
    {
        var program = Program();
        program.CreateBlock(name)
            .Emit(new Instruction(Opcode.STA, AddressMode.IndirectIndexed, new ImmediateOperand(NESConstants.sp))).Emit(RTS());
        Assert.Throws<TranspileException>(() => Prepare(program, foreground: [name]));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void CompilerGatePropagatesActualR7ContextToBankedCallee(byte bank, bool valid)
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(LDA(7)).Emit(STA_abs(0x8000))
            .Emit(LDA(bank)).Emit(STA_abs(0x8001)).Emit(JSR("gate")).Emit(RTS());
        var gate = program.CreateBlock("gate").Emit(JSR("banked")).Emit(RTS());
        var region = Program(0x8000);
        var body = region.CreateBlock("banked").Emit(JSR("_asset")).Emit(RTS());
        var asset = Program(0xb000);
        Native(asset, "_asset", RTS());
        var placement = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null);
        void Run() => ManagedMapperSafety.Prepare([program, region, asset], [], ["main"], ["banked"], Shadow, 0,
            managedBlocks: [main, body], compilerOwnedBlocks: [gate], prgAssets: [placement]);
        if (valid)
            Run();
        else
            Assert.Contains("requires physical bank 1", Assert.Throws<TranspileException>(Run).Message);
    }

    [Fact]
    public void MainOnlyRootFollowsManagedHelpersWithoutLosingR7Context()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(LDA(7)).Emit(STA_abs(0x8000))
            .Emit(LDA(1)).Emit(STA_abs(0x8001)).Emit(JSR("helper")).Emit(RTS());
        var helper = program.CreateBlock("helper").Emit(JSR("_asset")).Emit(RTS());
        var unused = program.CreateBlock("unused").Emit(JSR("_unsafe_unused")).Emit(RTS());
        Native(program, "_unsafe_unused", JSR(0x5555), RTI());
        var asset = Program(0xb000);
        Native(asset, "_asset", RTS());
        var placement = new CompiledPrgAsset(new() { Bank = 1, CpuAddress = 0xa000, Offset = 0x1000 }, asset, null);
        ManagedMapperSafety.Prepare([program, asset], [], ["main"], [], Shadow, 0,
            managedBlocks: [main, helper, unused], compilerOwnedBlocks: [], prgAssets: [placement]);
    }

    [Fact]
    public void BankedCodeCannotReenterActualCompilerGate()
    {
        var program = Program();
        var body = program.CreateBlock("banked").Emit(JSR("gate")).Emit(RTS());
        var gate = program.CreateBlock("gate").Emit(JSR("banked")).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], [], ["banked"], Shadow, 0,
                managedBlocks: [body], compilerOwnedBlocks: [gate]));
        Assert.Contains("cannot reenter", exception.Message);
    }

    [Fact]
    public void ManagedTailCallToCompilerHelperContinuesOriginalCallerAnalysis()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR("helper")).Emit(JSR("missing")).Emit(RTS());
        var helper = program.CreateBlock("helper").Emit(JMP("runtime"));
        var runtime = program.CreateBlock("runtime").Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0,
                managedBlocks: [main, helper], compilerOwnedBlocks: [runtime]));
        Assert.Contains("unresolved", exception.Message);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 0x800)]
    [InlineData(false, 0x1000)]
    [InlineData(false, 0x1800)]
    [InlineData(true, 0)]
    [InlineData(true, 0x800)]
    [InlineData(true, 0x1000)]
    [InlineData(true, 0x1800)]
    public void AuthoredSelectorContextWritesRejectBothBytesAndRamMirrors(bool banked, int mirror)
    {
        var program = Program();
        var store = STA_abs((ushort)(Shadow + (banked ? 1 : 0) + mirror));
        if (banked)
        {
            var body = program.CreateBlock("banked").Emit(LDA(0x40)).Emit(store).Emit(RTS());
            var exception = Assert.Throws<TranspileException>(() =>
                ManagedMapperSafety.Prepare([program], [], [], ["banked"], Shadow, 0, managedBlocks: [body]));
            Assert.Contains("compiler-owned selector context", exception.Message);
        }
        else
        {
            Native(program, "_callback", LDA(0x40), store, RTS());
            var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
            Assert.Contains("compiler-owned selector context", exception.Message);
        }
    }

    [Theory]
    [InlineData(AddressMode.AbsoluteX)]
    [InlineData(AddressMode.AbsoluteY)]
    public void IndexedStoreRangeCannotIntersectSelectorContext(AddressMode mode)
    {
        var program = Program();
        Native(program, "_callback",
            new Instruction(Opcode.STA, mode, new AbsoluteOperand(Shadow - 2)), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("compiler-owned selector context", exception.Message);
    }

    [Theory]
    [InlineData(AddressMode.IndexedIndirect, 0)]
    [InlineData(AddressMode.IndexedIndirect, 0x1000)]
    [InlineData(AddressMode.IndirectIndexed, 0)]
    [InlineData(AddressMode.IndirectIndexed, 0x1800)]
    public void ProvenIndirectRamPointerCannotOverwriteSelectorContext(AddressMode mode, int mirror)
    {
        var program = Program();
        int pointer = Shadow + mirror - (mode == AddressMode.IndirectIndexed ? 1 : 0);
        Native(program, "_callback", LDA((byte)pointer), STA_zpg(0x10), LDA((byte)(pointer >> 8)), STA_zpg(0x11),
            LDX(0), new Instruction(Opcode.STA, mode, new ImmediateOperand(0x10)), RTS());
        var exception = Assert.Throws<TranspileException>(() => Prepare(program, ["_callback"]));
        Assert.Contains("compiler-owned selector context", exception.Message);
    }

    [Fact]
    public void ReadModifyWriteCannotCorruptSavedSelectorContext()
    {
        var program = Program();
        var body = program.CreateBlock("banked")
            .Emit(new Instruction(Opcode.INC, AddressMode.Absolute, new AbsoluteOperand(Shadow + 1))).Emit(RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], [], ["banked"], Shadow, 0, managedBlocks: [body]));
        Assert.Contains("compiler-owned selector context", exception.Message);
    }

    [Fact]
    public void OrdinarySourceIsNotExemptedByDeclaredRamCodeContract()
    {
        var program = Program();
        Native(program, "_foreground", JSR(0x7420), LDA(0x40), STA_abs(Shadow), RTS());
        var exception = Assert.Throws<TranspileException>(() =>
            ManagedMapperSafety.Prepare([program], [], ["_foreground"], [], Shadow, 0, [RamCode()]));
        Assert.Contains("compiler-owned selector context", exception.Message);
    }

    [Fact]
    public void ProvenCompilerAccessAndNewPublicationCanWriteSelectorContext()
    {
        var program = Program();
        var main = program.CreateBlock("main").Emit(JSR("runtime")).Emit(LDA(0)).Emit(STA_abs(0x8000)).Emit(RTS());
        var runtime = program.CreateBlock("runtime").Emit(LDA(6)).Emit(STA_abs(Shadow)).Emit(STA_abs(Shadow + 1)).Emit(RTS());
        ManagedMapperSafety.Prepare([program], [], ["main"], [], Shadow, 0, managedBlocks: [main], compilerOwnedBlocks: [runtime]);
        Assert.Equal(Shadow, Assert.IsType<AbsoluteOperand>(main[2].Operand).Address);
    }

    [Fact]
    public void RamStoreAdjacentToSelectorContextRemainsAllowed()
    {
        var program = Program();
        Native(program, "_callback", STA_abs(Shadow - 1),
            new Instruction(Opcode.STA, AddressMode.AbsoluteX, new AbsoluteOperand(Shadow + 2)), RTS());
        Prepare(program, ["_callback"]);
    }
}
