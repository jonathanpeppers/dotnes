/*
Horizontal scrolling demo with vrambuf and split.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/horizscroll.c
Simplified: uses byte scroll counter (0-255), continuous left scroll.
*/

using static NES.NESLib;

// palette
byte[] PALETTE = {
    0x03,                       // background color
    0x25, 0x30, 0x27, 0x00,    // palette 0
    0x1C, 0x20, 0x2C, 0x00,    // palette 1
    0x00, 0x10, 0x20, 0x00,    // palette 2
    0x06, 0x16, 0x26, 0x00,    // palette 3
    0x16, 0x35, 0x24, 0x00,    // sprite palette 0
    0x00, 0x37, 0x25, 0x00,    // sprite palette 1
    0x0D, 0x2D, 0x1A, 0x00,    // sprite palette 2
    0x0D, 0x27, 0x2A           // sprite palette 3
};

// set palette colors
pal_all(PALETTE);

// write text to nametable A (top lines are the "status bar")
vram_adr(NTADR_A(7, 0));
vram_write("Nametable A, Line 0");
vram_adr(NTADR_A(7, 1));
vram_write("Nametable A, Line 1");
vram_adr(NTADR_A(7, 2));
vram_write("Nametable A, Line 2");

// fill line 3 with tile 5 to create a visible boundary
vram_adr(NTADR_A(0, 3));
vram_fill(5, 32);

// more text in nametable A
vram_adr(NTADR_A(2, 4));
vram_write("Nametable A, Line 4");
vram_adr(NTADR_A(2, 15));
vram_write("Nametable A, Line 15");
vram_adr(NTADR_A(2, 27));
vram_write("Nametable A, Line 27");

// text in nametable B (right side with vertical mirroring)
vram_adr(NTADR_B(2, 4));
vram_write("Nametable B, Line 4");
vram_adr(NTADR_B(2, 15));
vram_write("Nametable B, Line 15");
vram_adr(NTADR_B(2, 27));
vram_write("Nametable B, Line 27");

// set attributes for nametable A — palette 1 for top rows
vram_adr(0x23c0);
vram_fill(0x55, 8);

// set sprite 0 for split detection
oam_clear();
oam_spr(1, 30, 0xa0, 0, 0);

// clear vram buffer and set NMI to use it
vrambuf_clear();
set_vram_update(0x0100);

// enable PPU rendering
ppu_on_all();

// scroll demo — continuous left scroll
scroll_demo();

static void scroll_demo()
{
    byte x = 0;

    while (true)
    {
        // ensure VRAM buffer is cleared
        ppu_wait_nmi();
        vrambuf_clear();

        // split at sprite zero and set X scroll
        split(x, 0);

        // scroll left one pixel
        x = (byte)(x + 1);
    }
}
