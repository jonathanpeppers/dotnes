using System.Text;

namespace dotnes.tests;

static class AssertEx
{
    public static void Equal(byte[] expected, NESWriter writer)
    {
        writer.Flush();
        Equal(expected, ((MemoryStream)writer.BaseStream).ToArray());
    }

    public static void Equal(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        var builder = new StringBuilder();
        for (int i = 0; i < actual.Length; i++)
        {
            if (expected[i] != actual[i])
                builder.AppendLine($"Index: {i:X}, Expected: {expected[i]:X}, Actual: {actual[i]:X}");
        }
        if (builder.Length > 0)
            throw new Exception(builder.ToString());
    }
}
