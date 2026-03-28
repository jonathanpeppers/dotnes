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
/// VRAM auto-increment mode for <see cref="NESLib.vram_inc(VramIncrement)"/>
/// </summary>
public enum VramIncrement : byte
{
    /// <summary>
    /// Increment VRAM address by 1 after each write (horizontal)
    /// </summary>
    By1 = 0,

    /// <summary>
    /// Increment VRAM address by 32 after each write (vertical)
    /// </summary>
    By32 = 1,
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

/// <summary>
/// Sprite attribute flags for <see cref="NESLib.oam_spr(byte, byte, byte, byte, byte)"/>
/// </summary>
public static class OAM
{
    public const byte FLIP_V = 0x80;
    public const byte FLIP_H = 0x40;
    public const byte BEHIND = 0x20;
}

/// <summary>
/// MMC1 mirroring modes (bits 0-1 of the Control register).
/// Cast to <c>byte</c> and OR with PRG/CHR mode bits for <see cref="NESLib.mmc1_set_mirroring(byte)"/>.
/// </summary>
public enum MMC1Mirror : byte
{
    OneLower = 0,
    OneUpper = 1,
    Vertical = 2,
    Horizontal = 3,
}
