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
/// PPU mask bits for <see cref="NESLib.ppu_mask(MASK)"/>
/// </summary>
[Flags]
public enum MASK : byte
{
    MONO = 0x01,
    EDGE_BG = 0x02,
    EDGE_SPR = 0x04,
    BG = 0x08,
    SPR = 0x10,
    TINT_RED = 0x20,
    TINT_GREEN = 0x40,
    TINT_BLUE = 0x80,
}

/// <summary>
/// Sprite attribute flags for <see cref="NESLib.oam_spr(byte, byte, byte, byte, byte)"/>
/// </summary>
public static class OAM
{
    public const byte FLIP_V = 0x80;
    public const byte FLIP_H = 0x40;
    public const byte BEHIND = 0x20;
}
