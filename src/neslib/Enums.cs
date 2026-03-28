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
/// Return value of <see cref="NESLib.ppu_system()"/>
/// </summary>
public enum VideoSystem : byte
{
    PAL = 0x00,
    NTSC = 0x80,
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
