using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ManagedCodeBankTests(ITestOutputHelper output) : RoslynTests(output)
{
    const string Simple = """
        poke(0x6000, Audio.Tick(7));
        while (true) ;
        [NESCodeBank("audio")]
        static class Audio
        {
            public static byte Tick(byte input) => (byte)(input + 1);
        }
        """;

    static CompilationOptions Options() => new()
    {
        Mapper = 4,
        PrgBanks = 3,
        Mmc3BankedLayout = true,
        Mmc3ManagedHomeBank = 0,
        ManagedCodeBanks = { new() { Name = "audio", Bank = 2, Size = 0x1000 } },
    };

    BankedCompilation Compile(string source, CompilationOptions? options = null, string? native = null)
    {
        var assembly = CompileAssembly(source, allowUnsafe: true);
        using var reader = native == null ? null : new AssemblyReader(new StringReader(native));
        return NesCompiler.CompileBanked(assembly, options ?? Options(), reader == null ? [] : [reader], _logger);
    }

    [Fact]
    public void MetadataPlacesMethodsAndCallsFixedGates()
    {
        var result = Compile(Simple);
        var region = Assert.Single(result.Regions);
        Assert.Equal("audio", region.Placement.Name);
        Assert.Equal(0x8000, region.Program.BaseAddress);
        Assert.Equal(0xC000, result.FixedProgram.BaseAddress);
        string method = Assert.Single(region.Program.Blocks).Label!;
        Assert.EndsWith("_Tick", method);
        Assert.Null(result.FixedProgram.GetBlock(method));
        Assert.Contains(result.FixedProgram.Blocks, b => b.Label!.StartsWith("__nesbank_gate_", StringComparison.Ordinal));
        Assert.Equal(region.Program.GetLabels()[method], result.FixedProgram.GetLabels()[method]);
        Assert.Equal(result.FixedProgram.ToBytes(), result.FixedProgram.ToBytes());
        Assert.Equal(region.Program.ToBytes(), region.Program.ToBytes());
    }

    [Fact]
    public void FlatCompileDoesNotSilentlyIgnoreBanking()
    {
        using var assembly = CompileAssembly(Simple);
        Assert.Contains("no NESManagedCodeBank", Assert.Throws<TranspileException>(() =>
            NesCompiler.Compile(assembly)).Message);
        assembly.Position = 0;
        Assert.Contains("CompileBanked", Assert.Throws<TranspileException>(() =>
            NesCompiler.Compile(assembly, Options())).Message);
    }

    [Theory]
    [InlineData("static byte value = 1;")]
    [InlineData("static Audio() { }")]
    [InlineData("static byte[] values = new byte[4];")]
    public void RejectsImplicitStaticInitialization(string declaration)
    {
        string source = $$"""
            Audio.Tick(); while (true) ;
            [NESCodeBank("audio")]
            static class Audio { {{declaration}} public static void Tick() { poke(0x6000, 1); } }
            """;
        Assert.Contains("static initialization", Assert.Throws<TranspileException>(() => Compile(source)).Message);
    }

    [Theory]
    [InlineData("public static byte Tick(ushort value) => (byte)value;", "Audio.Tick(1)")]
    [InlineData("public static byte Tick(ref byte value) => value;", "Audio.Tick(ref value)")]
    [InlineData("public static byte Tick(byte[] value) => value[0];", "Audio.Tick(new byte[] { 1 })")]
    [InlineData("public static int Tick(byte value) => value;", "Audio.Tick(1)")]
    public void RejectsUnsupportedGateSignatures(string declaration, string call)
    {
        string source = $$"""
            byte value = 1; poke(0x6000, (byte){{call}}); while (true) ;
            [NESCodeBank("audio")] static class Audio { {{declaration}} }
            """;
        Assert.Contains("arguments", Assert.Throws<TranspileException>(() => Compile(source)).Message);
    }

    [Fact]
    public void UsesMetadataIdentityForSameNamedMethods()
    {
        var result = Compile("""
            poke(0x6000, Fixed.Tick(3));
            poke(0x6001, Audio.Tick(4));
            while (true) ;
            static class Fixed { public static byte Tick(byte value) => (byte)(value + 1); }
            [NESCodeBank("audio")]
            static class Audio { public static byte Tick(byte value) => (byte)(value + 2); }
            """);
        Assert.NotNull(result.FixedProgram.GetBlock("Tick"));
        Assert.EndsWith("_Tick", Assert.Single(result.Regions[0].Program.Blocks).Label);
    }

    [Fact]
    public void SupportsMethodAnnotationOnStaticLocalFunction()
    {
        var result = Compile("""
            poke(0x6000, Compute(1)); while (true) ;
            [NESCodeBank("audio")]
            static byte Compute(byte value) => (byte)(value ^ 0xAA);
            """);
        Assert.Single(result.Regions[0].Program.Blocks);
    }

    [Fact]
    public void RejectsCallsIntoDifferentRegion()
    {
        var options = Options();
        options.ManagedCodeBanks.Add(new() { Name = "other", Bank = 3, Size = 0x1000 });
        Assert.Contains("another region", Assert.Throws<TranspileException>(() => Compile("""
            Audio.Tick(); while (true) ;
            [NESCodeBank("audio")] static class Audio { public static void Tick() { Other.Tick(); } }
            [NESCodeBank("other")] static class Other { public static void Tick() { poke(0x6000, 1); } }
            """, options)).Message);
    }

    [Fact]
    public void RejectsZeroLocalRecursiveCycle()
    {
        Assert.Contains("Recursive", Assert.Throws<TranspileException>(() => Compile("""
            poke(0x6000, 0); Audio.Tick(); while (true) ;
            [NESCodeBank("audio")] static class Audio
            {
                public static void Tick() { Next(); }
                static void Next() { Tick(); }
            }
            """)).Message);
    }

    [Fact]
    public void RejectsMissingInterruptContract()
    {
        Assert.Contains("NonNestingChrCallbacks", Assert.Throws<TranspileException>(() => Compile("""
            unsafe { nmi_set_callback(&Nmi); }
            Audio.Tick(); while (true) ;
            static extern void Nmi();
            [NESCodeBank("audio")] static class Audio { public static void Tick() { poke(0x6000, 1); } }
            """, native: "_Nmi:\nrts")).Message);
    }

    [Fact]
    public void StartupDataReferencesFollowFinalPlacement()
    {
        var result = Compile(Simple);
        var program = result.FixedProgram;
        ushort table = program.GetLabels()["__DESTRUCTOR_TABLE__"];
        byte[] copy = program.GetMainBlock("copydata");
        Assert.Equal((byte)table, copy[1]);
        Assert.Equal((byte)(table >> 8), copy[5]);
        byte[] done = program.GetMainBlock("donelib");
        Assert.Equal((byte)table, done[5]);
        Assert.Equal((byte)(table >> 8), done[7]);
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, 0x6000);
        ushort copyAddress = program.GetLabels()["copydata"];
        cpu.Memory[0x6000] = 0x20;
        cpu.Memory[0x6001] = (byte)copyAddress;
        cpu.Memory[0x6002] = (byte)(copyAddress >> 8);
        cpu.RunUntil(0x6003);
        Assert.Equal(program.GetMainBlock("__DESTRUCTOR_TABLE__"), cpu.Memory[0x0300..0x0325]);
    }

    [Fact]
    public void RegionAcceptsExactSizeAndRejectsOneByteOverflow()
    {
        int size = Compile(Simple).Regions[0].Program.TotalSize;
        var options = Options();
        options.ManagedCodeBanks[0].Size = size;
        Assert.Equal(size, Compile(Simple, options).Regions[0].Program.TotalSize);
        options.ManagedCodeBanks[0].Size--;
        Assert.Contains("exceed", Assert.Throws<InvalidOperationException>(() => Compile(Simple, options)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicRamLabelsIdentifyActualMainArraysAfterMapperContext()
    {
        var result = Compile("""
            byte[] first = new byte[4];
            byte[] second = new byte[3];
            first[0] = peek(0x6200);
            second[0] = first[0];
            Audio.Tick(second[0]);
            while (true) ;
            [NESCodeBank("audio")]
            static class Audio
            {
                static byte count;
                static ushort word;
                public static void Tick(byte input) { count = input; word = input; poke(0x6000, count); }
            }
            """);
        var labels = result.FixedProgram.GetLabels();
        Assert.Equal(0x0328, labels["__nesbank_selector"]);
        Assert.Equal(0x0329, labels["__nesbank_saved_selector"]);
        Assert.Equal(0x032A, labels["__nesbank_main_locals"]);
        Assert.Equal(0x032A, labels["__nesbank_main_first_array"]);
        Assert.Equal(0x032A, labels["__nesbank_main_array_0"]);
        Assert.Equal(4, labels["__nesbank_main_array_0_size"]);
        Assert.Equal(0x032E, labels["__nesbank_main_array_1"]);
        Assert.Equal(3, labels["__nesbank_main_array_1_size"]);
    }

    [Fact]
    public void StockRomWriterPreservesPrgAssetsChrAndVectors()
    {
        string prgPath = Path.Combine(Path.GetTempPath(), $"dotnes-managed-prg-{Guid.NewGuid():N}.bin");
        string chrPath = Path.Combine(Path.GetTempPath(), $"dotnes-managed-chr-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(prgPath, [0x12, 0x34, 0x56]);
            File.WriteAllBytes(chrPath, [0xAB, 0xCD, 0xEF]);
            using var assembly = CompileAssembly(Simple);
            using var chr = new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")));
            byte[] legacyChr = chr.GetSegments().Where(segment => segment.Name == "CHARS").SelectMany(segment => segment.Bytes).ToArray();
            using var transpiler = new Transpiler(assembly, [chr], _logger,
                mapper: 4, prgBanks: 3, chrBanks: 2, mmc3BankedLayout: true,
                prgBankAssets: [new(prgPath, 2, 0x1000, 0x8000)],
                chrBankAssets: [new(chrPath, 8, 0)],
                managedCodeBanks: Options().ManagedCodeBanks.ToArray(), mmc3ManagedHomeBank: 0);
            using var outputRom = new MemoryStream();
            transpiler.Write(outputRom);
            byte[] rom = outputRom.ToArray();
            int chrStart = 16 + 3 * 16384;
            Assert.Equal(chrStart + 2 * 8192, rom.Length);
            Assert.Equal(new byte[] { 0x4E, 0x45, 0x53, 0x1A, 3, 2, 0x40, 0 }, rom[..8]);
            Assert.Equal(new byte[] { 0x12, 0x34, 0x56 }, rom[(16 + 2 * 8192 + 0x1000)..(16 + 2 * 8192 + 0x1003)]);
            Assert.Equal(legacyChr, rom[chrStart..(chrStart + legacyChr.Length)]);
            Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, rom[(chrStart + 8192)..(chrStart + 8195)]);
            Assert.Equal(new byte[] { 0xA9, 0, 0x8D, 0, 0x80, 0x4C, 0, 0xC0 },
                rom[(chrStart - 14)..(chrStart - 6)]);
            Assert.Equal(new byte[] { 0xF2, 0xFF }, rom[(chrStart - 4)..(chrStart - 2)]);
            var compilation = Compile(Simple);
            byte[] code = compilation.Regions[0].Program.ToBytes();
            Assert.Equal(code, rom[(16 + 2 * 8192)..(16 + 2 * 8192 + code.Length)]);
            byte[] fixedCode = compilation.FixedProgram.ToBytes();
            Assert.Equal(fixedCode, rom[(16 + 4 * 8192)..(16 + 4 * 8192 + fixedCode.Length)]);
        }
        finally
        {
            File.Delete(prgPath);
            File.Delete(chrPath);
        }
    }

    [Fact]
    public void ReservationCannotOverlapAssetsEvenAfterEmittedCodeEnds()
    {
        string asset = Path.Combine(Path.GetTempPath(), $"dotnes-managed-overlap-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(asset, [0xFF]);
            using var assembly = CompileAssembly(Simple);
            using var chr = new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")));
            using var transpiler = new Transpiler(assembly, [chr], _logger,
                mapper: 4, prgBanks: 3, mmc3BankedLayout: true,
                prgBankAssets: [new(asset, 2, 0x0FFF, 0x8000)],
                managedCodeBanks: Options().ManagedCodeBanks.ToArray(), mmc3ManagedHomeBank: 0);
            Assert.Contains("overlap", Assert.Throws<InvalidOperationException>(() => transpiler.Write(new MemoryStream())).Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(asset);
        }
    }

    [Fact]
    public void PublicCompilationFollowsPermanentNativeAssetFromInitializedR7()
    {
        string asset = Path.Combine(Path.GetTempPath(), $"dotnes-managed-native-{Guid.NewGuid():N}.s");
        try
        {
            File.WriteAllText(asset, """
                .segment "CODE"
                _Native:
                    lda #$5D
                    sta $6002
                    rts
                unused_unsafe_code:
                    lda #$46
                    sta $8000
                    rts
                """);
            var options = Options();
            options.PrgBankAssets.Add(new() { Path = asset, Bank = 1, CpuAddress = 0xA000, Offset = 0x1000 });
            var result = Compile("""
                poke(MMC3_BANK_SELECT, 7);
                poke(MMC3_BANK_DATA, 1);
                Native();
                poke(0x6000, Audio.Tick(7));
                while (true) ;
                static extern void Native();
                [NESCodeBank("audio")]
                static class Audio { public static byte Tick(byte input) => (byte)(input + 1); }
                """, options);
            var compiledAsset = Assert.Single(result.PrgAssets);
            Assert.Equal(asset, compiledAsset.Placement.Path);
            Assert.Equal(1, compiledAsset.Placement.Bank);
            Assert.Equal(0xA000, compiledAsset.Placement.CpuAddress);
            Assert.Equal(0x1000, compiledAsset.Placement.Offset);
            var nativeProgram = Assert.IsType<Program6502>(compiledAsset.Program);
            Assert.Equal(0xB000, nativeProgram.BaseAddress);
            File.Delete(asset);
            result.ResolveAndRelax();
            Assert.Equal(new byte[] { 0xA9, 0x5D, 0x8D, 2, 0x60, 0x60, 0xA9, 0x46, 0x8D, 0, 0x80, 0x60 },
                nativeProgram.ToBytes());
            Assert.Equal(0xB000, result.FixedProgram.GetLabels()["_Native"]);
            Assert.Contains("20 00 B0", BitConverter.ToString(result.FixedProgram.GetMainBlock()).Replace('-', ' '));
        }
        finally
        {
            File.Delete(asset);
        }
    }

    [Fact]
    public void FlatCompilationRejectsPrgAssetsInsteadOfDroppingThem()
    {
        var options = Options();
        options.PrgBankAssets.Add(new() { Path = "unused.s", Bank = 1, CpuAddress = 0xA000 });
        using var assembly = CompileAssembly(Simple);
        Assert.Contains("CompileBanked", Assert.Throws<ArgumentException>(() =>
            NesCompiler.Compile(assembly, options)).Message);
    }

    [Fact]
    public void FixedNativeBlocksRetainPreparedCodeAndDataWithoutStartup()
    {
        var result = Compile("""
            Native(); Audio.Tick(7); while (true) ;
            static extern void Native();
            [NESCodeBank("audio")]
            static class Audio { public static byte Tick(byte input) => (byte)(input + 1); }
            """, native: """
                .segment "CODE"
                _Native:
                    lda #3
                    sta $8000
                    lda #$12
                    sta $8001
                    rts
                native_data:
                    .byte $A4, $A5
                """);
        var native = result.FixedNativeBlocks;
        Assert.NotEmpty(native);
        Assert.All(native, block => Assert.Contains(result.FixedProgram.Blocks, candidate => ReferenceEquals(candidate, block)));
        Assert.Equal(result.FixedProgram.Blocks.Where(native.Contains), native);
        Assert.Contains(native, block => block.IsDataBlock);
        Assert.DoesNotContain(native, block => block.Label is "main" or "clearRAM" or "_nmi" or "skipNtsc");
        Assert.DoesNotContain(native, block => block.Label?.StartsWith("__nesbank_", StringComparison.Ordinal) == true);
        var body = Assert.Single(native, block => block.Label == "_Native");
        ushort shadow = result.FixedProgram.GetLabels()["__nesbank_selector"];
        Assert.NotEmpty(body.FindAll(instruction => instruction.Opcode == Opcode.STA &&
            instruction.Operand is AbsoluteOperand address && address.Address == shadow));
    }

    [Fact]
    public void PublicCompilationRejectsNullPrgAssetEntries()
    {
        var options = Options();
        options.PrgBankAssets.Add(null!);
        Assert.Contains("null entries", Assert.Throws<ArgumentException>(() => Compile(Simple, options)).Message);
    }

    [Theory]
    [InlineData(0x5FFF, 1)]
    [InlineData(0x8000, 1)]
    [InlineData(0x7FFF, 2)]
    [InlineData(0x7000, 0)]
    [InlineData(0x7000, -1)]
    [InlineData(0x7000, int.MaxValue)]
    public void RejectsOutOfRangeNativeRamContracts(ushort address, int size)
    {
        var options = Options();
        options.NativeRamCode.Add(new()
        {
            Name = "ram-entry", Address = address, Size = size,
            Contract = NativeRamCodeContract.ForegroundRtsPreservesMapperContext,
        });
        Assert.Contains("PRG RAM", Assert.Throws<TranspileException>(() => Compile(Simple, options)).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsDuplicateOrOverlappingRamContracts(bool duplicateName)
    {
        var options = Options();
        options.NativeRamCode.Add(new()
        {
            Name = "first", Address = 0x7400, Size = 8,
            Contract = NativeRamCodeContract.ForegroundRtsPreservesMapperContext,
        });
        options.NativeRamCode.Add(new()
        {
            Name = duplicateName ? "first" : "second", Address = 0x7404, Size = 8,
            Contract = NativeRamCodeContract.ForegroundRtsPreservesMapperContext,
        });
        Assert.Throws<TranspileException>(() => Compile(Simple, options));
    }

    [Fact]
    public void NativeRamContractHasNoImplicitTrustDefault()
    {
        var options = Options();
        options.NativeRamCode.Add(new() { Name = "entry", Address = 0x7400, Size = 4 });
        Assert.Contains("explicit", Assert.Throws<TranspileException>(() => Compile(Simple, options)).Message);
    }

    [Fact]
    public void StartupInitializesHomeBankAndShadowBeforeEnablingNmi()
    {
        var result = Compile(Simple);
        var program = result.FixedProgram;
        var labels = program.GetLabels();
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, labels["clearRAM"]);
        Array.Fill(cpu.Memory, (byte)0xCC, 0x0300, 0x500);
        byte selector = 0xFF, home = 0xFF;
        bool enabled = false;
        cpu.WriteBus = (address, value) =>
        {
            if (address == 0x8000)
                selector = value;
            else if (address == 0x8001)
            {
                Assert.Equal(6, selector);
                home = value;
            }
            else
                cpu.Memory[address] = value;
            if (address == 0x2000 && (value & 0x80) != 0)
            {
                enabled = true;
                Assert.Equal(6, cpu.Memory[labels["__nesbank_selector"]]);
                Assert.Equal(6, selector);
                Assert.Equal(0, home);
                Assert.Equal(0x4C, cpu.Memory[NESConstants.NMI_CALLBACK]);
                Assert.Equal(0x0800, cpu.SoftwareStackPointer);
            }
        };
        cpu.RunUntil(labels["_waitSync3"]);
        Assert.True(enabled);
        Assert.Equal(program.GetMainBlock("__DESTRUCTOR_TABLE__"), cpu.Memory[0x0300..0x0325]);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Fact]
    public void MethodAnnotationOverridesClassRegionWithoutMovingExterns()
    {
        var options = Options();
        options.ManagedCodeBanks.Add(new() { Name = "second", Bank = 3, Offset = 0x200, Size = 0x1000 });
        var result = Compile("""
            Audio.One(); Audio.Two(); while (true) ;
            [NESCodeBank("audio")]
            static class Audio
            {
                public static void One() { poke(0x6000, 1); }
                [NESCodeBank("second")]
                public static void Two() { poke(0x6000, 2); }
                public static extern void UnusedNative();
            }
            """, options);
        Assert.EndsWith("_One", Assert.Single(result.Regions.Single(region => region.Placement.Name == "audio").Program.Blocks).Label);
        var second = result.Regions.Single(region => region.Placement.Name == "second");
        Assert.EndsWith("_Two", Assert.Single(second.Program.Blocks).Label);
        Assert.Equal(0x8200, second.Program.BaseAddress);
    }
}
