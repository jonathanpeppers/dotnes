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
        if (expected.Length != actual.Length)
        {
            var hexDump = new StringBuilder();
            hexDump.AppendLine($"Expected {expected.Length} bytes but got {actual.Length} bytes.");
            hexDump.AppendLine("Actual bytes:");
            for (int i = 0; i < actual.Length; i++)
            {
                hexDump.Append($"{actual[i]:X2}");
                if ((i + 1) % 16 == 0) hexDump.AppendLine();
            }
            Assert.Fail(hexDump.ToString());
        }

        var builder = new StringBuilder();
        for (int i = 0; i < actual.Length; i++)
        {
            if (expected[i] != actual[i])
                builder.AppendLine($"Index: {i}, Expected: {expected[i]:X2}, Actual: {actual[i]:X2}");
        }
        if (builder.Length > 0)
        {
            var hexDump = new StringBuilder();
            hexDump.AppendLine("Actual bytes:");
            for (int i = 0; i < actual.Length; i++)
            {
                hexDump.Append($"{actual[i]:X2}");
                if ((i + 1) % 16 == 0) hexDump.AppendLine();
            }
            throw new Exception($"{builder}{hexDump}");
        }
    }
}
