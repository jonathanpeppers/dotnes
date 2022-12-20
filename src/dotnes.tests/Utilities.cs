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
        text = text.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");

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
