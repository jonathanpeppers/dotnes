using Xunit.Abstractions;

namespace dotnes.tests;

public class DecompilerTests
{
    readonly ILogger _logger;

    public DecompilerTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    [Fact]
    public void NESRomReader_ParsesHeader()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(2, reader.PrgBanks);
        Assert.Equal(1, reader.ChrBanks);
        Assert.Equal(0, reader.Mapper);
        Assert.False(reader.VerticalMirroring);
        Assert.False(reader.HasBattery);
        Assert.Equal(0x8000, reader.ResetVector);
        Assert.Equal(32768, reader.PrgRom.Length); // 2 * 16KB
        Assert.Equal(8192, reader.ChrRom.Length);  // 1 * 8KB
    }

    [Theory]
    [InlineData("statusbar", true)]   // verticalMirroring=true
    [InlineData("horizscroll", true)] // verticalMirroring=true
    public void NESRomReader_VerticalMirroring(string name, bool expectedMirroring)
    {
        var romBytes = GetVerifiedRom(name);
        var reader = new NESRomReader(romBytes);

        Assert.Equal(expectedMirroring, reader.VerticalMirroring);
    }

    [Fact]
    public void NESRomReader_ChrRamMode()
    {
        var romBytes = GetVerifiedRom("transtable");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(0, reader.ChrBanks);
        Assert.Empty(reader.ChrRom);
    }

    [Fact]
    public void NESRomReader_ChrRomExtraction()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(8192, reader.ChrRom.Length);
        var chrAsm = reader.GenerateChrAssembly();
        Assert.StartsWith(".segment \"CHARS\"", chrAsm);
        Assert.Contains("$", chrAsm); // Should have hex bytes
    }

    [Fact]
    public void NESRomReader_InterruptVectors()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        // Reset vector should point to $8000 (start of PRG ROM)
        Assert.Equal(0x8000, reader.ResetVector);
        // NMI and IRQ vectors should be in PRG ROM range
        Assert.True(reader.NmiVector >= 0x8000);
        Assert.True(reader.IrqVector >= 0x8000);
    }

    [Fact]
    public void NESRomReader_InvalidRom_Throws()
    {
        var badData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Throws<InvalidOperationException>(() => new NESRomReader(badData));
    }

    [Fact]
    public void Decompiler_Hello_ProducesCorrectOutput()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify key NESLib calls are present
        Assert.Contains("pal_col(0, 0x02);", code);
        Assert.Contains("pal_col(1, 0x14);", code);
        Assert.Contains("pal_col(2, 0x20);", code);
        Assert.Contains("pal_col(3, 0x30);", code);
        Assert.Contains("vram_adr(NTADR_A(2, 2));", code);
        Assert.Contains("vram_write(\"HELLO, .NET!\");", code);
        Assert.Contains("ppu_on_all();", code);
        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Hello_GeneratesCsproj()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile(); // Must decompile first to populate state

        var csproj = decompiler.GenerateCsproj("hello");

        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.Contains("<Using Include=\"NES\" />", csproj);
        Assert.Contains("dotnes", csproj);
        // hello uses default settings so no mapper/prg/chr overrides
        Assert.DoesNotContain("NESMapper", csproj);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversPalBgData()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // The attributetable sample uses pal_bg with a 16-byte palette
        Assert.Contains("pal_bg(new byte[] {", code);
        // Verify specific palette values from the sample's PALETTE array
        Assert.Contains("0x03", code);
        Assert.Contains("0x11, 0x30, 0x27", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoversPalBgAndPalSprData()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses both pal_bg and pal_spr with inline byte arrays
        Assert.Contains("pal_bg(new byte[] {", code);
        Assert.Contains("pal_spr(new byte[] {", code);
        // Verify pal_bg palette data: 0x0F, 0x30, 0x10, 0x00 repeated
        Assert.Contains("0x0F, 0x30, 0x10, 0x00", code);
        // Verify pal_spr contains sprite palette data
        Assert.Contains("0x0F, 0x30, 0x10, 0x20", code);
    }

    [Fact]
    public void Decompiler_Statusbar_CsprojHasVerticalMirroring()
    {
        var romBytes = GetVerifiedRom("statusbar");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var csproj = decompiler.GenerateCsproj("statusbar");

        Assert.Contains("<NESVerticalMirroring>true</NESVerticalMirroring>", csproj);
    }

    static byte[] GetVerifiedRom(string name)
    {
        // Verified ROM binaries are stored alongside the test source files
        var path = Path.Combine(FindTestSourceDirectory(), $"TranspilerTests.Write.{name}.verified.bin");
        return File.ReadAllBytes(path);
    }

    static string FindTestSourceDirectory()
    {
        // Navigate from the test output directory to the source directory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "dotnes.tests");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "TranspilerTests.cs")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find test source directory");
    }
}
