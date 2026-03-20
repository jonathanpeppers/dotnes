using System.Text;

namespace dotnes;

/// <summary>
/// Reads and parses a .nes ROM file (iNES format).
/// Extracts header info, PRG ROM, CHR ROM, and interrupt vectors.
/// </summary>
class NESRomReader
{
    const int HeaderSize = 16;
    const int PrgBankSize = 16384; // 16 KB
    const int ChrBankSize = 8192;  // 8 KB

    public int PrgBanks { get; }
    public int ChrBanks { get; }
    public int Mapper { get; }
    public bool VerticalMirroring { get; }
    public bool HasBattery { get; }

    public byte[] PrgRom { get; }
    public byte[] ChrRom { get; }

    public ushort NmiVector { get; }
    public ushort ResetVector { get; }
    public ushort IrqVector { get; }

    public NESRomReader(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        // Read and validate iNES header
        byte n = reader.ReadByte();
        byte e = reader.ReadByte();
        byte s = reader.ReadByte();
        byte magic = reader.ReadByte();

        if (n != 'N' || e != 'E' || s != 'S' || magic != 0x1A)
            throw new InvalidOperationException("Not a valid iNES ROM file.");

        PrgBanks = reader.ReadByte();
        ChrBanks = reader.ReadByte();

        byte flags6 = reader.ReadByte();
        byte flags7 = reader.ReadByte();

        VerticalMirroring = (flags6 & 0x01) != 0;
        HasBattery = (flags6 & 0x02) != 0;
        Mapper = ((flags6 >> 4) & 0x0F) | (flags7 & 0xF0);

        // Skip remaining header bytes (flags8-15)
        reader.ReadBytes(HeaderSize - 8);

        // Skip 512-byte trainer if present (flags6 bit 2)
        if ((flags6 & 0x04) != 0)
            reader.ReadBytes(512);

        // Read PRG ROM
        int prgSize = PrgBanks * PrgBankSize;
        PrgRom = reader.ReadBytes(prgSize);
        if (PrgRom.Length != prgSize)
            throw new InvalidOperationException($"Truncated ROM: expected {prgSize} PRG bytes, got {PrgRom.Length}.");

        // Read CHR ROM (if present)
        int chrSize = ChrBanks * ChrBankSize;
        ChrRom = chrSize > 0 ? reader.ReadBytes(chrSize) : Array.Empty<byte>();
        if (chrSize > 0 && ChrRom.Length != chrSize)
            throw new InvalidOperationException($"Truncated ROM: expected {chrSize} CHR bytes, got {ChrRom.Length}.");

        // Read interrupt vectors from last 6 bytes of PRG ROM
        if (PrgRom.Length >= 6)
        {
            int vectorBase = PrgRom.Length - 6;
            NmiVector = (ushort)(PrgRom[vectorBase] | (PrgRom[vectorBase + 1] << 8));
            ResetVector = (ushort)(PrgRom[vectorBase + 2] | (PrgRom[vectorBase + 3] << 8));
            IrqVector = (ushort)(PrgRom[vectorBase + 4] | (PrgRom[vectorBase + 5] << 8));
        }
    }

    public NESRomReader(byte[] rom) : this(new MemoryStream(rom)) { }

    /// <summary>
    /// Generates a ca65-format .s file for the CHR ROM data.
    /// When bank is specified, only that 8KB bank is emitted.
    /// </summary>
    public string GenerateChrAssembly(int? bank = null)
    {
        if (ChrRom.Length == 0)
            return string.Empty;

        int start, length;
        if (bank.HasValue)
        {
            if (bank.Value < 0 || bank.Value >= ChrBanks)
                return string.Empty;
            start = bank.Value * ChrBankSize;
            length = Math.Min(ChrBankSize, ChrRom.Length - start);
        }
        else
        {
            start = 0;
            length = ChrRom.Length;
        }

        return GenerateChrAssemblyFromData(ChrRom, start, length);
    }

    /// <summary>
    /// Generates a ca65-format .s file from arbitrary tile data bytes.
    /// </summary>
    public static string GenerateChrAssemblyFromData(byte[] data, int start, int length)
    {
        if (length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(".segment \"CHARS\"");

        // Output as .byte directives, 16 bytes per line (one tile row)
        for (int i = 0; i < length; i += 16)
        {
            sb.Append(".byte ");
            int count = Math.Min(16, length - i);
            for (int j = 0; j < count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append($"${data[start + i + j]:X2}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
