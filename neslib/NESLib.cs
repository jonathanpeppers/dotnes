namespace NES;

/// <summary>
/// Recommended use via `using static NES.NESLib;`
/// Based on: https://github.com/clbr/neslib/blob/master/neslib.h
/// </summary>
public static class NESLib
{
    /// <summary>
    /// set a palette entry, index is 0..31
    /// </summary>
    public static void pal_col(byte index, byte color) { }

    /// <summary>
    /// set vram pointer to write operations if you need to write some data to vram
    /// </summary>
    public static void vram_adr(ushort adr) { }

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(string src, ushort size) { }

    /// <summary>
    /// turn on bg, spr
    /// </summary>
    public static void ppu_on_all() { }

    public const ushort NAMETABLE_A = 0x2000;
    public const ushort NAMETABLE_B = 0x2400;
    public const ushort NAMETABLE_C = 0x2800;
    public const ushort NAMETABLE_D = 0x2c00;

    // TODO: Macros below should be computed at compile-time and methods removed

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_A(x,y)	 	(NAMETABLE_A|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_A(byte x, byte y) => (ushort)(NAMETABLE_A | (((y) << 5) | (x)));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_B(x,y) 		(NAMETABLE_B|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_B(byte x, byte y) => (ushort)(NAMETABLE_B | (((y) << 5) | (x)));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_C(x,y) 		(NAMETABLE_C|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_C(byte x, byte y) => (ushort)(NAMETABLE_C | (((y) << 5) | (x)));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_D(x,y) 		(NAMETABLE_D|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_D(byte x, byte y) => (ushort)(NAMETABLE_D | (((y) << 5) | (x)));
}