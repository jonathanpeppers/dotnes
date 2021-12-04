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

    public NESWriter(Stream stream, bool leaveOpen = false) => _writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen);

    /// <summary>
    /// Trainer, if present (0 or 512 bytes)
    /// </summary>
    public byte[]? Trainer { get; set; }

    /// <summary>
    /// PRG ROM data (16384 * x bytes)
    /// </summary>
    public byte[]? PRG_ROM { get; set; }

    /// <summary>
    /// CHR ROM data, if present (8192 * y bytes)
    /// </summary>
    public byte[]? CHR_ROM { get; set; }

    /// <summary>
    /// PlayChoice INST-ROM, if present (0 or 8192 bytes)
    /// </summary>
    public byte[]? INST_ROM { get; set; }

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
        // Size of PRG ROM in 16 KB units
        _writer.Write(checked ((byte)(PRG_ROM.Length / 16384)));
        // Size of CHR ROM in 8 KB units (Value 0 means the board uses CHR RAM)
        if (CHR_ROM != null)
            _writer.Write(checked((byte)(CHR_ROM.Length / 8192)));
        else
            _writer.Write((byte)0);
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

    /// <summary>
    /// Writes a built-in method from NESLib
    /// </summary>
    public void WriteBuiltIn(string name)
    {
        switch (name)
        {
            case nameof(NESLib.pal_all):
                /*
                 * 8211	8517          	STA TEMP                      ; _pal_all
                 * 8213	8618          	STX TEMP+1                    
                 * 8215	A200          	LDX #$00                      
                 * 8217	A920          	LDA #$20                      
                 */
                STA(0x17);
                STX(0x18);
                LDX(0x00);
                LDA(0x20);
                break;
            default:
                throw new NotImplementedException($"{name} is not implemented!");
        }
    }

    /// <summary>
    /// Store Accumulator in Memory
    /// </summary>
    public void STA(byte n)
    {
        _writer.Write((byte)Instruction.STA_zpg);
        _writer.Write(n);
    }

    /// <summary>
    /// Store Index X in Memory
    /// </summary>
    public void STX(byte n)
    {
        _writer.Write((byte)Instruction.STX_zpg);
        _writer.Write(n);
    }

    /// <summary>
    /// Load Accumulator with Memory
    /// </summary>
    public void LDA(byte n)
    {
        _writer.Write((byte)Instruction.LDA);
        _writer.Write(n);
    }

    /// <summary>
    /// Load Index X with Memory
    /// </summary>
    public void LDX(byte n)
    {
        _writer.Write((byte)Instruction.LDX);
        _writer.Write(n);
    }

    /// <summary>
    /// Jump to New Location Saving Return Address
    /// </summary>
    public void JSR(ushort address)
    {
        _writer.Write((byte)Instruction.JSR);
        _writer.Write(address);
    }

    public void Write()
    {
        WriteHeader();
        if (PRG_ROM != null)
            _writer.Write(PRG_ROM);
        if (CHR_ROM != null)
            _writer.Write(CHR_ROM);
        if (Trainer != null)
            _writer.Write(Trainer);
        if (INST_ROM != null)
            _writer.Write(INST_ROM);
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
