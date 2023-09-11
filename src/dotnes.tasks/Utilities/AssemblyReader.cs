namespace dotnes;

/// <summary>
/// Right now this class only reads chr_generic.s files
/// </summary>
public class AssemblyReader : IDisposable
{
    const string SegmentInstruction = ".segment ";
    const string ByteInstruction = ".byte ";
    TextReader reader;

    public AssemblyReader(TextReader reader)
    {
        Source = reader.GetType().ToString();
        this.reader = reader;
    }

    public AssemblyReader(string source)
    {
        Source = source;
        reader = new StringReader(source);
    }

    public string Source { get; set; }

    public IEnumerable<Segment> GetSegments()
    {
        string? name = null;
        var bytes = new List<byte>();
        string line;
        do
        {
            line = reader.ReadLine();

            // Blank or comments
            if (string.IsNullOrEmpty(line) || line[0] == ';')
                continue;

            // .segment "CHARS"
            if (line.StartsWith(SegmentInstruction, StringComparison.Ordinal))
            {
                // Return the last segment
                if (name != null && bytes.Count > 0)
                    yield return new Segment(name, bytes.ToArray());

                name = line.Substring(SegmentInstruction.Length + 1, line.Length - SegmentInstruction.Length - 2);
                bytes.Clear();
                continue;
            }

            // .byte $00,$00,$00,$00,$00,$00,$00,$00
            if (line.StartsWith(ByteInstruction, StringComparison.Ordinal))
            {
                var span = line.AsSpan().Slice(ByteInstruction.Length);
                for (int i = 0; i < span.Length; i += 4)
                {
                    byte firstNibble = ToHexValue(span[i + 1]);
                    byte secondNibble = ToHexValue(span[i + 2]);
                    bytes.Add((byte)(firstNibble << 4 | secondNibble));
                }
            }

        } while (line != null);

        if (name != null && bytes.Count > 0)
            yield return new Segment(name, bytes.ToArray());
    }

    static byte ToHexValue(char hex)
    {
        return (byte)(hex - (hex < 58 ? 48 : (hex < 97 ? 55 : 87)));
    }

    public void Dispose() => reader.Dispose();
}

/// <summary>
/// Represents a binary segment
/// </summary>
public record Segment (string Name, byte[] Bytes);
