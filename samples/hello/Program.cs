using static NES.NESColor;

/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=hello.c

A simple "hello world" example.
Set the screen background color and palette colors.
Then write a message to the nametable.
Finally, turn on the PPU to display video.
*/

// set palette colors
pal_col(0, DarkBlue);
pal_col(1, Magenta);
pal_col(2, LightGray);
pal_col(3, White);

// write text to name table
vram_adr(NTADR_A(2, 2));       // set address
vram_write("HELLO, .NET!");    // write bytes to video RAM

// enable PPU rendering (turn on screen)
ppu_on_all();

// infinite loop
while (true) ;
