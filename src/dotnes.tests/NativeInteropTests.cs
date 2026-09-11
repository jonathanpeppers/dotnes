using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NativeInteropTests : RoslynTests
{
    public NativeInteropTests(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("bool")]
    public void ScalarByReferenceArgumentsCannotSilentlyOmitTheirAddress(string type)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            {{type}} value = default;
            Fill(ref value);
            while (true);
            static extern void Fill(ref {{type}} value);
            """));
        Assert.Contains("address of scalar local", error.Message);
        Assert.Contains("by-reference", error.Message);
        Assert.Contains("byte/sbyte value parameters", error.Message);
    }

    [Theory]
    [InlineData(PrimitiveTypeCode.Byte)]
    [InlineData(PrimitiveTypeCode.SByte)]
    [InlineData(PrimitiveTypeCode.Int16)]
    [InlineData(PrimitiveTypeCode.UInt16)]
    [InlineData(PrimitiveTypeCode.Boolean)]
    public void HighIndexScalarAddressUsesTheSameActionableDiagnostic(PrimitiveTypeCode type)
    {
        foreach (var (opcode, index) in new[] { (ILOpCode.Ldloca_s, 0), (ILOpCode.Ldloca, 256) })
        {
            using var stream = new MemoryStream();
            using var writer = new IL2NESWriter(stream);
            var locals = new PrimitiveTypeCode?[index + 1];
            locals[index] = type;
            writer.ConfigureNumericTypes(new Dictionary<string, MethodNumericTypes>
            {
                ["main"] = new([.. locals], [], PrimitiveTypeCode.Void),
            });
            var error = Assert.Throws<TranspileException>(() =>
                writer.Write(new ILInstruction(opcode, 0, index), index));
            Assert.Contains($"address of scalar local {index}", error.Message);
            Assert.Contains("by-reference", error.Message);
            Assert.Contains("byte/sbyte value parameters", error.Message);
            Assert.Empty(stream.ToArray());
        }
    }

    [Theory]
    [InlineData("short", -300)]
    [InlineData("ushort", 65535)]
    public void SupportedExternWordReturnsRetainBothBytes(string type, int value)
    {
        WithNativeProgram($$"""
            {{type}} result = Get();
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern {{type}} Get();
            static extern void test_stop();
            """, $"_Get:\nlda #${unchecked((byte)value):X2}\nldx #${unchecked((byte)(value >> 8)):X2}\nrts",
            (program, _) =>
            {
                const ushort stop = 0x7FF0;
                program.DefineExternalLabel("_test_stop", stop);
                var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, program.GetLabels()["main"]);
                cpu.RunUntil(stop);
                Assert.Equal(unchecked((byte)value), cpu.Memory[0x6000]);
                Assert.Equal(unchecked((byte)(value >> 8)), cpu.Memory[0x6001]);
                Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
                Assert.Equal(0xFD, cpu.SP);
            });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExternCallsUseTheSameSymbolInMainAndHelpers(bool staticClass, bool legacyName)
    {
        string source = staticClass
            ? """
              Native.Set(1);
              Helper();
              ppu_wait_nmi();
              while (true) ;
              static void Helper() => Native.Set(2);
              static class Native { public static extern void Set(byte value); }
              """
            : """
              Set(1);
              Helper();
              ppu_wait_nmi();
              while (true) ;
              static extern void Set(byte value);
              static void Helper() => Set(2);
              """;
        WithNativeProgram(source, $"{(legacyName ? "Set" : "_Set")}:\nsta $6000\nrts", (program, _) =>
        {
            ushort target = program.GetLabels()["_Set"];
            foreach (string method in new[] { "main", "Helper" })
            {
                Assert.Single(program.GetBlock(method)!.InstructionsWithLabels,
                    entry => entry.Instruction.Opcode == Opcode.JSR &&
                        entry.Instruction.Operand == new LabelOperand("_Set", OperandSize.Word));
                Assert.Contains($"20{target & 0xFF:X2}{target >> 8:X2}", Convert.ToHexString(program.GetMainBlock(method)));
            }
            program.ToBytes();
            Assert.DoesNotContain("Set", program.Labels.UnresolvedReferences);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCallbacksRetainDispatchersWithoutManagedStubs(bool inHelper)
    {
        string setup = """
            poke(PPU_CTRL, 0);
            sei();
            unsafe
            {
                nmi_set_callback(&Native.Nmi);
                irq_set_callback(&Native.Irq);
            }
            """;
        string source = inHelper
            ? $"Setup.Configure(); while (true) ; static class Setup {{ public static void Configure() {{ {setup} }} }} "
            : $"{setup} while (true) ; ";
        source += """
            static class Native
            {
                public static extern void Nmi();
                public static extern void Irq();
            }
            """;
        WithNativeProgram(source, "_Nmi:\ninc $6000\nrts\n_Irq:\ninc $6001\nrts", (program, transpiler) =>
        {
            Assert.DoesNotContain(transpiler.UserMethods.Keys, name => name is "Nmi" or "Irq");
            Assert.NotNull(program.GetBlock("irq_with_callback"));
            var labels = program.GetLabels();
            string hex = Convert.ToHexString(program.GetMainBlock(inHelper ? "Configure" : "main"));
            foreach (string callback in new[] { "_Nmi", "_Irq" })
            {
                ushort address = labels[callback];
                Assert.Contains($"A9{address & 0xFF:X2}A2{address >> 8:X2}", hex);
            }

            using var rom = new MemoryStream();
            transpiler.Write(rom);
            byte[] bytes = rom.ToArray();
            const int vectors = 16 + 32768 - 6;
            Assert.Equal(labels["_nmi"], (ushort)(bytes[vectors] | bytes[vectors + 1] << 8));
            Assert.Equal(labels["irq_with_callback"], (ushort)(bytes[vectors + 4] | bytes[vectors + 5] << 8));
        });
    }

    [Fact]
    public void MatchingCanonicalAndLegacyExportsAreAccepted()
    {
        WithNativeProgram(
            "Set(); ppu_wait_nmi(); while (true) ; static extern void Set();",
            "_Set:\nrts\nSet = _Set",
            (program, _) =>
            {
                Assert.Equal(program.GetLabels()["Set"], program.GetLabels()["_Set"]);
                program.ToBytes();
            });
    }

    [Fact]
    public void ConflictingCanonicalAndLegacyExportsAreRejected()
    {
        var exception = Assert.Throws<TranspileException>(() => WithNativeProgram(
            "Set(); ppu_wait_nmi(); while (true) ; static extern void Set();",
            "_Set:\nrts\nSet:\nnop\nrts",
            (_, _) => Assert.Fail("Conflicting symbols must not compile.")));
        Assert.Contains("Conflicting native symbols '_Set' and 'Set'", exception.Message);
    }

    [Fact]
    public void LegacyBindingFollowsAddressResolutionAndCanBeRebound()
    {
        using var transpiler = BuildProgram(
            "Set(); ppu_wait_nmi(); while (true) ; static extern void Set();", out var program);
        program.DefineExternalLabel("Set", 0x7000);
        Assert.Contains("200070", Convert.ToHexString(program.GetMainBlock()));
        program.DefineExternalLabel("Set", 0x7010);
        Assert.Contains("201070", Convert.ToHexString(program.GetMainBlock()));
        program.DefineExternalLabel("_Set", 0x7020);
        Assert.Throws<TranspileException>(() => program.ToBytes());
    }

    [Fact]
    public void LegacyNativeLabelFollowsRelocation()
    {
        WithNativeProgram("Set(); ppu_wait_nmi(); while (true) ; static extern void Set();",
            "Set:\nrts", (program, _) =>
            {
                ushort original = program.GetLabels()["_Set"];
                program.BaseAddress = 0xC000;
                program.ResolveAddresses();
                Assert.Equal(original + 0x4000, program.GetLabels()["_Set"]);
                Assert.Equal(program.GetLabels()["Set"], program.GetLabels()["_Set"]);
                program.ToBytes();
            });
    }

    [Theory]
    [InlineData("scroll")]
    [InlineData("popa")]
    public void BuiltInsAndForwardReferencesCannotSupplyMissingNativeCallbacks(string name)
    {
        using var transpiler = BuildProgram(
            $$"""
            unsafe { nmi_set_callback(&Native.{{name}}); }
            while (true) ;
            static class Native { public static extern void {{name}}(); }
            """, out var program, allowUnsafe: true);

        Assert.NotNull(program.GetBlock(name));
        Assert.False(program.Labels.IsDefined($"_{name}"));
        var exception = Assert.Throws<UnresolvedLabelException>(() => program.ToBytes());
        Assert.Equal($"_{name}", exception.Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeExportMayShareABuiltInName(bool legacyName)
    {
        WithNativeProgram(
            """
            unsafe { nmi_set_callback(&Native.scroll); }
            while (true) ;
            static class Native { public static extern void scroll(); }
            """,
            $"{(legacyName ? "scroll" : "_scroll")}:\ninc $6000\nrts",
            (program, _) =>
            {
                var nativeBlock = program.Blocks.Last(block => block.Label == (legacyName ? "scroll" : "_scroll"));
                ushort nativeAddress = (ushort)(program.BaseAddress +
                    program.Blocks.TakeWhile(block => block != nativeBlock).Sum(block => block.Size));
                Assert.Equal(nativeAddress, program.GetLabels()["_scroll"]);
                Assert.Contains($"A9{nativeAddress & 0xFF:X2}A2{nativeAddress >> 8:X2}",
                    Convert.ToHexString(program.GetMainBlock()));
                program.ToBytes();
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitBindingMayShareABuiltInName(bool legacyName)
    {
        using var transpiler = BuildProgram(
            """
            unsafe { nmi_set_callback(&Native.scroll); }
            while (true) ;
            static class Native { public static extern void scroll(); }
            """, out var program, allowUnsafe: true);

        program.DefineExternalLabel(legacyName ? "scroll" : "_scroll", 0x7000);
        Assert.Contains("A900A270", Convert.ToHexString(program.GetMainBlock()));
        Assert.Equal((ushort)0x7000, program.GetLabels()["_scroll"]);
        Assert.NotEqual((ushort)0x7000, program.GetLabels()["scroll"]);
        program.ToBytes();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedLabelsCannotSupplyLegacyBindingsOrConflicts(bool bindCanonical)
    {
        var program = new Program6502();
        program.CreateBlock("Helper").Emit(new Instruction(Opcode.RTS, AddressMode.Implied));
        program.RegisterExternSymbol("Helper");
        if (bindCanonical)
            program.DefineExternalLabel("_Helper", 0x7000);
        program.ResolveAddresses();
        Assert.Equal(bindCanonical, program.Labels.IsDefined("_Helper"));
        if (bindCanonical)
            Assert.Equal((ushort)0x7000, program.GetLabels()["_Helper"]);
    }

    [Theory]
    [InlineData("nmi_set_callback")]
    [InlineData("irq_set_callback")]
    public void NullCallbackIsRejected(string setter)
    {
        Assert.Throws<TranspileException>(() =>
            GetProgramBytes($"unsafe {{ {setter}(null); }} while (true) ;", null, allowUnsafe: true));
    }

    [Theory]
    [InlineData("nmi_set_callback", false)]
    [InlineData("nmi_set_callback", true)]
    [InlineData("irq_set_callback", false)]
    [InlineData("irq_set_callback", true)]
    public void ConditionalCallbackAddressesAreRejected(string setter, bool inHelper)
    {
        string setup = $"unsafe {{ {setter}(peek(0x6000) == 0 ? &First : &Second); }}";
        string source = inHelper
            ? $"Configure(); while (true) ; static void Configure() {{ {setup} }} "
            : $"{setup} while (true) ; ";
        source += "static extern void First(); static extern void Second();";
        var exception = Assert.Throws<TranspileException>(() => WithNativeProgram(
            source, "_First:\nrts\n_Second:\nrts", (_, _) => Assert.Fail("A merged pointer must not compile.")));
        Assert.Contains("requires a direct function address", exception.Message);
    }

    [Theory]
    [InlineData("nmi_set_callback")]
    [InlineData("irq_set_callback")]
    public void ConditionalDirectSetterCallsRemainSupported(string setter)
    {
        WithNativeProgram(
            $$"""
            unsafe
            {
                if (peek(0x6000) == 0)
                    {{setter}}(&First);
                else
                    {{setter}}(&Second);
            }
            while (true) ;
            static extern void First();
            static extern void Second();
            """,
            "_First:\nrts\n_Second:\nrts",
            (program, _) => program.ToBytes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRendererRejectsOamScope(bool inHelper)
    {
        string scope = "using (var oam = new OamScope()) { oam.spr(1, 2, 3, 0); }";
        string source = inHelper
            ? $"ppu_use_native_renderer(); Draw(); while (true) ; static void Draw() {{ {scope} }}"
            : $"ppu_use_native_renderer(); {scope} while (true) ;";
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(source));
        Assert.Contains("'OamScope' uses the stock renderer", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRendererIsWholeProgramAndBypassesStockGraphics(bool inHelper)
    {
        string source = inHelper
            ? "Configure(); ppu_wait_nmi(); while (true) ; static void Configure() => ppu_use_native_renderer();"
            : "ppu_use_native_renderer(); ppu_wait_nmi(); while (true) ;";
        using var transpiler = BuildProgram(source, out var program);
        var nmi = program.GetBlock("_nmi")!;
        Assert.Equal(new[] { Opcode.PHA, Opcode.TXA, Opcode.PHA, Opcode.TYA, Opcode.PHA, Opcode.JMP },
            nmi.InstructionsWithLabels.Select(entry => entry.Instruction.Opcode));
        Assert.Equal(new LabelOperand("skipAll", OperandSize.Word), nmi[5].Operand);
        Assert.Equal("E601E602A502C906D004A9008502", Convert.ToHexString(program.GetMainBlock("skipAll")));
        Assert.Equal("20140068A868AA6840", Convert.ToHexString(program.GetMainBlock("skipNtsc")));
        Assert.Equal(new byte[] { 0x40 }, program.GetMainBlock("_irq"));
        Assert.DoesNotContain(program.Blocks.SelectMany(block => block.InstructionsWithLabels),
            entry => entry.Instruction.Operand == new LabelOperand("ppu_use_native_renderer", OperandSize.Word));
        _ = program.ToBytes();
    }

    [Theory]
    [InlineData("ppu_on_all();", "ppu_on_all")]
    [InlineData("ppu_mask(0);", "ppu_mask")]
    [InlineData("oam_clear();", "oam_clear")]
    [InlineData("pal_col(0, 1);", "pal_col")]
    [InlineData("scroll(0, 0);", "scroll")]
    [InlineData("set_scroll_x(1);", "set_scroll_x")]
    [InlineData("set_vram_update((ushort)0);", "set_vram_update")]
    [InlineData("one_vram_buffer(1, 0x2000);", "one_vram_buffer")]
    [InlineData("vram_inc(VramIncrement.By32);", "vram_inc")]
    [InlineData("set_ppu_ctrl_var(0);", "set_ppu_ctrl_var")]
    public void NativeRendererRejectsStockGraphicsInHelpers(string call, string method)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $"ppu_use_native_renderer(); Helper(); while (true) ; static void Helper() {{ {call} }}"));
        Assert.Contains($"'{method}' uses the stock renderer", exception.Message);
    }

    [Fact]
    public void NativeRendererAllowsImmediateTransfersAndFrameHelpers()
    {
        using var transpiler = BuildProgram(
            """
            ppu_use_native_renderer();
            poke(PPU_MASK, 0);
            vram_adr(0x2000);
            vram_put(1);
            vram_fill(2, 16);
            vram_write(new byte[] { 3, 4 });
            vram_write("OamScope.status");
            ppu_wait_nmi();
            ppu_wait_frame();
            delay(1);
            poke(0x6000, nesclock());
            poke(0x6001, get_frame_count());
            while (true) ;
            """, out var program);
        _ = program.ToBytes();
        foreach (string name in new[] { "vram_adr", "vram_put", "vram_fill", "vram_write", "ppu_wait_nmi", "ppu_wait_frame", "delay", "nesclock" })
            Assert.NotNull(program.GetBlock(name));
    }

    [Fact]
    public void NativeRendererExecutesOnlyCallbacksAndCountersDuringInterrupts()
    {
        WithNativeProgram(
            """
            ppu_use_native_renderer();
            poke(PPU_CTRL, 0);
            sei();
            unsafe
            {
                nmi_set_callback(&Nmi);
                irq_set_callback(&Irq);
            }
            poke(PPU_CTRL, 0x80);
            cli();
            PrepareRegisters();
            test_stop();
            while (true) ;
            static extern void Nmi();
            static extern void Irq();
            static extern void PrepareRegisters();
            static extern void test_stop();
            """,
            """
            _PrepareRegisters:
                lda #$7F
                clc
                adc #$01
                ldx #$53
                ldy #$27
                sec
                rts
            _Nmi:
                inc $6000
                lda #$12
                ldx #$34
                ldy #$56
                clc
                clv
                rts
            _Irq:
                inc $6001
                lda #$00
                sta $E000
                ldx #$78
                ldy #$9A
                clc
                clv
                rts
            """,
            (program, _) =>
            {
                const ushort stop = 0x7FF0;
                program.DefineExternalLabel("_test_stop", stop);
                var labels = program.GetLabels();
                var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, labels["main"]);
                // Model the reset-installed JMP opcode; the real setter fills its address.
                cpu.Memory[NESConstants.NMI_CALLBACK] = 0x4C;
                cpu.RunUntil(stop);
                cpu.Memory[0xFFFA] = (byte)labels["_nmi"];
                cpu.Memory[0xFFFB] = (byte)(labels["_nmi"] >> 8);
                cpu.Memory[0xFFFE] = (byte)labels["irq_with_callback"];
                cpu.Memory[0xFFFF] = (byte)(labels["irq_with_callback"] >> 8);
                cpu.Memory[NESConstants.STARTUP] = 0xFF;
                cpu.Memory[NESConstants.NES_PRG_BANKS] = 5;
                cpu.Memory[NESConstants.PPU_MASK_VAR] = 0x1E;
                cpu.Memory[NESConstants.PAL_UPDATE] = 1;
                cpu.Memory[NESConstants.VRAM_UPDATE] = 1;
                var registers = (cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer);

                cpu.WrittenAddresses.Clear();
                cpu.Nmi();
                cpu.RunUntil(stop);
                Assert.Equal(registers, (cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer));
                Assert.Equal(1, cpu.Memory[0x6000]);
                Assert.Equal(0, cpu.Memory[NESConstants.STARTUP]);
                Assert.Equal(0, cpu.Memory[NESConstants.NES_PRG_BANKS]);
                Assert.All(cpu.WrittenAddresses, address =>
                    Assert.True(address is >= 0x100 and <= 0x1FF or 0x01 or 0x02 or 0x6000,
                        $"Unexpected NMI write to ${address:X4}."));

                cpu.WrittenAddresses.Clear();
                Assert.True(cpu.Irq());
                cpu.RunUntil(stop);
                Assert.Equal(registers, (cpu.A, cpu.X, cpu.Y, cpu.SP, cpu.Status, cpu.SoftwareStackPointer));
                Assert.Equal(1, cpu.Memory[0x6001]);
                Assert.Equal(1, cpu.Memory[0x6000]);
                Assert.Equal(0, cpu.Memory[NESConstants.STARTUP]);
                Assert.Equal(0, cpu.Memory[NESConstants.NES_PRG_BANKS]);
                Assert.Contains((ushort)0xE000, cpu.WrittenAddresses);
                Assert.All(cpu.WrittenAddresses, address =>
                    Assert.True(address is >= 0x100 and <= 0x1FF or 0x6001 or 0xE000,
                        $"Unexpected IRQ write to ${address:X4}."));
            });
    }

    void WithNativeProgram(string source, string assembly, Action<Program6502, Transpiler> assertion)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dotnes-native-{Guid.NewGuid():N}.s");
        try
        {
            File.WriteAllText(path, assembly);
            using var reader = new AssemblyReader(path);
            using var transpiler = BuildProgram(source, out var program, [reader], allowUnsafe: true);
            assertion(program, transpiler);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
