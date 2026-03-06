/*
Peek-Poke Demo: Direct NES hardware register access.
Demonstrates poke() for writing to hardware registers
and peek() for reading from hardware registers.

PPU Registers:
  $2001 = PPU_MASK (rendering control)
  $2002 = PPU_STATUS (vblank flag, sprite 0 hit)
  $2005 = PPU_SCROLL (scroll position)
*/

// set palette colors
pal_col(0, 0x02);   // dark blue background
pal_col(1, 0x14);   // purple
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("PEEK-POKE DEMO");

// enable PPU rendering
ppu_on_all();

// ppu_mask: set rendering control flags
ppu_mask(0x1E);

// poke: direct write to PPU scroll register
poke(0x2005, 0);    // scroll X = 0
poke(0x2005, 0);    // scroll Y = 0

// peek: read PPU status register
// reading $2002 resets the PPU address latch
peek(0x2002);

// infinite loop
while (true) ;
