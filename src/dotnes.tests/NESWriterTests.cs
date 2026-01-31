using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NESWriterTests
{
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public NESWriterTests(ITestOutputHelper output)
    {
        _logger = new XUnitLogger(output);
    }

    NESWriter GetWriter()
    {
        _stream.SetLength(0);

        return new NESWriter(_stream, leaveOpen: true, logger: _logger);
    }

    void AssertInstructions(string assembly)
    {
        var expected = Utilities.ToByteArray(assembly);
        var actual = _stream.ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Write_pal_all()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalAll());
        writer.Flush();
        AssertInstructions("8517 8618 A200 A920");
    }

    [Fact]
    public void Write_pal_copy()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalCopy());
        writer.Flush();
        AssertInstructions("8519 A000 B117 9DC001 E8 C8 C619 D0F5 E607 60");
    }

    [Fact]
    public void Write_pal_bg()
    {
        using var writer = GetWriter();
        // pal_copy label must be set for the BNE branch to resolve
        // BNE at offset 8 needs relative offset 0xE4 (-28) -> target = 0x800A - 28 = 0x7FEE
        writer.Labels["pal_copy"] = 0x7FEE;
        writer.WriteBlock(BuiltInSubroutines.PalBg());
        writer.Flush();
        AssertInstructions("8517 8618 A200 A910 D0E4");
    }

    [Fact]
    public void Write_pal_spr()
    {
        using var writer = GetWriter();
        // pal_copy label must be set for the BNE branch to resolve
        // BNE at offset 7 needs relative offset 0xDB (-37) -> target = 0x8009 - 37 = 0x7FE4
        writer.Labels["pal_copy"] = 0x7FE4;
        writer.WriteBlock(BuiltInSubroutines.PalSpr());
        writer.Flush();
        AssertInstructions("8517 8618 A210 8A D0DB");
    }

    [Fact]
    public void Write_pal_col()
    {
        using var writer = GetWriter();
        writer.Labels["popa"] = 0x854F + 0x43;
        writer.WriteBlock(BuiltInSubroutines.PalCol());
        writer.Flush();
        AssertInstructions("8517 209285 291F AA A517 9DC001 E607 60");
    }

    [Fact]
    public void Write_pal_clear()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PalClear());
        writer.Flush();
        AssertInstructions("A90F A200 9DC001 E8 E020 D0F8 8607 60");
    }

    [Fact]
    public void Write_vram_adr()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.VramAdr());
        writer.Flush();
        AssertInstructions("8E0620 8D0620 60");
    }

    [Fact]
    public void Write_vram_write()
    {
        using var writer = GetWriter();
        // vram_write needs popax label - address derived from expected output "207C85" = JSR $857C
        writer.Labels["popax"] = 0x857C;
        writer.WriteBlock(BuiltInSubroutines.VramWrite());
        writer.Flush();
        AssertInstructions("8517 8618 207C85 8519 861A A000 B119 8D0720 E619 D002 E61A A517 D002 C618 C617 A517 0518 D0E7 60");
    }

    [Fact]
    public void Write_ppu_on_all()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuOnAll());
        writer.Flush();
        AssertInstructions("A512 0918");
    }

    [Fact]
    public void Write_ppu_onoff()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuOnOff());
        writer.Flush();
        AssertInstructions("8512 4CF082");
    }

    [Fact]
    public void Write_ppu_on_bg()
    {
        using var writer = GetWriter();
        // ppu_on_bg branches backward to ppu_onoff with offset -11 (0xF5)
        // At stream position 0 (address 0x8000), BNE is at address 0x8004
        // Target = 0x8004 + 2 - 11 = 0x7FFB
        writer.Labels["ppu_onoff"] = 0x7FFB;
        writer.WriteBlock(BuiltInSubroutines.PpuOnBg());
        writer.Flush();
        AssertInstructions("A512 0908 D0F5");
    }

    [Fact]
    public void Write_ppu_wait_nmi()
    {
        using var writer = GetWriter();
        writer.WriteBlock(BuiltInSubroutines.PpuWaitNmi());
        writer.Flush();
        AssertInstructions("A901 8503 A501 C501 F0FC 60");
    }
}