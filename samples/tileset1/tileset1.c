// tileset1.c - For comparison on 8bitworkshop.com
// Build this on 8bitworkshop and compare with tileset1.nes from dotnes
// Original tileset data from 8bitworkshop tileset1.c preset

#include "neslib.h"

// Include the tileset data
#include "tileset1.c"

// This version uses CHR ROM (the tileset is embedded in the ROM)
// The tileset is padded so ASCII codes map directly to tile indices
// (tiles 0x00-0x1F are blank, 0x20-0x9F have the font)

void main(void)
{
  // set palette colors (from tileset1.c palSprites)
  pal_bg(palSprites);

  // write text to name table
  vram_adr(NTADR_A(8, 2));
  vram_write("CUSTOM TILESET", 14);

  vram_adr(NTADR_A(2, 6));
  vram_write("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 26);

  vram_adr(NTADR_A(2, 8));
  vram_write("0123456789 !@#$%&", 17);

  vram_adr(NTADR_A(6, 12));
  vram_write("POWERED BY .NET", 15);

  // enable PPU rendering
  ppu_on_all();

  // infinite loop
  while (1) ;
}
