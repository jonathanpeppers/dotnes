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
            PRG_ROM = 2,
            CHR_ROM = 1,
        };
        r.WriteHeader();
        r.Flush();

        var actual = s.ToArray();
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(data[i], actual[i]);
        }
    }
}