namespace dotnes.tests;

public class NESWriterTests
{
    readonly byte[] data;
    readonly MemoryStream stream = new MemoryStream();

    public NESWriterTests()
    {
        using var s = GetType().Assembly.GetManifestResourceStream("dotnes.tests.Data.hello.nes");
        if (s == null)
            throw new Exception("Cannot load hello.nes!");
        data = new byte[s.Length];
        s.Read(data, 0, data.Length);
    }

    NESWriter GetWriter(byte[]? PRG_ROM = null, byte[]? CHR_ROM = null)
    {
        stream.SetLength(0);

        return new NESWriter(stream, leaveOpen: true)
        {
            PRG_ROM = PRG_ROM,
            CHR_ROM = CHR_ROM,
        };
    }

    void AssertInstructions(string assembly)
    {
        var expected = toByteArray(assembly.Replace(" ", ""));
        var actual = stream.ToArray();
        Assert.Equal(expected, actual);

        static byte[] toByteArray(string text)
        {
            int length = text.Length >> 1;
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)((toHex(text[i << 1]) << 4) + (toHex(text[(i << 1) + 1])));
            }
            return bytes;
        }

        static int toHex(char ch)
        {
            int value = (int)ch;
            return value - (value < 58 ? 48 : (value < 97 ? 55 : 87));
        }
    }

    [Fact]
    public void WriteHeader()
    {
        using var writer = GetWriter(new byte[2 * 16384], new byte[1 * 8192]);
        writer.WriteHeader();
        writer.Flush();

        var actual = stream.ToArray();
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(data[i], actual[i]);
        }
    }

    [Fact]
    public void WriteFullROM()
    {
        using var writer = GetWriter(new byte[2 * 16384], new byte[1 * 8192]);

        Array.Copy(data, 16, writer.PRG_ROM!, 0, 2 * 16384);
        Array.Copy(data, 16 + 2 * 16384, writer.CHR_ROM!, 0, 8192);

        writer.Write();
        writer.Flush();

        Assert.Equal(data, stream.ToArray());
    }

    [Fact]
    public void WriteLDA()
    {
        using var r = GetWriter();
        r.Write(Instruction.LDA, 0x08);
        r.Flush();

        // 8076	A908          	LDA #$08  
        AssertInstructions("A908");
    }

    [Fact]
    public void WriteJSR()
    {
        using var r = GetWriter();
        r.Write(Instruction.JSR, 0x84F4);
        r.Flush();

        // 807A	20F484        	JSR initlib
        AssertInstructions("20F484");
    }

    [Fact]
    public void Write_pal_all()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_all));
        r.Flush();
        AssertInstructions("8517 8618 A200 A920");
    }

    [Fact]
    public void Write_pal_copy()
    {
        using var r = GetWriter();
        r.WriteBuiltIn("pal_copy");
        r.Flush();
        AssertInstructions("8519 A000");
    }

    [Fact]
    public void Write_pal_bg()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_bg));
        r.Flush();
        AssertInstructions("8517 8618 A200 A910 D0E4");
    }

    [Fact]
    public void Write_pal_spr()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_spr));
        r.Flush();
        AssertInstructions("8517 8618 A210 8A D0DB");
    }

    [Fact]
    public void Write_pal_col()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.pal_col));
        r.Flush();
        AssertInstructions("8517 205085 291F AA A517 9DC001 E607 60");
    }

    [Fact]
    public void Write_vram_adr()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.vram_adr));
        r.Flush();
        AssertInstructions("8E0620 8D0620 60");
    }

    [Fact]
    public void Write_vram_write()
    {
        using var r = GetWriter();
        r.WriteBuiltIn(nameof(NESLib.vram_write));
        r.Flush();
        AssertInstructions("8517 8618 203A85 8519 861A A000 B119 8D0720 E619 D002 E61A A517 D002 C618 C617 A517 0518 D0E7 60");
    }
}