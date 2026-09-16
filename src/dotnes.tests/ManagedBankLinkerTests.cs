using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public sealed class ManagedBankLinkerTests : IDisposable
{
    readonly string directory = Path.Combine(Environment.CurrentDirectory, $"managed-linker-{Guid.NewGuid():N}");

    [Fact]
    public void RefreshesCallsAliasesImmediatesAndDataAfterRelaxationAndEdits()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("caller")
            .Emit(JSR("entry_alias"))
            .Emit(new Instruction(Opcode.LDA, AddressMode.Immediate, new LowByteOperand("after")))
            .Emit(new Instruction(Opcode.LDX, AddressMode.Immediate, new HighByteOperand("after")));
        var pointer = Block.FromRawData(new byte[2], "pointer");
        pointer.Relocations = [(0, "after")];
        fixedProgram.AddBlock(pointer);

        var banked = new Program6502();
        var entry = banked.CreateBlock("entry");
        entry.AdditionalLabels = ["entry_alias=after"];
        entry.Emit(BNE("after"));
        entry.EmitRange(Enumerable.Repeat(NOP(), 130));
        banked.CreateBlock("after").Emit(RTS());
        var result = new BankedCompilation(fixedProgram, [Region(banked)]);
        result.ResolveAndRelax();

        Assert.Equal(0x8087, banked.Labels.Resolve("after"));
        Assert.Equal(0x8087, fixedProgram.Labels.Resolve("after"));
        Assert.Equal(new byte[] { 0x20, 0x87, 0x80, 0xA9, 0x87, 0xA2, 0x80 },
            fixedProgram.GetMainBlock("caller"));
        Assert.Equal(new byte[] { 0x87, 0x80 }, fixedProgram.ToBytes().TakeLast(2));
        Assert.Equal(new byte[] { 0xF0, 0x03, 0x4C, 0x87, 0x80 }, banked.ToBytes().Take(5));

        entry.Insert(0, NOP());
        result.ResolveAndRelax();
        Assert.Equal(0x8088, fixedProgram.Labels.Resolve("after"));
        byte[] first = fixedProgram.ToBytes();
        result.ResolveAndRelax();
        Assert.Equal(first, fixedProgram.ToBytes());
        Assert.Equal(new byte[] { 0x88, 0x80 }, first.TakeLast(2));
    }

    [Fact]
    public void ConvergesThroughSuccessiveRelaxationWaves()
    {
        var target = new Program6502();
        var block = target.CreateBlock("target");
        for (int i = 0; i < 11; i++)
            block.Emit(BNE($"target{i}"));
        block.Emit(BNE("seed"));
        block.EmitRange(Enumerable.Repeat(NOP(), 75));
        for (int i = 0; i < 11; i++)
        {
            block.Emit(NOP(), $"target{i}");
            block.EmitRange(Enumerable.Repeat(NOP(), i == 10 ? 2 : 4));
        }
        block.Emit(RTS(), "seed");
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("caller").Emit(JSR("seed"));

        BankedCompilation.LinkPrograms([fixedProgram, target]);

        Assert.Equal(0x80BC, fixedProgram.Labels.Resolve("seed"));
        Assert.Equal(new byte[] { 0x20, 0xBC, 0x80 }, fixedProgram.GetMainBlock("caller"));
    }

    [Fact]
    public void ExcludesForwardLabelsAndExternalBindingsFromExports()
    {
        var program = Program6502.CreateWithBuiltIns();
        program.DefineExternalLabel("caller_binding", 0x9000);
        program.ResolveAddresses();

        var labels = program.GetDefinedLabels();
        Assert.DoesNotContain("main", labels.Keys);
        Assert.DoesNotContain("caller_binding", labels.Keys);
        Assert.Contains(NESConstants._nmi, labels.Keys);
    }

    [Fact]
    public void PreservesNativeExternSpellingForImportedLegacyLabels()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.RegisterExternSymbol("native");
        fixedProgram.CreateBlock("caller").Emit(JSR("_native"));
        var native = new Program6502();
        native.AddNativeBlock(new Block("native").Emit(RTS()));

        BankedCompilation.LinkPrograms([fixedProgram, native]);

        Assert.Equal(new byte[] { 0x20, 0x00, 0x80 }, fixedProgram.GetMainBlock("caller"));
        Assert.DoesNotContain("_native", fixedProgram.GetDefinedLabels().Keys);
    }

    [Fact]
    public void RemovesImportsWhenTheirOwnersAreRemoved()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("caller").Emit(JSR("removed"));
        var banked = new Program6502();
        var block = banked.CreateBlock("removed").Emit(RTS());
        BankedCompilation.LinkPrograms([fixedProgram, banked]);
        banked.RemoveBlock(block);

        BankedCompilation.LinkPrograms([fixedProgram, banked]);

        Assert.False(fixedProgram.Labels.TryResolve("removed", out _));
        Assert.Throws<UnresolvedLabelException>(() => fixedProgram.ToBytes());
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0xC010)]
    public void RejectsCrossImageBranchesBeforeRelaxing(int targetAddress)
    {
        var fixedProgram = FixedProgram();
        var caller = fixedProgram.CreateBlock("caller").Emit(BNE("other"));
        var other = new Program6502 { BaseAddress = (ushort)targetAddress };
        other.CreateBlock("other").Emit(RTS());

        var error = Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([fixedProgram, other]));

        Assert.Contains("relative branch", error.Message);
        Assert.Equal(2, caller.Size);
    }

    [Fact]
    public void RejectsBranchAliasesToImportedSymbolsInOverlappingCpuWindows()
    {
        var first = new Program6502();
        var branch = first.CreateBlock("first").Emit(BNE("alias"));
        branch.AdditionalLabels = ["alias=other"];
        var second = new Program6502();
        second.CreateBlock("other").Emit(RTS());

        Assert.Contains("relative branch", Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([first, second])).Message);
        Assert.Equal(2, branch.Size);
    }

    [Fact]
    public void ScopesCheapInstructionAndDataLabels()
    {
        var fixedProgram = FixedProgram();
        var a = fixedProgram.CreateBlock("a");
        a.Emit(BNE("@end")).Emit(RTS(), "@end");
        var banked = new Program6502();
        var b = banked.CreateBlock("b");
        b.Emit(BNE("@end")).Emit(RTS(), "@end");
        var table = Block.FromRawData(new byte[4], "table");
        table.InternalLabels = new() { ["@value"] = 2 };
        table.Relocations = [(0, "@value")];
        banked.AddBlock(table);

        BankedCompilation.LinkPrograms([fixedProgram, banked]);

        Assert.Equal(new byte[] { 0xD0, 0x00, 0x60 }, fixedProgram.GetMainBlock("a"));
        Assert.Equal(new byte[] { 0x05, 0x80, 0x00, 0x00 }, banked.ToBytes().TakeLast(4));
        Assert.DoesNotContain("@end", fixedProgram.GetDefinedLabels().Keys);
    }

    [Fact]
    public void RejectsDuplicateRealLabelsButAllowsIndependentAnonymousCode()
    {
        var first = new Program6502();
        first.CreateBlock("_anonymous_code_0").Emit(RTS(), "@end");
        var second = new Program6502();
        second.CreateBlock("_anonymous_code_0").Emit(RTS(), "@end");
        BankedCompilation.LinkPrograms([first, second]);

        first.CreateBlock("duplicate").Emit(RTS());
        second.CreateBlock("duplicate").Emit(RTS());
        Assert.Contains("duplicate label", Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([first, second])).Message);
    }

    [Theory]
    [InlineData("main", "instruction_5A")]
    [InlineData("method", "method_instruction_5A")]
    public void ReemittedManagedIlLabelsSupersedeStaleAliasesWithinTheirOwnBlock(string method, string label)
    {
        var program = FixedProgram();
        var block = program.CreateBlock(method).Emit(NOP(), label);
        block.SetNextLabel("replacement");
        block.SetNextLabel(label);
        block.Emit(RTS());
        block.AdditionalLabels = [$"exported={label}"];
        var caller = new Program6502();
        caller.CreateBlock("caller").Emit(JSR("exported"));

        BankedCompilation.LinkPrograms([program, caller]);

        Assert.Equal(0xC002, program.Labels.Resolve(label));
        Assert.Equal(new byte[] { 0x20, 0x02, 0xC0 }, caller.ToBytes());
        program.ResolveAddresses();
        Assert.Equal(0xC002, program.Labels.Resolve(label));
    }

    [Theory]
    [InlineData(true, false, "instruction_5A")]
    [InlineData(false, true, "instruction_5A")]
    [InlineData(false, false, "instruction_not_hex")]
    [InlineData(false, false, "other_instruction_5A")]
    public void StillRejectsNativeExportAndNonsyntheticAliasCollisions(bool native, bool export, string label)
    {
        var program = FixedProgram();
        var block = new Block("main").Emit(NOP(), label);
        if (export)
        {
            block.Emit(RTS(), "replacement");
            block.AdditionalLabels = [$"{label}=replacement"];
        }
        else
        {
            block.SetNextLabel("replacement");
            block.SetNextLabel(label);
            block.Emit(RTS());
        }
        if (native)
            program.AddNativeBlock(block);
        else
            program.AddBlock(block);

        Assert.Contains("duplicate label", Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([program])).Message);
    }

    [Fact]
    public void StillRejectsDuplicateConcreteManagedIlLabels()
    {
        var program = FixedProgram();
        program.CreateBlock("main").Emit(NOP(), "instruction_5A").Emit(RTS(), "instruction_5A");

        Assert.Contains("duplicate label", Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([program])).Message);
    }

    [Fact]
    public void AllowsUnboundRelocationsWhileLinkingButNotWhileEmitting()
    {
        var fixedProgram = FixedProgram();
        var data = Block.FromRawData(new byte[2], "table");
        data.Relocations = [(0, "external")];
        fixedProgram.AddBlock(data);
        var result = new BankedCompilation(fixedProgram, []);
        result.ResolveAndRelax();

        Assert.Contains("unresolved data relocation", Assert.Throws<InvalidOperationException>(
            () => fixedProgram.ToBytes()).Message);
        fixedProgram.DefineExternalLabel("external", 0x8123);
        result.ResolveAndRelax();
        Assert.Equal(new byte[] { 0x23, 0x81 }, fixedProgram.ToBytes().TakeLast(2));
    }

    [Theory]
    [InlineData(16370, true)]
    [InlineData(16371, false)]
    [InlineData(16385, false)]
    public void ChecksFixedCapacityAndAddressOverflow(int size, bool fits)
    {
        var fixedProgram = FixedProgram();
        fixedProgram.AddRawData(new byte[size - fixedProgram.TotalSize]);
        var result = new BankedCompilation(fixedProgram, []);
        if (fits)
        {
            result.ResolveAndRelax();
            Assert.Equal(size, fixedProgram.ToBytes().Length);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => result.ResolveAndRelax());
        }
    }

    [Fact]
    public void RejectsOverflowIntroducedByRelaxation()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("branch").Emit(BNE("tail"));
        fixedProgram.AddRawData(new byte[16378]);
        fixedProgram.CreateBlock("tail").Emit(RTS());

        Assert.Contains("16-bit CPU address space", Assert.Throws<InvalidOperationException>(
            () => BankedCompilation.LinkPrograms([fixedProgram])).Message);
    }

    [Fact]
    public void ReservesUnusedManagedSpaceAndAllowsDisjointNativeAndRawAssets()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.RegisterExternSymbol("native");
        fixedProgram.CreateBlock("caller").Emit(JSR("_native"));
        var banked = new Program6502();
        banked.CreateBlock("managed").Emit(JSR("native")).Emit(RTS());
        var region = Region(banked, size: 0x100);
        string native = Write("native.s", ".segment \"CODE\"\nnative:\n    rts\n");
        string raw = Write("raw.bin", new byte[] { 0x42, 0x43 });
        var assets = new[]
        {
            new BankedRomAsset(native, 0, 0x100, 0x8000),
            new BankedRomAsset(raw, 0, 0x101, 0x8000),
        };

        var image = Mmc3BankLayout.BuildPrgImage(fixedProgram, 2, assets, 0, 0, [region]);

        Assert.Equal(new byte[] { 0x20, 0x00, 0x81, 0x60 }, image.Take(4));
        Assert.All(image.Skip(4).Take(0x100 - 4), value => Assert.Equal(0, value));
        Assert.Equal(new byte[] { 0x60, 0x42, 0x43 }, image.Skip(0x100).Take(3));
        Assert.Contains("overlaps", Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildPrgImage(fixedProgram, 2,
                [assets[0], new BankedRomAsset(raw, 0, 0xFF, 0x8000)], 0, 0, [region])).Message);
    }

    [Fact]
    public void UsesFinalFixedVectorsAfterBranchRelaxation()
    {
        var fixedProgram = new Program6502 { BaseAddress = 0xC000 };
        fixedProgram.CreateBlock("start").Emit(BNE("far"));
        fixedProgram.AddRawData(new byte[130]);
        fixedProgram.CreateBlock("far").Emit(RTS());
        fixedProgram.CreateBlock(NESConstants._nmi).Emit(RTS());
        fixedProgram.CreateBlock(NESConstants._irq).Emit(RTS());
        fixedProgram.CreateBlock(NESConstants.irq_with_callback).Emit(RTS());

        byte[] image = Mmc3BankLayout.BuildPrgImage(fixedProgram, 2, [], 0x1234, 0x5678, []);

        Assert.Equal(new byte[] { 0x88, 0xC0, 0xF2, 0xFF, 0x8A, 0xC0 }, image.TakeLast(6));
    }

    [Fact]
    public void PreparesAllLinkedManagedProgramsOnceThenRelinksEdits()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("caller").Emit(JSR("native_tail"));
        var managed = new Program6502();
        managed.CreateBlock("managed").Emit(JSR("native_tail"));
        string native = Write("prepare.s",
            ".segment \"CODE\"\nnative_entry:\n    nop\nnative_tail:\n    rts\n");
        var assets = new[] { new BankedRomAsset(native, 0, 0x100, 0x8000) };
        int calls = 0;

        byte[] image = Mmc3BankLayout.BuildPrgImage(
            fixedProgram, 2, assets, 0, 0, [Region(managed, size: 0x100)],
            prepareManagedPrograms: programs =>
            {
                calls++;
                Assert.Equal(3, programs.Count);
                Assert.Same(fixedProgram, programs[0]);
                Assert.Same(managed, programs[1]);
                Assert.Equal(0x8101, fixedProgram.Labels.Resolve("native_tail"));
                Assert.Equal(0x8101, managed.Labels.Resolve("native_tail"));
                var nativeProgram = programs[2];
                var entry = nativeProgram.GetBlock("native_entry")!;
                Assert.True(nativeProgram.IsNativeBlock(entry));
                entry.Insert(0, NOP());
            });

        Assert.Equal(1, calls);
        Assert.Equal(new byte[] { 0x20, 0x02, 0x81 }, image.Take(3));
        Assert.Equal(new byte[] { 0xEA, 0xEA, 0x60 }, image.Skip(0x100).Take(3));
        Assert.Equal(new byte[] { 0x20, 0x02, 0x81 }, fixedProgram.GetMainBlock("caller"));
    }

    [Fact]
    public void DoesNotPrepareLegacyPrograms()
    {
        int calls = 0;
        Mmc3BankLayout.BuildPrgImage(FixedProgram(), 2, [], 0xC000, 0xC001,
            prepareManagedPrograms: _ => calls++);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ValidatesManagedReservationAfterPreparationGrowth()
    {
        var managed = new Program6502();
        var code = managed.CreateBlock("managed").Emit(RTS());

        Assert.Contains("reserved Size", Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildPrgImage(FixedProgram(), 2, [], 0, 0, [Region(managed, size: 1)],
                prepareManagedPrograms: _ => code.Insert(0, NOP()))).Message);
    }

    [Theory]
    [InlineData(-1, 0, 1, 0x8000)]
    [InlineData(2, 0, 1, 0x8000)]
    [InlineData(0, -1, 1, 0x8000)]
    [InlineData(0, 0, 0, 0x8000)]
    [InlineData(0, 8191, 2, 0x8000)]
    [InlineData(0, 0, 1, 0xA000)]
    public void RejectsInvalidManagedPlacements(int bank, int offset, int size, int cpuAddress)
    {
        var placement = new ManagedCodeBank
        {
            Name = "invalid", Bank = bank, Offset = offset, Size = size, CpuAddress = (ushort)cpuAddress,
        };
        var region = new ManagedCodeRegion(placement, new Program6502());
        Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildPrgImage(FixedProgram(), 2, [], 0, 0, [region]));
    }

    [Fact]
    public void RejectsWindowConflictsAndOverlappingReservations()
    {
        var fixedProgram = FixedProgram();
        var a = Region(new Program6502(), "a", size: 0x100);
        var b = Region(new Program6502 { BaseAddress = 0x8080 }, "b", offset: 0x80, size: 0x100);
        Assert.Contains("overlaps", Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildPrgImage(fixedProgram, 2, [], 0, 0, [a, b])).Message);
        string raw = Write("conflict.bin", new byte[] { 0x60 });
        Assert.Contains("conflicting CPU windows", Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildPrgImage(fixedProgram, 2,
                [new BankedRomAsset(raw, 0, 0x100, 0xA000)], 0, 0, [a])).Message);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ChecksRegionSizeAtExactBankBoundary(int emittedSize, bool fits)
    {
        var program = new Program6502 { BaseAddress = 0x9FFF };
        program.AddRawData(new byte[emittedSize]);
        var region = Region(program, offset: 0x1FFF, size: 1);
        if (fits)
        {
            byte[] image = Mmc3BankLayout.BuildPrgImage(FixedProgram(), 2, [], 0, 0, [region]);
            Assert.Equal(32768, image.Length);
        }
        else
        {
            Assert.Contains("reserved Size", Assert.Throws<InvalidOperationException>(
                () => Mmc3BankLayout.BuildPrgImage(FixedProgram(), 2, [], 0, 0, [region])).Message);
        }
    }

    [Fact]
    public void CopiesPlacementAndRegionList()
    {
        var placement = new ManagedCodeBank { Name = "original", Size = 10 };
        var region = new ManagedCodeRegion(placement, new Program6502());
        var regions = new List<ManagedCodeRegion> { region };
        var result = new BankedCompilation(FixedProgram(), regions);
        placement.Name = "changed";
        regions.Clear();

        Assert.Equal("original", Assert.Single(result.Regions).Placement.Name);
    }

    [Fact]
    public void ExposesPreparedA000WindowAssetsAndReusesTheirModelsDuringEmission()
    {
        var fixedProgram = FixedProgram();
        fixedProgram.CreateBlock("caller").Emit(JSR("_native"));
        var managed = new Program6502();
        managed.CreateBlock("managed").Emit(JSR("jump_vector"));
        string native = Write("title-io.s",
            """
            .segment "CODE"
            _native:
                jmp worker
            jump_vector:
                jmp worker
            worker:
                rts
            .segment "RODATA"
            native_table:
                .word caller
            """);
        string binary = Write("data.bin", new byte[] { 0x42 });
        var inputs = new[]
        {
            new BankedRomAsset(native, 1, 0x1000, 0xA000),
            new BankedRomAsset(binary, 1, 0x1100, 0xA000),
        };
        var assets = Mmc3BankLayout.PrepareManagedPrgAssets(inputs, 2);
        var result = new BankedCompilation(fixedProgram, [Region(managed)], assets, 2);
        result.ResolveAndRelax();

        Assert.Equal(2, result.PrgAssets.Count);
        var nativeAsset = result.PrgAssets[0];
        var nativeProgram = nativeAsset.Program!;
        Assert.Null(nativeAsset.Data);
        Assert.Equal(0xB000, fixedProgram.Labels.Resolve("_native"));
        Assert.Equal(0xB003, managed.Labels.Resolve("jump_vector"));
        var nativeCode = Assert.Single(nativeProgram.Blocks, block => !block.IsDataBlock);
        Assert.True(nativeProgram.IsNativeBlock(nativeCode));
        Assert.False(nativeProgram.IsNativeBlock(nativeProgram.GetBlock("native_table")!));
        Assert.Same(nativeProgram, result.GetPrograms()[2]);
        Assert.Null(result.PrgAssets[1].Program);

        nativeCode.Insert(nativeCode.Count - 1, NOP());
        result.PrgAssets[1].Data![0] = 0x43;
        File.Delete(native);
        File.Delete(binary);
        result.ResolveAndRelax();
        byte[] image = Mmc3BankLayout.BuildPrgImage(
            fixedProgram, 2, inputs, 0, 0, result.Regions, compiledPrgAssets: result.PrgAssets);

        Assert.Equal(new byte[] { 0x4C, 0x07, 0xB0, 0x4C, 0x07, 0xB0, 0xEA, 0x60, 0x02, 0xC0 },
            image.Skip(8192 + 0x1000).Take(10));
        Assert.Equal(0x43, image[8192 + 0x1100]);
    }

    [Fact]
    public void PreparedAssetsCopyPlacementAndBinaryInput()
    {
        var placement = new PrgBankAsset { Path = "original.bin", Bank = 1, CpuAddress = 0xA000, Offset = 0x1000 };
        byte[] bytes = [0x42];
        var asset = new CompiledPrgAsset(placement, null, bytes);
        var list = new List<CompiledPrgAsset> { asset };
        var result = new BankedCompilation(FixedProgram(), [], list, 2);
        placement.Offset = 0;
        bytes[0] = 0;
        list.Clear();

        Assert.Equal(0x1000, Assert.Single(result.PrgAssets).Placement.Offset);
        Assert.Equal(0x42, asset.Data![0]);
    }

    [Theory]
    [InlineData(0, 0xFF, 1, 0x8000, true)]
    [InlineData(0, 0x100, 1, 0x8000, false)]
    [InlineData(0, 0x100, 1, 0xA000, true)]
    [InlineData(1, 0x1FFF, 2, 0xA000, true)]
    [InlineData(2, 0, 1, 0x8000, true)]
    public void ResultValidatesNativeAssetsAgainstReservationsAndPhysicalBanks(
        int bank, int offset, int size, int cpuAddress, bool invalid)
    {
        var asset = new CompiledPrgAsset(
            new PrgBankAsset { Path = "data.bin", Bank = bank, Offset = offset, CpuAddress = (ushort)cpuAddress },
            null, new byte[size]);
        var result = new BankedCompilation(FixedProgram(),
            [Region(new Program6502(), size: 0x100)], [asset], 2);
        if (invalid)
            Assert.Throws<InvalidOperationException>(() => result.ResolveAndRelax());
        else
            result.ResolveAndRelax();
    }

    [Fact]
    public void ResultValidatesAssetOverlapAfterNativeRelaxation()
    {
        var native = new Program6502 { BaseAddress = 0xA000 };
        native.CreateBlock("native").Emit(BNE("far"));
        native.AddRawData(new byte[130]);
        native.CreateBlock("far").Emit(RTS());
        var programAsset = new CompiledPrgAsset(
            new PrgBankAsset { Path = "native.s", Bank = 1, CpuAddress = 0xA000 }, native, null);
        var dataAsset = new CompiledPrgAsset(
            new PrgBankAsset { Path = "data.bin", Bank = 1, CpuAddress = 0xA000, Offset = 133 }, null, [0x42]);
        var result = new BankedCompilation(FixedProgram(), [], [programAsset, dataAsset], 2);

        Assert.Contains("overlaps", Assert.Throws<InvalidOperationException>(
            () => result.ResolveAndRelax()).Message);
        Assert.Equal(136, native.TotalSize);
    }

    [Fact]
    public void AllowsUnresolvedPreparedNativeDataUntilEmission()
    {
        string path = Write("unbound.s", ".segment \"RODATA\"\npointer:\n    .word external\n");
        var assets = Mmc3BankLayout.PrepareManagedPrgAssets([new BankedRomAsset(path, 1, 0, 0xA000)], 2);
        var result = new BankedCompilation(FixedProgram(), [], assets, 2);
        result.ResolveAndRelax();

        Assert.Contains("unresolved data relocation", Assert.Throws<InvalidOperationException>(
            () => result.PrgAssets[0].Program!.ToBytes()).Message);
    }

    static Program6502 FixedProgram()
    {
        var program = new Program6502 { BaseAddress = 0xC000 };
        program.CreateBlock(NESConstants._nmi).Emit(RTS());
        program.CreateBlock(NESConstants._irq).Emit(RTS());
        return program;
    }

    static ManagedCodeRegion Region(
        Program6502 program, string name = "banked", int offset = 0, int size = 8192)
        => new(new ManagedCodeBank { Name = name, Bank = 0, Offset = offset, Size = size }, program);

    string Write(string name, string text)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    string Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
