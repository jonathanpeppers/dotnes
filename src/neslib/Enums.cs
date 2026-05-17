namespace NES;

/// <summary>
/// Return value of <see cref="NESLib.pad_poll(byte)"/>
/// </summary>
public enum PAD : byte
{
    /// <summary>
    /// A flag (bit 0) with value 0x01.
    /// </summary>
    A = 0x01,
    /// <summary>
    /// B flag (bit 1) with value 0x02.
    /// </summary>
    B = 0x02,

    /// <summary>
    /// SELECT flag (bit 2) with value 0x04.
    /// </summary>
    SELECT = 0x04,
    /// <summary>
    /// START flag (bit 3) with value 0x08.
    /// </summary>
    START = 0x08,
    /// <summary>
    /// UP flag (bit 4) with value 0x10.
    /// </summary>
    UP = 0x10,

    /// <summary>
    /// DOWN flag (bit 5) with value 0x20.
    /// </summary>
    DOWN = 0x20,

    /// <summary>
    /// LEFT flag (bit 6) with value 0x40.
    /// </summary>
    LEFT = 0x40,

    /// <summary>
    /// RIGHT flag (bit 7) with value 0x80.
    /// </summary>
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
    /// <summary>
    /// PAL / Dendy (50Hz, 312 scanlines/frame, 341 PPU cycles/scanline)
    /// </summary>
    PAL = 0x00,

    /// <summary>
    /// NTSC (60Hz, 262 scanlines/frame, 341 PPU cycles/scanline)
    /// </summary>
    NTSC = 0x80,
}

/// <summary>
/// PPU mask bits for <see cref="NESLib.ppu_mask(MASK)"/>
/// </summary>
[Flags]
public enum MASK : byte
{
    /// <summary>
    /// Greyscale (0: normal color, 1: greyscale)
    /// </summary>
    MONO = 0x01,

    /// <summary>
    /// Show background in leftmost 8 pixels of screen
    /// </summary>
    EDGE_BG = 0x02,

    /// <summary>
    /// Show sprites in leftmost 8 pixels of screen
    /// </summary>
    EDGE_SPR = 0x04,

    /// <summary>
    /// Enable background rendering
    /// </summary>
    BG = 0x08,

    /// <summary>
    /// Enable sprite rendering
    /// </summary>
    SPR = 0x10,

    /// <summary>
    /// Emphasize red (green on PAL/Dendy)
    /// </summary>
    TINT_RED = 0x20,

    /// <summary>
    /// Emphasize green (red on PAL/Dendy)
    /// </summary>
    TINT_GREEN = 0x40,

    /// <summary>
    /// Emphasize blue
    /// </summary>
    TINT_BLUE = 0x80,
}

/// <summary>
/// Sprite size for <see cref="NESLib.oam_size(SpriteSize)"/>
/// </summary>
public enum SpriteSize : byte
{
    /// <summary>
    /// Size of sprite is 8x8 pixels
    /// </summary>
    Size8x8 = 0,

    /// <summary>
    /// Size of sprite is 8x16 pixels
    /// </summary>
    Size8x16 = 1,
}

/// <summary>
/// Sprite attribute flags for <see cref="NESLib.oam_spr(byte, byte, byte, byte, byte)"/>
/// </summary>
public static class OAM
{
    /// <summary>
    /// Flip the sprite vertically
    /// </summary>
    public const byte FLIP_V = 0x80;
    /// <summary>
    /// Flip the sprite horizontally
    /// </summary>
    public const byte FLIP_H = 0x40;
    /// <summary>
    /// Place the sprite behind the background
    /// </summary>
    /// <remarks>The sprite will be behind the background, but in front of the universal background color (the very first bg palette entry)</remarks>
    public const byte BEHIND = 0x20;
}

/// <summary>
/// Pulse channel selector for <see cref="NESLib.apu_play_tone"/> and <see cref="NESLib.apu_stop"/>.
/// </summary>
public enum PulseChannel : byte
{
    /// <summary>
    /// Selects first pulse channel (square wave generator 1)
    /// </summary>
    Pulse1 = 0,

    /// <summary>
    /// Selects second pulse channel (square wave generator 2)
    /// </summary>
    Pulse2 = 1,
}

/// <summary>
/// Duty cycle for NES APU pulse channels.
/// Controls the waveform shape used by <see cref="NESLib.apu_play_tone"/>.
/// </summary>
public enum APUDuty : byte
{
    /// <summary>12.5% duty cycle (thin, buzzy)</summary>
    Duty12 = 0,
    /// <summary>25% duty cycle (hollow, reedy)</summary>
    Duty25 = 1,
    /// <summary>50% duty cycle (square wave, full)</summary>
    Duty50 = 2,
    /// <summary>75% duty cycle (same as 25% inverted)</summary>
    Duty75 = 3,
}

/// <summary>
/// MMC1 mirroring modes (bits 0-1 of the Control register).
/// Cast to <c>byte</c> and OR with PRG/CHR mode bits for <see cref="NESLib.mmc1_set_mirroring(byte)"/>.
/// </summary>
public enum MMC1Mirror : byte
{
    /// <summary>
    /// $2800 contains the nametable
    /// </summary>
    OneLower = 0,

    /// <summary>
    /// $2000 contains the nametable
    /// </summary>
    OneUpper = 1,

    /// <summary>
    /// $2000 and $2400 contain the first nametable, and $2800 and $2C00 contain the second nametable
    /// </summary>
    Vertical = 2,

    /// <summary>
    /// $2000 and $2800 contain the first nametable, and $2400 and $2C00 contain the second nametable
    /// </summary>
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

    /// <summary>
    /// Dark grey color.
    /// </summary>
    public const byte DarkGray    = 0x00;

    /// <summary>
    /// Dark azure color.
    /// </summary>
    public const byte DarkAzure   = 0x01;
    /// <summary>
    /// Dark blue color.
    /// </summary>
    public const byte DarkBlue    = 0x02;
    /// <summary>
    /// Dark violet color.
    /// </summary>
    public const byte DarkViolet  = 0x03;
    /// <summary>
    /// Dark magenta color.
    /// </summary>
    public const byte DarkMagenta = 0x04;
    /// <summary>
    /// Dark rose color.
    /// </summary>
    public const byte DarkRose    = 0x05;
    /// <summary>
    /// Dark red color.
    /// </summary>
    public const byte DarkRed     = 0x06;
    /// <summary>
    /// Brown color.
    /// </summary>
    public const byte Brown       = 0x07;
    /// <summary>
    /// Olive color.
    /// </summary>
    public const byte Olive       = 0x08;
    /// <summary>
    /// Dark lime color.
    /// </summary>
    public const byte DarkLime    = 0x09;
    /// <summary>
    /// Dark green color.
    /// </summary>
    public const byte DarkGreen   = 0x0A;
    /// <summary>
    /// Dark teal color.
    /// </summary>
    public const byte DarkTeal    = 0x0B;
    /// <summary>
    /// Dark cyan color.
    /// </summary>
    public const byte DarkCyan    = 0x0C;
    /// <summary>
    /// Black ($0F). Preferred over $0D for screen backgrounds because $0D may
    /// produce "blacker-than-black" NTSC output on real hardware.
    /// </summary>
    public const byte Black       = 0x0F;

    // Row 1 ($1x) — medium shades
    /// <summary>
    /// Gray color.
    /// </summary>
    public const byte Gray    = 0x10;
    /// <summary>
    /// Azure color.
    /// </summary>
    public const byte Azure   = 0x11;
    /// <summary>
    /// Blue color.
    /// </summary>
    public const byte Blue    = 0x12;
    /// <summary>
    /// Violet color.
    /// </summary>
    public const byte Violet  = 0x13;
    /// <summary>
    /// Magenta color.
    /// </summary>
    public const byte Magenta = 0x14;
    /// <summary>
    /// Rose color.
    /// </summary>
    public const byte Rose    = 0x15;
    /// <summary>
    /// Red color.
    /// </summary>
    public const byte Red     = 0x16;
    /// <summary>
    /// Orange color.
    /// </summary>
    public const byte Orange  = 0x17;
    /// <summary>
    /// Gold color.
    /// </summary>
    public const byte Gold    = 0x18;
    /// <summary>
    /// Lime color.
    /// </summary>
    public const byte Lime    = 0x19;
    /// <summary>
    /// Green color.
    /// </summary>
    public const byte Green   = 0x1A;
    /// <summary>
    /// Teal color.
    /// </summary>
    public const byte Teal    = 0x1B;
    /// <summary>
    /// Cyan color.
    /// </summary>
    public const byte Cyan    = 0x1C;

    // Row 2 ($2x) — light shades

    /// <summary>
    /// Light grey color.
    /// </summary>
    public const byte LightGray    = 0x20;
    /// <summary>
    /// Light azure color.
    /// </summary>
    public const byte LightAzure   = 0x21;
    /// <summary>
    /// Light blue color.
    /// </summary>
    public const byte LightBlue    = 0x22;
    /// <summary>
    /// Light violet color.
    /// </summary>
    public const byte LightViolet  = 0x23;
    /// <summary>
    /// Light magenta color.
    /// </summary>
    public const byte LightMagenta = 0x24;
    /// <summary>
    /// Light rose color.
    /// </summary>
    public const byte LightRose    = 0x25;
    /// <summary>
    /// Light red color.
    /// </summary>
    public const byte LightRed     = 0x26;
    /// <summary>
    /// Light orange color.
    /// </summary>
    public const byte LightOrange  = 0x27;
    /// <summary>
    /// Yellow color.
    /// </summary>
    public const byte Yellow       = 0x28;
    /// <summary>
    /// Light lime color.
    /// </summary>
    public const byte LightLime    = 0x29;
    /// <summary>
    /// Light green color.
    /// </summary>
    public const byte LightGreen   = 0x2A;
    /// <summary>
    /// Light teal color.
    /// </summary>
    public const byte LightTeal    = 0x2B;
    /// <summary>
    /// Light cyan color.
    /// </summary>
    public const byte LightCyan    = 0x2C;
    /// <summary>
    /// Medium gray color.
    /// </summary>
    public const byte MediumGray   = 0x2D;

    // Row 3 ($3x) — palest / lightest shades
    /// <summary>
    /// White color.
    /// </summary>
    public const byte White       = 0x30;
    /// <summary>
    /// Pale azure color.
    /// </summary>
    public const byte PaleAzure   = 0x31;
    /// <summary>
    /// Pale blue color.
    /// </summary>
    public const byte PaleBlue    = 0x32;
    /// <summary>
    /// Pale violet color.
    /// </summary>
    public const byte PaleViolet  = 0x33;
    /// <summary>
    /// Pale magenta color.
    /// </summary>
    public const byte PaleMagenta = 0x34;
    /// <summary>
    /// Pale rose color.
    /// </summary>
    public const byte PaleRose    = 0x35;
    /// <summary>
    /// Pale red color.
    /// </summary>
    public const byte PaleRed     = 0x36;
    /// <summary>
    /// Pale orange color.
    /// </summary>
    public const byte PaleOrange  = 0x37;
    /// <summary>
    /// Pale yellow color.
    /// </summary>
    public const byte PaleYellow  = 0x38;
    /// <summary>
    /// Pale lime color.
    /// </summary>
    public const byte PaleLime    = 0x39;
    /// <summary>
    /// Pale green color.
    /// </summary>
    public const byte PaleGreen   = 0x3A;
    /// <summary>
    /// Pale teal color.
    /// </summary>
    public const byte PaleTeal    = 0x3B;
    /// <summary>
    /// Pale cyan color.
    /// </summary>
    public const byte PaleCyan    = 0x3C;
    /// <summary>
    /// Silver color.
    /// </summary>
    public const byte Silver      = 0x3D;
}
