/*
Based on: https://8bitworkshop.com/v3.8.0/?platform=nes&file=hello.c

A simple "hello world" example.
Set the screen background color and palette colors.
Then write a message to the nametable.
Finally, turn on the PPU to display video.
*/

// set palette colors
pal_col(0, 0x02);   // set screen to dark blue
pal_col(1, 0x14);   // fuchsia
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text to name table
vram_adr(NTADR_A(2, 2));            // set address
vram_write("HELLO, .NET!");         // write bytes to video RAM

// enable PPU rendering (turn on screen)
ppu_on_all();

// infinite loop
while (true) ;
