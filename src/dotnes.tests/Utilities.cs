using System.Text;

namespace dotnes.tests;

class Utilities
{
    public static Stream GetResource(string name)
    {
        var stream = typeof(Utilities).Assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException($"Cannot load {name}!");
        return stream;
    }

    public static byte[] ToByteArray(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder();
        using var reader = new StringReader(text);
        while (reader.Peek() != -1)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            int comment = line.IndexOf(';');
            if (comment != -1)
            {
                line = line[..comment];
            }
            builder.Append(line.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", ""));
        }

        text = builder.ToString();
        int length = text.Length >> 1;
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((ToHex(text[i << 1]) << 4) + (ToHex(text[(i << 1) + 1])));
        }
        return bytes;
    }

    public static int ToHex(char ch)
    {
        int value = ch;
        return value - (value < 58 ? 48 : (value < 97 ? 55 : 87));
    }
}
