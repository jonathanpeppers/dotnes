using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Reads 6502 assembly (.s) files. Supports:
/// - CHR ROM segments (.segment "CHARS" + .byte data)
/// - Code blocks with labels (label: + .byte data)
/// </summary>
public class AssemblyReader : IDisposable
{
    const string SegmentInstruction = ".segment ";
    const string ByteInstruction = ".byte ";
    readonly TextReader reader;

    public AssemblyReader(TextReader reader)
    {
        Path = reader.GetType().ToString();
        this.reader = reader;
    }

    public AssemblyReader(string path)
    {
        Path = path;
        reader = new StreamReader(File.OpenRead(path));
    }

    /// <summary>
    /// File path or TextReader type used
    /// </summary>
    public string Path { get; }

    public IEnumerable<Segment> GetSegments()
    {
        string? name = null;
        var bytes = new List<byte>();
        string line;
        do
        {
            line = reader.ReadLine()!;

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
                ParseByteInstruction(line, bytes);
            }

        } while (line != null);

        if (name != null && bytes.Count > 0)
            yield return new Segment(name, bytes.ToArray());
    }

    /// <summary>
    /// Parses labeled code blocks from a .s file.
    /// Labels are lines ending with ':' (e.g., _famitone_init:).
    /// Code is represented as .byte directives containing machine code.
    /// Returns Block objects suitable for inclusion in Program6502.
    /// </summary>
    public static IEnumerable<Block> GetCodeBlocks(string path)
    {
        using var reader = new StreamReader(File.OpenRead(path));
        foreach (var block in GetCodeBlocks(reader))
            yield return block;
    }

    /// <summary>
    /// Parses labeled code blocks from a TextReader.
    /// </summary>
    public static IEnumerable<Block> GetCodeBlocks(TextReader reader)
    {
        string? currentLabel = null;
        var bytes = new List<byte>();
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            // Skip blank lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";"))
                continue;

            var trimmed = line.Trim();

            // Skip .segment directives
            if (trimmed.StartsWith(SegmentInstruction, StringComparison.Ordinal))
                continue;

            // Label: line ending with ':' (e.g., _famitone_init:)
            if (trimmed.EndsWith(":") && !trimmed.StartsWith("."))
            {
                // Yield previous block if any
                if (currentLabel != null && bytes.Count > 0)
                {
                    yield return Block.FromRawData(bytes.ToArray(), currentLabel);
                    bytes.Clear();
                }

                currentLabel = trimmed.Substring(0, trimmed.Length - 1);
                continue;
            }

            // .byte data
            if (trimmed.StartsWith(ByteInstruction, StringComparison.Ordinal))
            {
                ParseByteInstruction(trimmed, bytes);
            }
        }

        // Yield final block
        if (currentLabel != null && bytes.Count > 0)
            yield return Block.FromRawData(bytes.ToArray(), currentLabel);
    }

    static void ParseByteInstruction(string line, List<byte> bytes)
    {
        var span = line.AsSpan().Slice(ByteInstruction.Length);
        for (int i = 0; i < span.Length; i += 4)
        {
            byte firstNibble = ToHexValue(span[i + 1]);
            byte secondNibble = ToHexValue(span[i + 2]);
            bytes.Add((byte)(firstNibble << 4 | secondNibble));
        }
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
