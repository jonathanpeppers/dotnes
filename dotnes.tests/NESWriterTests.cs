namespace dotnes.tests;

public class NESWriterTests
{
    readonly byte[] data;

    public NESWriterTests()
    {
        using var s = GetType().Assembly.GetManifestResourceStream("dotnes.tests.Data.hello.nes");
        if (s == null)
            throw new Exception("Cannot load hello.nes!");
        data = new byte[s.Length];
        s.Read(data, 0, data.Length);
    }

    [Fact]
    public void WriteHeader()
    {
        using var s = new MemoryStream();
        using var r = new NESWriter(s)
        {
            PRG_ROM = new byte[2 * 16384],
            CHR_ROM = new byte[1 * 8192],
        };
        r.WriteHeader();
        r.Flush();

        var actual = s.ToArray();
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(data[i], actual[i]);
        }
    }

    [Fact]
    public void Write()
    {
        using var s = new MemoryStream();
        using var r = new NESWriter(s)
        {
            PRG_ROM = new byte[2 * 16384],
            CHR_ROM = new byte[1 * 8192],
        };

        Array.Copy(data, 16, r.PRG_ROM, 0, 2 * 16384);
        Array.Copy(data, 16 + 2 * 16384, r.CHR_ROM, 0, 8192);

        r.Write();
        r.Flush();

        Assert.Equal(data, s.ToArray());
    }
}