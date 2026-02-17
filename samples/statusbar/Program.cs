/*
Split-screen status bar demo.
We position sprite 0 at the desired scanline, and when it
collides with the background, a flag in the PPU is set.
The split() function waits for this flag, then changes the
X scroll register in the PPU.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/statusbar.c
*/

using static NES.NESLib;

// set palette colors
pal_col(0, 0x00);   // black
pal_col(1, 0x04);   // dark purple
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white
pal_col(5, 0x14);   // pink
pal_col(6, 0x24);   // light pink
pal_col(7, 0x34);   // lighter pink

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
// place at x=1, y=30 (just below status bar), tile 0xa0, attribute 0
oam_clear();
oam_spr(1, 30, 0xa0, 0, 0);

// enable rendering
ppu_on_all();

// scroll demo: horizontal scroll with split screen
byte x = 0;
byte going_right = 1;

while (true)
{
    split(x, 0);

    if (going_right != 0)
    {
        x = (byte)(x + 1);
        if (x == 255)
            going_right = 0;
    }
    else
    {
        x = (byte)(x - 1);
        if (x == 0)
            going_right = 1;
    }
}
