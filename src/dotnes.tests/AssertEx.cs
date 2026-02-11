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
            // Print the actual bytes as hex for easier test updating
            var hexDump = new StringBuilder();
            hexDump.AppendLine($"Expected {expected.Length} bytes but got {actual.Length} bytes.");
            hexDump.AppendLine("Actual bytes (for updating test):");
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
            {
                // Uncomment to debug further
                //builder.AppendLine($"Prev:  {i - 1:X}, Expected: {expected[i - 1]:X}, Actual: {actual[i - 1]:X}");
                builder.AppendLine($"Index: {i:X}, Expected: {expected[i]:X}, Actual: {actual[i]:X}");
                //builder.AppendLine($"Next:  {i + 1:X}, Expected: {expected[i + 1]:X}, Actual: {actual[i + 1]:X}");
            }
        }
        if (builder.Length > 0)
            throw new Exception(builder.ToString());
    }
}
