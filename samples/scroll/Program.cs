/*
Scrolling demo.
Horizontal mirroring means nametables A and C are stacked vertically.
The vertical scroll area is 480 pixels; nametables wrap around.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/scroll.c
*/

// set palette colors
pal_col(0, 0x02);   // dark blue
pal_col(1, 0x14);   // pink
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text to nametable A
vram_adr(NTADR_A(2, 0));
vram_write("Nametable A, Line 0");
vram_adr(NTADR_A(2, 15));
vram_write("Nametable A, Line 15");
vram_adr(NTADR_A(2, 29));
vram_write("Nametable A, Line 29");

// write text to nametable C
vram_adr(NTADR_C(2, 0));
vram_write("Nametable C, Line 0");
vram_adr(NTADR_C(2, 15));
vram_write("Nametable C, Line 15");
vram_adr(NTADR_C(2, 29));
vram_write("Nametable C, Line 29");

// enable rendering
ppu_on_all();

// scroll demo: bounce between 0 and 239
byte scroll_y = 0;
byte going_down = 1;

while (true)
{
    ppu_wait_nmi();
    scroll(0, scroll_y);

    if (going_down != 0)
    {
        scroll_y = (byte)(scroll_y + 1);
        if (scroll_y == 239)
            going_down = 0;
    }
    else
    {
        scroll_y = (byte)(scroll_y - 1);
        if (scroll_y == 0)
            going_down = 1;
    }
}
