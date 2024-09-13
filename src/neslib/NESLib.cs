﻿using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("dotnes")]
[assembly: InternalsVisibleTo("dotnes.tasks")]
[assembly: InternalsVisibleTo("dotnes.tests")]

namespace NES;

/// <summary>
/// Recommended use via `using static NES.NESLib;`
/// Based on: https://github.com/clbr/neslib/blob/master/neslib.h
/// Alternate: https://github.com/mhughson/attributes/blob/master/neslib.h
/// Tables in: https://github.com/clbr/neslib/blob/master/neslib.sinc
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

    /// <summary>
    /// set sprite in OAM buffer, chrnum is tile, attr is attribute, sprid is offset in OAM in bytes
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_spr(byte x, byte y, byte chrnum, byte attr, byte sprid) => default;

    /// <summary>
    /// set metasprite in OAM buffer
    /// meta sprite is a const unsigned char array, it contains four bytes per sprite
    /// in order x offset, y offset, tile, attribute
    /// x=128 is end of a meta sprite
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_meta_spr(byte x, byte y, byte sprid, byte[] data) => default;

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
    /// unpack RLE data to current address of vram, mostly used for nametables
    /// </summary>
    public static void vram_unrle(byte[] data) { }

    /// <summary>
    /// delay for N frames
    /// </summary>
    public static void delay(byte frames) { }

    /// <summary>
    /// set scroll, including rhe top bits
    /// it is always applied at beginning of a TV frame, not at the function call
    /// </summary>
    public static void scroll(int x, int y) { }

    /// <summary>
    /// set scroll after screen split invoked by the sprite 0 hit
    /// warning: all CPU time between the function call and the actual split point will be wasted!
    /// warning: the program loop has to fit into the frame time, ppu_wait_frame should not be used
    ///          otherwise empty frames without split will be inserted, resulting in jumpy screen
    /// warning: only X scroll could be changed in this version
    /// </summary>
    public static void split(int x, int y) { }


    /// <summary>
    /// select current chr bank for sprites, 0..1
    /// </summary>
    public static void bank_spr(byte n) { }

    /// <summary>
    /// select current chr bank for background, 0..1
    /// </summary>
    public static void bank_bg(byte n) { }

    /// <summary>
    /// get random number 0..255, same as rand8()
    /// </summary>
    public static byte rand() => default;
    /// <summary>
    /// get random number 0..255
    /// </summary>
    public static byte rand8() => default;
    /// <summary>
    /// get random number 0..65535
    /// </summary>
    public static ushort rand16() => default;

    /// <summary>
    /// set random seed
    /// </summary>
    public static void set_rand(ushort seed) { }

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

    // Tables are from: https://github.com/clbr/neslib/blob/master/neslib.sinc

    internal static readonly byte[] palBrightTableL =
    [
        /*
         * palBrightTableL:
         * .byte <palBrightTable0,<palBrightTable1,<palBrightTable2
         * .byte <palBrightTable3,<palBrightTable4,<palBrightTable5
         * .byte <palBrightTable6,<palBrightTable7,<palBrightTable8
         */
        0x34, 0x44, 0x54, 0x64, 0x74, 0x84, 0x94, 0xA4, 0xB4, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84, 0x84
    ];

    internal static readonly byte[] palBrightTable0 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, //black
    ];
    internal static readonly byte[] palBrightTable1 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable2 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable3 =
    [
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F, 0x0F,
    ];
    internal static readonly byte[] palBrightTable4 =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0F, 0x0F, 0x0F, //normal colors
    ];
    internal static readonly byte[] palBrightTable5 =
    [
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x00, 0x00, 0x00,
    ];
    internal static readonly byte[] palBrightTable6 =
    [
        0x10, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x10, 0x10, 0x10, //$10 because $20 is the same as $30
    ];
    internal static readonly byte[] palBrightTable7 =
    [
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x20, 0x20, 0x20,
    ];
    internal static readonly byte[] palBrightTable8 =
    [
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, //white
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
        0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
    ];
}