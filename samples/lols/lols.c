/*
Scrolling demo for 8bitworkshop.com (NES platform).
Paste into https://8bitworkshop.com to compile.
Based on https://8bitworkshop.com/v3.10.0/?platform=nes&file=scroll.c
*/

#include "neslib.h"

//#link "chr_generic.s"

void main(void)
{
    byte scroll_y = 0;

    // set palette colors
    pal_col(0, 0x02);   // set screen to dark blue
    pal_col(1, 0x14);   // fuchsia
    pal_col(2, 0x20);   // grey
    pal_col(3, 0x30);   // white

    // write text to nametable A
    vram_adr(NTADR_A(2, 2));
    vram_write("LOL! LOL! LOL! LOL! LOL! LOL!", 29);
    vram_adr(NTADR_A(2, 8));
    vram_write("LOL! LOL! LOL! LOL! LOL! LOL!", 29);
    vram_adr(NTADR_A(2, 14));
    vram_write("LOL! LOL! LOL! LOL! LOL! LOL!", 29);
    vram_adr(NTADR_A(2, 20));
    vram_write("LOL! LOL! LOL! LOL! LOL! LOL!", 29);
    vram_adr(NTADR_A(2, 26));
    vram_write("LOL! LOL! LOL! LOL! LOL! LOL!", 29);

    // enable PPU rendering (turn on screen)
    ppu_on_all();

    // infinite loop
    while (1)
    {
        // wait for next frame
        ppu_wait_frame();
        // scroll up (1 pixel per frame)
        scroll_y += 1;
        scroll(0, scroll_y);
    }
}
