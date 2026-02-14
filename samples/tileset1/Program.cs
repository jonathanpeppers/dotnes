/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=tileset1.c

Displays text using a custom CHR tileset from 8bitworkshop.
The tileset includes ASCII characters and a small climber sprite.
*/

// set palette colors (from tileset1.c palSprites)
byte[] palette = [
    0x0f, 0x17, 0x27, 0x37,
    0x0f, 0x11, 0x21, 0x31,
    0x0f, 0x15, 0x25, 0x35,
    0x0f, 0x19, 0x29, 0x39
];
pal_bg(palette);

// write text to name table using custom tileset
vram_adr(NTADR_A(8, 2));
vram_write("CUSTOM TILESET");

vram_adr(NTADR_A(2, 6));
vram_write("ABCDEFGHIJKLMNOPQRSTUVWXYZ");

vram_adr(NTADR_A(2, 8));
vram_write("0123456789 !@#$%&");

vram_adr(NTADR_A(6, 12));
vram_write("POWERED BY .NET");

// enable PPU rendering
ppu_on_all();

// infinite loop
while (true) ;
