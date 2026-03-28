using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("dotnes")]
[assembly: InternalsVisibleTo("dotnes-decompiler")]
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
    public static void pal_all(byte[] data) => throw null!;

    /// <summary>
    /// set bg palette only, data is 16 bytes array
    /// </summary>
    public static void pal_bg(byte[] data) => throw null!;

    /// <summary>
    /// set spr palette only, data is 16 bytes array
    /// </summary>
    public static void pal_spr(byte[] data) => throw null!;

    /// <summary>
    /// set a palette entry, index is 0..31
    /// </summary>
    public static void pal_col(byte index, byte color) => throw null!;

    /// <summary>
    /// reset palette to $0f
    /// </summary>
    public static void pal_clear() => throw null!;

    /// <summary>
    /// set virtual bright both for sprites and background, 0 is black, 4 is normal, 8 is white
    /// </summary>
    public static void pal_bright(byte bright) => throw null!;

    /// <summary>
    /// set virtual bright for sprites only
    /// </summary>
    public static void pal_spr_bright(byte bright) => throw null!;

    /// <summary>
    /// set virtual bright for sprites background only
    /// </summary>
    public static void pal_bg_bright(byte bright) => throw null!;

    /// <summary>
    /// play music (FamiTone2 library).
    /// Starts or resumes playback of a song by index from FamiTone2 music data.
    /// <para><b>⚠️ Not to be confused with <see cref="music_tick"/>.</b>
    /// This method is for the FamiTone2 external music library.
    /// <see cref="music_tick"/> is for the dotnes built-in music engine.</para>
    /// </summary>
    public static void music_play(byte song) => throw null!;

    /// <summary>
    /// stop music (FamiTone)
    /// </summary>
    public static void music_stop() => throw null!;

    /// <summary>
    /// pause music (FamiTone)
    /// </summary>
    public static void music_pause(bool pause) => throw null!;

    /// <summary>
    /// initialize the APU (Audio Processing Unit)
    /// enables all sound channels and sets initial state
    /// </summary>
    public static void apu_init() => throw null!;

    /// <summary>
    /// initialize FamiTone2 music library with music data
    /// Usage: famitone_init("danger_streets_music_data")
    /// The string names a data label in a linked .s file
    /// </summary>
    public static void famitone_init(string musicDataLabel) => throw null!;

    /// <summary>
    /// initialize FamiTone2 sound effects
    /// Usage: sfx_init("demo_sounds")
    /// The string names a data label in a linked .s file
    /// </summary>
    public static void sfx_init(string soundDataLabel) => throw null!;

    /// <summary>
    /// set up music data pointer for playback
    /// data is a byte array containing encoded music commands
    /// </summary>
    public static void start_music(byte[] data) => throw null!;

    /// <summary>
    /// advance one frame of music playback (dotnes built-in music engine).
    /// Call once per vblank/NMI to process music data and write to APU registers.
    /// <para><b>⚠️ Not to be confused with <see cref="music_play"/>.</b>
    /// <see cref="music_play"/> is for the FamiTone2 external library.
    /// This method is for the dotnes built-in music engine.</para>
    /// </summary>
    public static void music_tick() => throw null!;

    /// <summary>
    /// register a ushort[] note table for pulse channel playback
    /// </summary>
    public static void set_music_pulse_table(ushort[] table) => throw null!;

    /// <summary>
    /// register a ushort[] note table for triangle channel playback
    /// </summary>
    public static void set_music_triangle_table(ushort[] table) => throw null!;

    /// <summary>
    /// play sound effect
    /// </summary>
    public static void sfx_play(byte sound, byte channel) => throw null!;

    /// <summary>
    /// play sample
    /// </summary>
    public static void sample_play(byte sample) => throw null!;

    /// <summary>
    /// Set NMI callback to a function (called every NMI frame).
    /// Usage: nmi_set_callback(&amp;my_nmi_handler)
    /// </summary>
    public static unsafe void nmi_set_callback(delegate*<void> callback) => throw null!;

    /// <summary>
    /// Set IRQ callback to a function (called when a hardware IRQ fires).
    /// Usage: irq_set_callback(&amp;my_irq_handler)
    /// </summary>
    public static unsafe void irq_set_callback(delegate*<void> callback) => throw null!;

    /// <summary>
    /// enable CPU interrupts (6502 CLI instruction)
    /// Must be called after setting up the IRQ source (e.g., MMC3 scanline counter).
    /// </summary>
    public static void cli() => throw null!;

    /// <summary>
    /// disable CPU interrupts (6502 SEI instruction)
    /// </summary>
    public static void sei() => throw null!;

    /// <summary>
    /// get pad trigger
    /// </summary>
    public static PAD pad_trigger(byte pad) => throw null!;

    /// <summary>
    /// get pad state
    /// </summary>
    public static PAD pad_state(byte pad) => throw null!;

    /// <summary>
    /// read from vram
    /// </summary>
    public static void vram_read(byte[] dst, uint size) => throw null!;

    /// <summary>
    /// write to vram
    /// </summary>
    public static void vram_write(byte[] src, uint size) => throw null!;

    /// <summary>
    /// unpack LZ4 data to vram
    /// </summary>
    public static void vram_unlz4(byte[] input, byte[] output, uint uncompressedSize) => throw null!;

    /// <summary>
    /// fill memory at an absolute address
    /// </summary>
    public static void memfill(ushort addr, byte value, uint len) => throw null!;

    /// <summary>
    /// clear OAM buffer fast
    /// </summary>
    public static void oam_clear_fast() => throw null!;

    /// <summary>
    /// set metasprite in OAM buffer with palette override
    /// palette bits (0-3) are OR'd into each sprite's attribute byte
    /// uses oam_off global for OAM buffer offset, updates it after writing
    /// </summary>
    public static void oam_meta_spr_pal(byte x, byte y, byte pal, byte[] data) => throw null!;

    /// <summary>
    /// set metasprite in OAM buffer with clipping
    /// </summary>
    public static void oam_meta_spr_clip(int x, byte y, byte[] metasprite) => throw null!;

    /// <summary>
    /// wait actual TV frame, 50hz for PAL, 60hz for NTSC
    /// </summary>
    public static void ppu_wait_nmi() => throw null!;

    /// <summary>
    /// write a byte value to an absolute memory address
    /// </summary>
    public static void poke(ushort addr, byte value) => throw null!;

    /// <summary>
    /// read a byte value from an absolute memory address
    /// </summary>
    public static byte peek(ushort addr) => throw null!;

    /// <summary>
    /// wait for vertical sync (vblank), busy-loops on PPU status bit 7
    /// </summary>
    public static void waitvsync() => throw null!;

    /// <summary>
    /// wait virtual frame, it is always 50hz, frame-to-frame in PAL, frameskip in NTSC
    /// </summary>
    public static void ppu_wait_frame() => throw null!;

    /// <summary>
    /// turn off rendering, nmi still enabled when rendering is disabled
    /// </summary>
    public static void ppu_off() => throw null!;

    /// <summary>
    /// turn on bg, spr
    /// </summary>
    public static void ppu_on_all() => throw null!;

    /// <summary>
    /// turn on bg only
    /// </summary>
    public static void ppu_on_bg() => throw null!;

    /// <summary>
    /// turn on spr only
    /// </summary>
    public static void ppu_on_spr() => throw null!;

    /// <summary>
    /// set PPU_MASK directly
    /// </summary>
    public static void ppu_mask(MASK mask) => throw null!;

    /// <summary>
    /// get current video system, 0 for PAL, not 0 for NTSC
    /// </summary>
    public static byte ppu_system() => throw null!;

    /// <summary>
    /// Return an 8-bit counter incremented at each vblank
    /// </summary>
    public static byte nesclock() => throw null!;

    /// <summary>
    /// get the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static byte get_ppu_ctrl_var() => throw null!;

    /// <summary>
    /// set the internal ppu ctrl cache var for manual writing
    /// </summary>
    public static void set_ppu_ctrl_var(byte var) => throw null!;

    /// <summary>
    /// clear OAM buffer, all the sprites are hidden
    /// </summary>
    public static void oam_clear() => throw null!;

    /// <summary>
    /// OAM buffer offset, used by oam_meta_spr_pal
    /// </summary>
    public static byte oam_off;

    /// <summary>
    /// set sprite display mode, 0 for 8x8 sprites, 1 for 8x16 sprites
    /// </summary>
    public static void oam_size(byte size) => throw null!;

    /// <summary>
    /// set sprite in OAM buffer, chrnum is tile, attr is attribute, sprid is offset in OAM in bytes
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_spr(byte x, byte y, byte chrnum, byte attr, byte sprid) => throw null!;

    /// <summary>
    /// poll controller and return enum like PAD.LEFT, etc.
    /// </summary>
    /// <param name="pad">pad number (0 or 1)</param>
    /// <returns>Enum like PAD.LEFT, etc.</returns>
    public static PAD pad_poll(byte pad) => throw null!;

    /// <summary>
    /// set metasprite in OAM buffer
    /// meta sprite is a const unsigned char array, it contains four bytes per sprite
    /// in order x offset, y offset, tile, attribute
    /// x=128 is end of a meta sprite
    /// </summary>
    /// <returns>returns sprid+4, which is offset for a next sprite</returns>
    public static byte oam_meta_spr(byte x, byte y, byte sprid, byte[] data) => throw null!;

    /// <summary>
    /// hide all remaining sprites from given offset
    /// </summary>
    public static void oam_hide_rest(byte sprid) => throw null!;

    /// <summary>
    /// set vram pointer to write operations if you need to write some data to vram
    /// </summary>
    public static void vram_adr(ushort adr) => throw null!;

    /// <summary>
    /// put a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_put(byte n) => throw null!;

    /// <summary>
    /// fill a block with a byte at current vram address, works only when rendering is turned off
    /// </summary>
    public static void vram_fill(byte n, uint len) => throw null!;

    /// <summary>
    /// Set VRAM auto-increment mode: <see cref="VramIncrement.By1"/> for +1 or <see cref="VramIncrement.By32"/> for +32
    /// </summary>
    public static void vram_inc(VramIncrement mode) => throw null!;

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(string src) => throw null!;

    /// <summary>
    /// write a block to current address of vram, works only when rendering is turned off
    /// </summary>
    public static void vram_write(byte[] src) => throw null!;

    /// <summary>
    /// unpack RLE data to current address of vram, mostly used for nametables
    /// </summary>
    public static void vram_unrle(byte[] data) => throw null!;

    /// <summary>
    /// delay for N frames
    /// </summary>
    public static void delay(byte frames) => throw null!;

    /// <summary>
    /// set scroll, including rhe top bits
    /// it is always applied at beginning of a TV frame, not at the function call
    /// </summary>
    public static void scroll(int x, int y) => throw null!;

    /// <summary>
    /// set scroll after screen split invoked by the sprite 0 hit
    /// warning: all CPU time between the function call and the actual split point will be wasted!
    /// warning: the program loop has to fit into the frame time, ppu_wait_frame should not be used
    ///          otherwise empty frames without split will be inserted, resulting in jumpy screen
    /// warning: only X scroll could be changed in this version
    /// </summary>
    public static void split(int x, int y) => throw null!;

    /// <summary>
    /// select current chr bank for sprites, 0..1
    /// </summary>
    public static void bank_spr(byte n) => throw null!;

    /// <summary>
    /// select current chr bank for background, 0..1
    /// </summary>
    public static void bank_bg(byte n) => throw null!;

    /// <summary>
    /// get random number 0..255, same as rand8()
    /// </summary>
    public static byte rand() => throw null!;

    /// <summary>
    /// get random number 0..255
    /// </summary>
    public static byte rand8() => throw null!;

    /// <summary>
    /// get random number 0..32767, cc65-compatible 16-bit PRNG (32-bit LCG state)
    /// </summary>
    public static ushort rand16() => throw null!;

    /// <summary>
    /// set random seed for cc65-compatible PRNG, seed in A(lo)/X(hi)
    /// </summary>
    public static void srand(ushort seed) => throw null!;

    /// <summary>
    /// set random seed for 8-bit LFSR PRNG
    /// </summary>
    public static void set_rand(byte seed) => throw null!;

    /// <summary>
    /// Add two packed-BCD 16-bit numbers (software BCD since NES CPU lacks hardware BCD).
    /// Each nibble represents a decimal digit 0-9. Result is a 4-digit BCD value.
    /// Example: bcd_add(0x0100, 0x0001) returns 0x0101 (100 + 1 = 101).
    /// </summary>
    public static ushort bcd_add(ushort a, ushort b) => throw null!;

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
    public static void set_vram_update(byte[] buf) => throw null!;

    /// <summary>
    /// set_vram_update with a raw address (e.g., 0x0100 for the stack-page update buffer)
    /// </summary>
    public static void set_vram_update(ushort addr) => throw null!;

    // all following vram functions only work when display is disabled

    /// <summary>
    /// do a series of VRAM writes, the same format as for set_vram_update, but writes done right away
    /// </summary>
    public static void flush_vram_update(byte[] buf) => throw null!;

    // VRAM update buffer constants
    public const byte NT_UPD_HORZ = 0x40;
    public const byte NT_UPD_VERT = 0x80;
    public const byte NT_UPD_EOF = 0xFF;

    /// <summary>
    /// VRAM update buffer address (stack page $0100)
    /// Pass to set_vram_update() to use the vrambuf module
    /// </summary>
    public const ushort updbuf = 0x0100;

    /// <summary>
    /// OR with address to select vertical sequential mode in vrambuf_put
    /// </summary>
    public const ushort VRAMBUF_VERT = 0x8000;

    /// <summary>
    /// clear the VRAM update buffer and write EOF marker
    /// </summary>
    public static void vrambuf_clear() => throw null!;

    /// <summary>
    /// write a horizontal string sequence to the VRAM update buffer
    /// the NMI handler will flush it to VRAM on the next frame
    /// </summary>
    public static void vrambuf_put(ushort addr, string str) => throw null!;

    /// <summary>
    /// write a horizontal byte array sequence to the VRAM update buffer
    /// the NMI handler will flush it to VRAM on the next frame
    /// </summary>
    public static void vrambuf_put(ushort addr, byte[] buf, byte len) => throw null!;

    /// <summary>
    /// write a vertical byte array sequence to the VRAM update buffer
    /// same as vrambuf_put but sets the vertical increment flag
    /// </summary>
    public static void vrambuf_put_vert(ushort addr, byte[] buf, byte len) => throw null!;

    /// <summary>
    /// write EOF marker at current buffer position (without advancing pointer)
    /// </summary>
    public static void vrambuf_end() => throw null!;

    /// <summary>
    /// write EOF, wait for NMI frame, then clear buffer
    /// </summary>
    public static void vrambuf_flush() => throw null!;

    // CNROM (Mapper 3) bank switching

    /// <summary>
    /// select a CHR ROM bank (CNROM mapper 3)
    /// writes the bank number to $8000 to switch the 8 KB CHR bank
    /// </summary>
    public static void cnrom_set_chr_bank(byte bank) => throw null!;

    // These are from: https://github.com/mhughson/attributes/blob/master/neslib.h

    public const ushort NAMETABLE_A = 0x2000;
    public const ushort NAMETABLE_B = 0x2400;
    public const ushort NAMETABLE_C = 0x2800;
    public const ushort NAMETABLE_D = 0x2c00;

    // PPU register addresses for direct hardware access via poke()/peek()
    public const ushort PPU_CTRL = 0x2000;
    public const ushort PPU_MASK = 0x2001;
    public const ushort PPU_STATUS = 0x2002;
    public const ushort PPU_SCROLL = 0x2005;
    public const ushort PPU_ADDR = 0x2006;
    public const ushort PPU_DATA = 0x2007;

    // APU register addresses for direct hardware access via poke()/peek()
    public const ushort APU_PULSE1_CTRL = 0x4000;
    public const ushort APU_PULSE1_SWEEP = 0x4001;
    public const ushort APU_PULSE1_TIMER_LO = 0x4002;
    public const ushort APU_PULSE1_TIMER_HI = 0x4003;
    public const ushort APU_PULSE2_CTRL = 0x4004;
    public const ushort APU_PULSE2_SWEEP = 0x4005;
    public const ushort APU_PULSE2_TIMER_LO = 0x4006;
    public const ushort APU_PULSE2_TIMER_HI = 0x4007;
    public const ushort APU_TRIANGLE_CTRL = 0x4008;
    public const ushort APU_TRIANGLE_TIMER_LO = 0x400A;
    public const ushort APU_TRIANGLE_TIMER_HI = 0x400B;
    public const ushort APU_NOISE_CTRL = 0x400C;
    public const ushort APU_NOISE_PERIOD = 0x400E;
    public const ushort APU_NOISE_LENGTH = 0x400F;
    public const ushort APU_STATUS = 0x4015;

    // Battery-backed SRAM address range ($6000-$7FFF) for use with peek()/poke()
    public const ushort SRAM_START = 0x6000;
    public const ushort SRAM_END = 0x7FFF;

    // MMC3 mapper register addresses for bank switching via poke()
    public const ushort MMC3_BANK_SELECT = 0x8000;
    public const ushort MMC3_BANK_DATA = 0x8001;
    public const ushort MMC3_MIRRORING = 0xA000;
    public const ushort MMC3_WRAM_ENABLE = 0xA001;

    // MMC3 IRQ register addresses for scanline counting via poke()
    public const ushort MMC3_IRQ_LATCH = 0xC000;
    public const ushort MMC3_IRQ_RELOAD = 0xC001;
    public const ushort MMC3_IRQ_DISABLE = 0xE000;
    public const ushort MMC3_IRQ_ENABLE = 0xE001;

    /// <summary>
    /// Set an MMC3 CHR bank register to the specified bank number.
    /// Writes reg to $8000 (MMC3_BANK_SELECT) and bank to $8001 (MMC3_BANK_DATA).
    /// </summary>
    /// <param name="reg">MMC3 bank-select value. Bits 0-2 select the register (R0-R7),
    /// bits 6-7 control PRG/CHR banking mode. Must be a compile-time constant.
    /// R0/R1 (reg 0-1): 2KB CHR banks at PPU $0000/$0800.
    /// R2-R5 (reg 2-5): 1KB CHR banks at PPU $1000/$1400/$1800/$1C00.
    /// R6-R7 (reg 6-7): PRG banks (not CHR).</param>
    /// <param name="bank">Bank number to select. Can be a constant or local variable.</param>
    public static void set_chr_mode(byte reg, byte bank) => throw null!;

    // MMC1 mapper register addresses for serial shift register writes via mmc1_write()
    public const ushort MMC1_CONTROL = 0x8000;
    public const ushort MMC1_CHR_BANK0 = 0xA000;
    public const ushort MMC1_CHR_BANK1 = 0xC000;
    public const ushort MMC1_PRG_BANK = 0xE000;

    // MMC1 Control register PRG/CHR mode bits: use mirror | (MMC1Mirror)prg_chr_bits
    // when combining mirroring with PRG/CHR modes in mmc1_set_mirroring().

    // MMC1 Control register PRG/CHR mode bits (OR with mirroring constants)
    /// <summary>PRG mode: fix last bank at $C000, switch 16KB bank at $8000 (bits 2-3 = 11).</summary>
    public const byte MMC1_PRG_FIX_LAST = 0x0C;
    /// <summary>CHR mode: two separate 4KB banks (bit 4 = 1).</summary>
    public const byte MMC1_CHR_4K = 0x10;

    /// <summary>
    /// Write a 5-bit value to an MMC1 register using the serial shift register protocol.
    /// MMC1 requires writing one bit at a time (5 writes total) to latch a value.
    /// The addr must be one of the MMC1_* register constants.
    /// </summary>
    public static void mmc1_write(ushort addr, byte value) => throw null!;

    /// <summary>
    /// Switch the MMC1 PRG bank by writing to the PRG bank register ($E000).
    /// </summary>
    public static void mmc1_set_prg_bank(byte bank) => throw null!;

    /// <summary>
    /// Switch both MMC1 CHR banks by writing to CHR bank 0 ($A000) and CHR bank 1 ($C000).
    /// </summary>
    public static void mmc1_set_chr_bank(byte bank0, byte bank1) => throw null!;

    /// <summary>
    /// Write the full MMC1 Control register ($8000) via the serial shift register.
    /// The value contains: mirroring mode (bits 0-1), PRG bank mode (bits 2-3),
    /// and CHR bank mode (bit 4). Use <see cref="MMC1Mirror"/> values OR'd with PRG/CHR
    /// mode bits. Writing only a mirror constant (e.g., <see cref="MMC1Mirror.Vertical"/>)
    /// resets PRG/CHR modes to zero — combine with your desired mode bits.
    /// </summary>
    public static void mmc1_set_mirroring(byte mode) => throw null!;

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
    /// macro to get most significant byte in compile time
    /// #define MSB(x)			(((x)>>8))
    /// </summary>
    public static byte MSB(ushort x) => (byte)(x >> 8);

    /// <summary>
    /// macro to get least significant byte in compile time
    /// #define LSB(x)			(((x)&0xff))
    /// </summary>
    public static byte LSB(ushort x) => (byte)(x & 0xff);

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void pal_copy() => throw null!;

    /// <summary>
    /// NOTE: this one is internal, not in neslib.h
    /// </summary>
    internal static void ppu_onoff() => throw null!;

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