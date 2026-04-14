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
/// Sprite size for <see cref="NESLib.oam_size(SpriteSize)"/>
/// </summary>
public enum SpriteSize : byte
{
    Size8x8 = 0,
    Size8x16 = 1,
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

/// <summary>
/// Named constants for the NES hardware palette (2C02 PPU).
/// Use with <see cref="NESLib.pal_col(byte, byte)"/>,
/// <see cref="NESLib.pal_bg(byte[])"/>, <see cref="NESLib.pal_all(byte[])"/>, etc.
/// <para>
/// The NES PPU has a fixed 64-entry palette organized in 4 brightness rows
/// and 16 hue columns. This class covers the 54 usable colors ($00–$0D,
/// $0F–$1C, $20–$2D, $30–$3D); columns $E/$F are black duplicates and omitted.
/// </para>
/// <seealso href="https://www.nesdev.org/wiki/PPU_palettes"/>
/// </summary>
public static class NESColor
{
    // Row 0 ($0x) — darkest shades
    public const byte DarkGray    = 0x00;
    public const byte DarkAzure   = 0x01;
    public const byte DarkBlue    = 0x02;
    public const byte DarkViolet  = 0x03;
    public const byte DarkMagenta = 0x04;
    public const byte DarkRose    = 0x05;
    public const byte DarkRed     = 0x06;
    public const byte Brown       = 0x07;
    public const byte Olive       = 0x08;
    public const byte DarkLime    = 0x09;
    public const byte DarkGreen   = 0x0A;
    public const byte DarkTeal    = 0x0B;
    public const byte DarkCyan    = 0x0C;
    /// <summary>
    /// Black ($0F). Preferred over $0D for screen backgrounds because $0D may
    /// produce "blacker-than-black" NTSC output on real hardware.
    /// </summary>
    public const byte Black       = 0x0F;

    // Row 1 ($1x) — medium shades
    public const byte Gray    = 0x10;
    public const byte Azure   = 0x11;
    public const byte Blue    = 0x12;
    public const byte Violet  = 0x13;
    public const byte Magenta = 0x14;
    public const byte Rose    = 0x15;
    public const byte Red     = 0x16;
    public const byte Orange  = 0x17;
    public const byte Gold    = 0x18;
    public const byte Lime    = 0x19;
    public const byte Green   = 0x1A;
    public const byte Teal    = 0x1B;
    public const byte Cyan    = 0x1C;

    // Row 2 ($2x) — light shades
    public const byte LightGray    = 0x20;
    public const byte LightAzure   = 0x21;
    public const byte LightBlue    = 0x22;
    public const byte LightViolet  = 0x23;
    public const byte LightMagenta = 0x24;
    public const byte LightRose    = 0x25;
    public const byte LightRed     = 0x26;
    public const byte LightOrange  = 0x27;
    public const byte Yellow       = 0x28;
    public const byte LightLime    = 0x29;
    public const byte LightGreen   = 0x2A;
    public const byte LightTeal    = 0x2B;
    public const byte LightCyan    = 0x2C;
    public const byte MediumGray   = 0x2D;

    // Row 3 ($3x) — palest / lightest shades
    public const byte White       = 0x30;
    public const byte PaleAzure   = 0x31;
    public const byte PaleBlue    = 0x32;
    public const byte PaleViolet  = 0x33;
    public const byte PaleMagenta = 0x34;
    public const byte PaleRose    = 0x35;
    public const byte PaleRed     = 0x36;
    public const byte PaleOrange  = 0x37;
    public const byte PaleYellow  = 0x38;
    public const byte PaleLime    = 0x39;
    public const byte PaleGreen   = 0x3A;
    public const byte PaleTeal    = 0x3B;
    public const byte PaleCyan    = 0x3C;
    public const byte Silver      = 0x3D;
}
