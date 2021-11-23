using System.Text;

namespace dotnes;

/// <summary>
/// Writes .nes files
/// * https://wiki.nesdev.org/w/index.php/INES
/// * https://bheisler.github.io/post/nes-rom-parser-with-nom/
/// </summary>
class NESWriter : IDisposable
{
    readonly BinaryWriter _writer;

    public NESWriter(Stream stream) => _writer = new BinaryWriter(stream, Encoding.ASCII);

    /// <summary>
    /// Size of PRG ROM in 16 KB units
    /// </summary>
    public byte PRG_ROM { get; set; }

    /// <summary>
    /// Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
    /// </summary>
    public byte CHR_ROM { get; set; }

    /// <summary>
    /// Mapper, mirroring, battery, trainer
    /// </summary>
    public byte Flags6 { get; set; }

    /// <summary>
    /// Mapper, VS/Playchoice, NES 2.0
    /// </summary>
    public byte Flags7 { get; set; }

    /// <summary>
    /// PRG-RAM size (rarely used extension)
    /// </summary>
    public byte Flags8 { get; set; }

    /// <summary>
    /// TV system (rarely used extension)
    /// </summary>
    public byte Flags9 { get; set; }

    /// <summary>
    /// TV system, PRG-RAM presence (unofficial, rarely used extension)
    /// </summary>
    public byte Flags10 { get; set; }

    public void WriteHeader()
    {
        _writer.Write('N');
        _writer.Write('E');
        _writer.Write('S');
        _writer.Write('\x1A');
        _writer.Write(PRG_ROM);
        _writer.Write(CHR_ROM);
        _writer.Write(Flags6);
        _writer.Write(Flags7);
        _writer.Write(Flags8);
        _writer.Write(Flags9);
        _writer.Write(Flags10);
        // 5 bytes of padding
        for (int i = 0; i < 5; i++)
        {
            _writer.Write((byte)0);
        }
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
