using System.Diagnostics;
using System.Xml.Linq;
using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;
using Xunit.Abstractions;

namespace dotnes.tests;

public sealed class Mmc3BankLayoutTests : IDisposable
{
    readonly ILogger _logger;
    readonly string _tempDirectory;

    public Mmc3BankLayoutTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"dotnes-mmc3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void WritesDeterministicPhysicalPrgAndChrBanks()
    {
        string prgPath = WriteText(
            "bank0.s",
            """
            .segment "CODE"
            .import ppu_on_all
            bank_entry:
                jsr ppu_on_all
                lda #<bank_data
                ldx #>bank_data
                rts
            .segment "RODATA"
            bank_data:
                .byte $42
            bank_pointer:
                .word bank_data
            """);
        string chrPath = WriteBytes("chr8.bin", [0xDE, 0xAD, 0xBE, 0xEF]);
        var prgAssets = new[]
        {
            new BankedRomAsset(prgPath, Bank: 0, Offset: 0, CpuAddress: 0x8000),
        };
        var chrAssets = new[]
        {
            new BankedRomAsset(chrPath, Bank: 8, Offset: 0),
        };

        byte[] first = WriteRom(prgAssets, chrAssets);
        byte[] second = WriteRom(prgAssets, chrAssets);

        Assert.Equal(first, second);
        Assert.Equal(16 + (2 * NESWriter.PRG_ROM_BLOCK_SIZE) + (2 * NESWriter.CHR_ROM_BLOCK_SIZE), first.Length);
        Assert.Equal(new byte[] { (byte)'N', (byte)'E', (byte)'S', 0x1A }, first.Take(4));
        Assert.Equal(2, first[4]);
        Assert.Equal(2, first[5]);
        Assert.Equal(0x40, first[6]);
        Assert.Equal(0, first[7]);

        const int prgStart = 16;
        ushort fixedTarget = ReadWord(first, prgStart + 1);
        Assert.InRange(fixedTarget, 0xC000, 0xFFF1);
        Assert.Equal(
            new byte[] { 0xA9, 0x08, 0xA2, 0x80, 0x60, 0x42, 0x08, 0x80 },
            first.Skip(prgStart + 3).Take(8));
        Assert.All(
            first.Skip(prgStart + Mmc3BankLayout.PrgBankSize).Take(Mmc3BankLayout.PrgBankSize),
            value => Assert.Equal(0, value));

        int fixedProgramOffset = prgStart + (2 * Mmc3BankLayout.PrgBankSize);
        Assert.NotEqual(0, first[fixedProgramOffset]);
        int vectorsOffset = prgStart + (2 * NESWriter.PRG_ROM_BLOCK_SIZE) - 6;
        Assert.Equal(
            new byte[] { 0xA9, 0x00, 0x8D, 0x00, 0x80, 0x4C, 0x00, 0xC0 },
            first.Skip(vectorsOffset - 8).Take(8));
        ushort nmi = ReadWord(first, vectorsOffset);
        ushort reset = ReadWord(first, vectorsOffset + 2);
        ushort irq = ReadWord(first, vectorsOffset + 4);
        Assert.InRange(nmi, 0xC000, 0xFFF1);
        Assert.Equal(Mmc3BankLayout.ResetStubAddress, reset);
        Assert.InRange(irq, 0xC000, 0xFFF1);

        int chrStart = prgStart + (2 * NESWriter.PRG_ROM_BLOCK_SIZE);
        Assert.Equal(
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            first.Skip(chrStart + (8 * Mmc3BankLayout.ChrBankSize)).Take(4));
    }

    [Fact]
    public void RejectsPrgAssetsInReservedFixedBanks()
    {
        string path = WriteBytes("fixed.bin", [0x60]);
        var asset = new BankedRomAsset(path, Bank: 2, Offset: 0, CpuAddress: 0x8000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage([asset]));

        Assert.Contains("reserved", exception.Message);
    }

    [Fact]
    public void RejectsInvalidSwitchableCpuWindow()
    {
        string path = WriteBytes("window.bin", [0x60]);
        var asset = new BankedRomAsset(path, Bank: 0, Offset: 0, CpuAddress: 0xC000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage([asset]));

        Assert.Contains("$8000 or $A000", exception.Message);
    }

    [Fact]
    public void RejectsPrgBankOverflow()
    {
        string path = WriteBytes("overflow.bin", new byte[Mmc3BankLayout.PrgBankSize + 1]);
        var asset = new BankedRomAsset(path, Bank: 0, Offset: 0, CpuAddress: 0x8000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage([asset]));

        Assert.Contains("exceeds physical 8 KiB bank", exception.Message);
    }

    [Fact]
    public void RejectsOverlappingPrgAssets()
    {
        string first = WriteBytes("first.bin", [1, 2]);
        string second = WriteBytes("second.bin", [3, 4]);
        var assets = new[]
        {
            new BankedRomAsset(first, Bank: 0, Offset: 0, CpuAddress: 0x8000),
            new BankedRomAsset(second, Bank: 0, Offset: 1, CpuAddress: 0x8000),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage(assets));

        Assert.Contains("overlaps", exception.Message);
    }

    [Fact]
    public void RejectsRelativeBranchesAcrossPhysicalBanks()
    {
        string first = WriteText(
            "branch.s",
            """
            .segment "CODE"
            branch_entry:
                bne other_entry
                rts
            """);
        string second = WriteText(
            "target.s",
            """
            .segment "CODE"
            other_entry:
                rts
            """);
        var assets = new[]
        {
            new BankedRomAsset(first, Bank: 0, Offset: 0, CpuAddress: 0x8000),
            new BankedRomAsset(second, Bank: 1, Offset: 0, CpuAddress: 0xA000),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage(assets));

        Assert.Contains("relative branch", exception.Message);
    }

    [Fact]
    public void FinalizesBranchRelaxationBeforeCrossAssetRelocation()
    {
        string padding = string.Join(Environment.NewLine, Enumerable.Repeat("    nop", 130));
        string target = WriteText(
            "relaxed-target.s",
            $"""
            .segment "CODE"
            target_entry:
                bne far_target
            {padding}
            far_target:
                rts
            """);
        string pointer = WriteText(
            "pointer.s",
            """
            .segment "RODATA"
            far_pointer:
                .word far_target
            """);
        var assets = new[]
        {
            new BankedRomAsset(target, Bank: 0, Offset: 0, CpuAddress: 0x8000),
            new BankedRomAsset(pointer, Bank: 1, Offset: 0, CpuAddress: 0xA000),
        };

        byte[] image = BuildPrgImage(assets);

        Assert.Equal(new byte[] { 0xF0, 0x03, 0x4C, 0x87, 0x80 }, image.Take(5));
        Assert.Equal(new byte[] { 0x87, 0x80 }, image.Skip(Mmc3BankLayout.PrgBankSize).Take(2));
    }

    [Fact]
    public void ConvergesBranchRelaxationBeforeCrossAssetRelocation()
    {
        string branches = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 11).Select(index => $"    bne target{index}"));
        string initialPadding = string.Join(Environment.NewLine, Enumerable.Repeat("    nop", 75));
        string targets = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 11).Select(index =>
                $"target{index}:{Environment.NewLine}" +
                string.Join(Environment.NewLine, Enumerable.Repeat("    nop", index == 10 ? 3 : 5))));
        string target = WriteText(
            "relaxation-waves.s",
            $"""
            .segment "CODE"
            {branches}
                bne seed_target
            {initialPadding}
            {targets}
            seed_target:
                rts
            """);
        string pointer = WriteText(
            "relaxed-pointer.s",
            """
            .segment "RODATA"
            seed_pointer:
                .word seed_target
            """);
        var assets = new[]
        {
            new BankedRomAsset(target, Bank: 0, Offset: 0, CpuAddress: 0x8000),
            new BankedRomAsset(pointer, Bank: 1, Offset: 0, CpuAddress: 0xA000),
        };

        byte[] image = BuildPrgImage(assets);

        // The seed branch triggers eleven successive relaxation waves, moving seed_target to $80BC.
        Assert.Equal(0x60, image[0xBC]);
        Assert.Equal(new byte[] { 0xBC, 0x80 }, image.Skip(Mmc3BankLayout.PrgBankSize).Take(2));
    }

    [Fact]
    public void RejectsUnresolvedDataRelocation()
    {
        string path = WriteText(
            "unresolved.s",
            """
            .segment "RODATA"
            missing_pointer:
                .word missing_label
            """);
        var asset = new BankedRomAsset(path, Bank: 0, Offset: 0, CpuAddress: 0x8000);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildPrgImage([asset]));

        Assert.Contains("unresolved data relocation", exception.Message);
    }

    [Fact]
    public void AllowsAnonymousCodeInMultipleAssets()
    {
        string first = WriteText(
            "anonymous-first.s",
            """
            .segment "CODE"
                nop
                rts
            """);
        string second = WriteText(
            "anonymous-second.s",
            """
            .segment "CODE"
                inx
                rts
            """);
        var assets = new[]
        {
            new BankedRomAsset(first, Bank: 0, Offset: 0, CpuAddress: 0x8000),
            new BankedRomAsset(second, Bank: 1, Offset: 0, CpuAddress: 0xA000),
        };

        byte[] image = BuildPrgImage(assets);

        Assert.Equal(new byte[] { 0xEA, 0x60 }, image.Take(2));
        Assert.Equal(new byte[] { 0xE8, 0x60 }, image.Skip(Mmc3BankLayout.PrgBankSize).Take(2));
    }

    [Fact]
    public void RejectsChrRangeOverflowAndOverlap()
    {
        string outOfRange = WriteBytes("range.bin", [1]);
        var rangeException = Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildChrImage(
                Array.Empty<byte>(),
                chrBanks: 1,
                [new BankedRomAsset(outOfRange, Bank: 8, Offset: 0)]));
        Assert.Contains("valid banks are 0-7", rangeException.Message);

        string overflow = WriteBytes("chr-overflow.bin", new byte[Mmc3BankLayout.ChrBankSize + 1]);
        var overflowException = Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildChrImage(
                Array.Empty<byte>(),
                chrBanks: 1,
                [new BankedRomAsset(overflow, Bank: 0, Offset: 0)]));
        Assert.Contains("exceeds physical 1 KiB bank", overflowException.Message);

        string overlap = WriteBytes("chr-overlap.bin", [2]);
        var overlapException = Assert.Throws<InvalidOperationException>(
            () => Mmc3BankLayout.BuildChrImage(
                [1],
                chrBanks: 1,
                [new BankedRomAsset(overlap, Bank: 0, Offset: 0)]));
        Assert.Contains("overlaps", overlapException.Message);
    }

    [Fact]
    public void RequiresMapperFourForBankedLayout()
    {
        using var dll = Utilities.GetResource("bankswitch.release.dll");
        using var chr = new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")));
        using var transpiler = new Transpiler(
            dll,
            [chr],
            _logger,
            mapper: 0,
            prgBanks: 2,
            chrBanks: 1,
            mmc3BankedLayout: true);

        var exception = Assert.Throws<InvalidOperationException>(
            () => transpiler.Write(new MemoryStream()));

        Assert.Contains("NESMapper=4", exception.Message);
    }

    [Fact]
    public void SupportsMaximumMmc3BankCounts()
    {
        byte[] rom = WriteRom([], [], prgBanks: 32, chrBanks: 32);

        Assert.Equal(32, rom[4]);
        Assert.Equal(32, rom[5]);
        Assert.Equal(16 + (32 * NESWriter.PRG_ROM_BLOCK_SIZE) + (32 * NESWriter.CHR_ROM_BLOCK_SIZE), rom.Length);
    }

    [Theory]
    [InlineData(33, 1, "NESPrgBanks")]
    [InlineData(2, 33, "NESChrBanks")]
    public void RejectsMmc3BankCountsAboveHardwareLimits(int prgBanks, int chrBanks, string property)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WriteRom([], [], prgBanks, chrBanks));

        Assert.Contains(property, exception.Message);
        Assert.Contains("at most 32", exception.Message);
    }

    [Fact]
    public async Task MSBuildTargetsPlaceAbsoluteBankItems()
    {
        string taskDirectory = Path.GetDirectoryName(typeof(TranspileToNES).Assembly.Location)!;
        string targetPath = Path.Combine(_tempDirectory, "bankswitch.dll");
        using (var source = Utilities.GetResource("bankswitch.release.dll"))
        using (var destination = File.Create(targetPath))
            source.CopyTo(destination);

        string chrGenericPath = Path.Combine(_tempDirectory, "chr_generic.s");
        using (var source = Utilities.GetResource("chr_generic.s"))
        using (var destination = File.Create(chrGenericPath))
            source.CopyTo(destination);

        string prgPath = WriteText(
            "bank0.s",
            """
            .segment "CODE"
            bank_entry:
                rts
            """);
        string chrPath = WriteText(
            "bank8.s",
            """
            .segment "CHARS"
            .byte $DE,$AD,$BE,$EF
            """);
        string outputDirectory = Path.Combine(_tempDirectory, "out");
        string intermediateDirectory = Path.Combine(_tempDirectory, "obj");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(intermediateDirectory);

        string projectPath = Path.Combine(_tempDirectory, "banked-layout.proj");
        var project = new XDocument(
            new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("TargetPath", targetPath),
                    new XElement("OutputPath", outputDirectory + Path.DirectorySeparatorChar),
                    new XElement("TargetName", "bankswitch"),
                    new XElement("IntermediateOutputPath", intermediateDirectory + Path.DirectorySeparatorChar),
                    new XElement("NESMapper", "4"),
                    new XElement("NESPrgBanks", "2"),
                    new XElement("NESChrBanks", "2"),
                    new XElement("NESMmc3BankedLayout", "true")),
                new XElement("ItemGroup",
                    new XElement("NESAssembly", new XAttribute("Include", "*.s")),
                    new XElement("NESPrgBank",
                        new XAttribute("Include", prgPath),
                        new XAttribute("Bank", "0"),
                        new XAttribute("CpuAddress", "0x8000")),
                    new XElement("NESChrBank",
                        new XAttribute("Include", chrPath),
                        new XAttribute("Bank", "8"))),
                new XElement("Import",
                    new XAttribute("Project", Path.Combine(taskDirectory, "dotnes.targets")))));
        project.Save(projectPath);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _tempDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-t:Transpile");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-v:minimal");

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet msbuild timed out.");
        }
        string[] output = await Task.WhenAll(standardOutputTask, standardErrorTask);
        string standardOutput = output[0];
        string standardError = output[1];
        Assert.True(
            process.ExitCode == 0,
            $"dotnet msbuild failed with exit code {process.ExitCode}:{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");

        byte[] rom = File.ReadAllBytes(Path.Combine(outputDirectory, "bankswitch.nes"));
        Assert.Equal(2, rom[4]);
        Assert.Equal(2, rom[5]);
        Assert.Equal(0x60, rom[16]);

        int chrStart = 16 + (2 * NESWriter.PRG_ROM_BLOCK_SIZE);
        Assert.Equal(
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            rom.Skip(chrStart + (8 * Mmc3BankLayout.ChrBankSize)).Take(4));

        int vectorsOffset = chrStart - 6;
        Assert.Equal(Mmc3BankLayout.ResetStubAddress, ReadWord(rom, vectorsOffset + 2));
    }

    byte[] WriteRom(
        IReadOnlyList<BankedRomAsset> prgAssets,
        IReadOnlyList<BankedRomAsset> chrAssets,
        int prgBanks = 2,
        int chrBanks = 2)
    {
        using var dll = Utilities.GetResource("bankswitch.release.dll");
        using var chr = new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")));
        using var transpiler = new Transpiler(
            dll,
            [chr],
            _logger,
            mapper: 4,
            prgBanks: prgBanks,
            chrBanks: chrBanks,
            mmc3BankedLayout: true,
            prgBankAssets: prgAssets,
            chrBankAssets: chrAssets);
        using var output = new MemoryStream();
        transpiler.Write(output);
        return output.ToArray();
    }

    byte[] BuildPrgImage(IReadOnlyList<BankedRomAsset> assets)
    {
        var program = new Program6502 { BaseAddress = Mmc3BankLayout.FixedProgramAddress };
        program.CreateBlock("_nmi").Emit(RTS());
        program.CreateBlock("_irq").Emit(RTS());
        program.ResolveAddresses();
        var labels = program.GetLabels();
        return Mmc3BankLayout.BuildPrgImage(
            program,
            prgBanks: 2,
            assets,
            labels["_nmi"],
            labels["_irq"]);
    }

    string WriteText(string name, string contents)
    {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    string WriteBytes(string name, byte[] contents)
    {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    static ushort ReadWord(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }
}
