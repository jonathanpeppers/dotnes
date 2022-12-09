using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("dotnes.tasks")]
[assembly: InternalsVisibleTo("dotnes.tests")]

namespace NES;

/// <summary>
/// Recommended use via `using static NES.NESLib;`
/// Based on: https://github.com/clbr/neslib/blob/master/neslib.h
/// Alternate: https://github.com/mhughson/attributes/blob/master/neslib.h
/// </summary>
public static class NESLib
{
    /// <summary>
    /// set bg and spr palettes, data is 32 bytes array
    /// </summary>
    public static void pal_all(byte[] data) { }

    /// <summary>
    /// set bg palette only, data is 16 bytes array
    /// </summary>
    public static void pal_bg(byte[] data) { }

    /// <summary>
    /// set spr palette only, data is 16 bytes array
    /// </summary>
    public static void pal_spr(byte[] data) { }

    /// <summary>
    /// set a palette entry, index is 0..31
    /// </summary>
    public static void pal_col(byte index, byte color) { }

    /// <summary>
    /// reset palette to $0f
    /// </summary>
    public static void pal_clear() { }

    /// <summary>
    /// set virtual bright both for sprites and background, 0 is black, 4 is normal, 8 is white
    /// </summary>
    public static void pal_bright(byte bright) { }

    /// <summary>
    /// set virtual bright for sprites only
    /// </summary>
    public static void pal_spr_bright(byte bright) { }

    /// <summary>
    /// set virtual bright for sprites background only
    /// </summary>
    public static void pal_bg_bright(byte bright) { }





    /// <summary>
    /// wait actual TV frame, 50hz for PAL, 60hz for NTSC
    /// </summary>
    public static void ppu_wait_nmi() { }

    /// <summary>
    /// wait virtual frame, it is always 50hz, frame-to-frame in PAL, frameskip in NTSC
    /// </summary>
    public static void ppu_wait_frame() { }

    /// <summary>
    /// turn off rendering, nmi still enabled when rendering is disabled
    /// </summary>
    public static void ppu_off() { }

    /// <summary>
    /// turn on bg, spr
    /// </summary>
    public static void ppu_on_all() { }

    /// <summary>
    /// turn on bg only
    /// </summary>
    public static void ppu_on_bg() { }

    /// <summary>
    /// turn on spr only
    /// </summary>
    public static void ppu_on_spr() { }

    /// <summary>
    /// set PPU_MASK directly
    /// </summary>
    public static void ppu_mask(byte mask) { }

    /// <summary>
    /// get current video system, 0 for PAL, not 0 for NTSC
    /// </summary>
    public static byte ppu_system() => default;

    /// <summary>
    /// Return an 8-bit counter incremented at each vblank
    /// </summary>
    public static byte nesclock() => default;

    /// <summary>
    /// get the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static byte get_ppu_ctrl_var() => default;

    /// <summary>
    /// set the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static void set_ppu_ctrl_var(byte var) { }


    /// <summary>
    /// clear OAM buffer, all the sprites are hidden
    /// </summary>
    public static void oam_clear() { }

    /// <summary>
    /// set sprite display mode, 0 for 8x8 sprites, 1 for 8x16 sprites
    /// </summary>
    public static void oam_size(byte size) { }

    // set sprite in OAM buffer, chrnum is tile, attr is attribute, sprid is offset in OAM in bytes
    // returns sprid+4, which is offset for a next sprite
    //unsigned char __fastcall__ oam_spr(unsigned char x, unsigned char y,
    //                    unsigned char chrnum, unsigned char attr,
    //                    unsigned char sprid);

    // set metasprite in OAM buffer
    // meta sprite is a const unsigned char array, it contains four bytes per sprite
    // in order x offset, y offset, tile, attribute
    // x=128 is end of a meta sprite
    // returns sprid+4, which is offset for a next sprite
    //unsigned char __fastcall__ oam_meta_spr(unsigned char x, unsigned char y,
    //                    unsigned char sprid, const unsigned char* data);

    /// <summary>
    /// hide all remaining sprites from given offset
    /// </summary>
    public static void oam_hide_rest(byte sprid) { }


    /// <summary>
    /// set vram pointer to write operations if you need to write some data to vram
    /// </summary>
    public static void vram_adr(ushort adr) { }

    /// <summary>
    /// put a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_put(byte n) { }

    /// <summary>
    /// fill a block with a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_fill(byte n, uint len) { }

    /// <summary>
    /// set vram autoincrement, 0 for +1 and not 0 for +32
    /// </summary>
    public static void vram_inc(byte n) { }

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(string src) { }

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(byte[] src) { }

    /// <summary>
    /// delay for N frames
    /// </summary>
    public static void delay(byte frames) { }

    /// <summary>
    /// set scroll, including rhe top bits
    /// it is always applied at beginning of a TV frame, not at the function call
    /// </summary>
    public static void scroll(uint x, uint y) { }

    /// <summary>
    /// set scroll after screen split invoked by the sprite 0 hit
    /// warning: all CPU time between the function call and the actual split point will be wasted!
    /// warning: the program loop has to fit into the frame time, ppu_wait_frame should not be used
    ///          otherwise empty frames without split will be inserted, resulting in jumpy screen
    /// warning: only X scroll could be changed in this version
    /// </summary>
    public static void split(uint x, uint y) { }


    /// <summary>
    /// select current chr bank for sprites, 0..1
    /// </summary>
    public static void bank_spr(byte n) { }

    /// <summary>
    /// select current chr bank for background, 0..1
    /// </summary>
    public static void bank_bg(byte n) { }

    /// <summary>
    /// when display is enabled, vram access could only be done with this vram update system
    /// the function sets a pointer to the update buffer that contains data and addresses
    /// in a special format. It allows to write non-sequental bytes, as well as horizontal or
    /// vertical nametable sequences.
    /// buffer pointer could be changed during rendering, but it only takes effect on a new frame
    /// number of transferred bytes is limited by vblank time
    /// to disable updates, call this function with NULL pointer
    ///
    /// the update data format:
    ///  MSB, LSB, byte for a non-sequental write
    ///  MSB|NT_UPD_HORZ, LSB, LEN, [bytes] for a horizontal sequence
    ///  MSB|NT_UPD_VERT, LSB, LEN, [bytes] for a vertical sequence
    ///  NT_UPD_EOF to mark end of the buffer
    ///
    /// length of this data should be under 256 bytes
    /// </summary>
    public static void set_vram_update(byte[] buf) { }

    // all following vram functions only work when display is disabled

    /// <summary>
    /// do a series of VRAM writes, the same format as for set_vram_update, but writes done right away
    /// </summary>
    public static void flush_vram_update(byte[] buf) { }

    // These are from: https://github.com/mhughson/attributes/blob/master/neslib.h

    /// <summary>
    /// set NMI/IRQ callback
    /// TODO: not sure if should be public?
    /// </summary>
    internal static void nmi_set_callback(Action callback) { }

    public const ushort NAMETABLE_A = 0x2000;
    public const ushort NAMETABLE_B = 0x2400;
    public const ushort NAMETABLE_C = 0x2800;
    public const ushort NAMETABLE_D = 0x2c00;

    // TODO: Macros below should be computed at compile-time and methods removed

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_A(x,y)	 	(NAMETABLE_A|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_A(byte x, byte y) => (ushort)(NAMETABLE_A | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_B(x,y) 		(NAMETABLE_B|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_B(byte x, byte y) => (ushort)(NAMETABLE_B | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_C(x,y) 		(NAMETABLE_C|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_C(byte x, byte y) => (ushort)(NAMETABLE_C | ((y << 5) | x));

    /// <summary>
    /// macro to calculate nametable address from X,Y in compile time
    /// #define NTADR_D(x,y) 		(NAMETABLE_D|(((y)<<5)|(x)))
    /// </summary>
    public static ushort NTADR_D(byte x, byte y) => (ushort)(NAMETABLE_D | ((y << 5) | x));

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void pal_copy() { }

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void ppu_onoff() { }
}