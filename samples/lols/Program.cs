﻿/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=scroll.c

Scrolling demo.
We've selected horizontal mirroring as the default, so
nametables A and C are stacked on top of each other.
The vertical scroll area is 480 pixels high; note how
the nametables wrap around.
*/

// NOTE: C requires variables declared at the top of methods.
// Putting them at the top in C# should not be required.

int x = 0;   // x scroll position
int y = 0;   // y scroll position
string str = "LOL! LOL! LOL! LOL! LOL! LOL!";

// set palette colors
pal_col(0, 0x02);   // set screen to dark blue
pal_col(1, 0x14);   // fuchsia
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

//write text to name table
vram_adr(NTADR_A(1,0));
vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
// vram_adr(NTADR_A(2,10));
// vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
//  vram_adr(NTADR_B(1,20));
//  vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
// vram_adr(NTADR_B(2,0));
// vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
// vram_adr(NTADR_C(1,10));
// vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
// vram_adr(NTADR_C(2,20));
// vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");

// enable PPU rendering (turn on screen)
ppu_on_all();

// infinite loop
while (true)
{
    // wait for next frame
    ppu_wait_frame();
    // update y variable
    // y += 1;
    // set scroll register
    // scroll(x, y);
}
