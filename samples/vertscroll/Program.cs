/*
Vertical scrolling demo.
Horizontal mirroring (NESMirroring=Horizontal) stacks nametables A and C
vertically, enabling smooth vertical scrolling through a 480-pixel area.
*/

// set palette colors
pal_col(0, 0x0F);   // black
pal_col(1, 0x19);   // green
pal_col(2, 0x20);   // white
pal_col(3, 0x31);   // light blue

// write text to nametable A
vram_adr(NTADR_A(2, 2));
vram_write("VERTICAL SCROLL DEMO");
vram_adr(NTADR_A(2, 14));
vram_write("Nametable A");
vram_adr(NTADR_A(2, 27));
vram_write("Scrolling down...");

// write text to nametable C
vram_adr(NTADR_C(2, 2));
vram_write("Nametable C");
vram_adr(NTADR_C(2, 14));
vram_write("Still scrolling!");
vram_adr(NTADR_C(2, 27));
vram_write("Wrapping around...");

// enable rendering
ppu_on_all();

// continuously scroll downward
byte scroll_y = 0;

while (true)
{
    ppu_wait_nmi();
    scroll_y++;
    scroll(0, scroll_y);
}
