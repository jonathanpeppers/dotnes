namespace NES;

/// <summary>
/// Return value of <see cref="NESLib.pad_poll(byte)"/>
/// </summary>
public enum PAD : byte
{
    A = 0x01,
    B = 0x02,
    SELECT = 0x04,
    START = 0x08,
    UP = 0x10,
    DOWN = 0x20,
    LEFT = 0x40,
    RIGHT = 0x80,
}

/// <summary>
/// PPU mask bits for <see cref="NESLib.ppu_mask(byte)"/>
/// </summary>
public static class MASK
{
    public const byte MONO = 0x01;
    public const byte EDGE_BG = 0x02;
    public const byte EDGE_SPR = 0x04;
    public const byte BG = 0x08;
    public const byte SPR = 0x10;
    public const byte TINT_RED = 0x20;
    public const byte TINT_BLUE = 0x40;
    public const byte TINT_GREEN = 0x80;
}
