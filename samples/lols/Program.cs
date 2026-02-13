/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=scroll.c

Scrolling demo.
We've selected horizontal mirroring as the default, so
nametables A and C are stacked on top of each other.
The vertical scroll area is 480 pixels high; note how
the nametables wrap around.
*/

byte y = 0;   // y scroll position

// set palette colors
pal_col(0, 0x02);   // set screen to dark blue
pal_col(1, 0x14);   // fuchsia
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text across nametable A
vram_adr(NTADR_A(2, 2));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 6));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 10));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 14));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 18));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 22));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
vram_adr(NTADR_A(2, 26));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");

// enable PPU rendering (turn on screen)
ppu_on_all();

// infinite loop
while (true)
{
    // wait for next frame
    ppu_wait_frame();
    // scroll up continuously
    y += 1;
    scroll(0, y);
}
