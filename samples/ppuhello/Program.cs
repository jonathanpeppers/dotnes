// PPU Hello - Direct PPU register access without neslib
// Ported from 8bitworkshop ppuhello.c

// Wait for PPU warmup
waitvsync();
waitvsync();

// Turn off screen
poke(PPU_CTRL, 0);
poke(PPU_MASK, 0);

// Set palette at $3F00
poke(PPU_ADDR, 0x3F);
poke(PPU_ADDR, 0x00);
poke(PPU_DATA, 0x01);  // blue
poke(PPU_DATA, 0x00);  // gray
poke(PPU_DATA, 0x10);  // lt gray
poke(PPU_DATA, 0x20);  // white

// Write "HELLO PPU!" at nametable address $21C9
poke(PPU_ADDR, 0x21);
poke(PPU_ADDR, 0xC9);
poke(PPU_DATA, 0x48);  // H
poke(PPU_DATA, 0x45);  // E
poke(PPU_DATA, 0x4C);  // L
poke(PPU_DATA, 0x4C);  // L
poke(PPU_DATA, 0x4F);  // O
poke(PPU_DATA, 0x20);  // (space)
poke(PPU_DATA, 0x50);  // P
poke(PPU_DATA, 0x50);  // P
poke(PPU_DATA, 0x55);  // U
poke(PPU_DATA, 0x21);  // !

// Reset scroll position
poke(PPU_SCROLL, 0);
poke(PPU_SCROLL, 0);

// Reset PPU address
poke(PPU_ADDR, 0x20);
poke(PPU_ADDR, 0x00);

// Turn on screen
poke(PPU_MASK, (byte)(MASK.BG | MASK.SPR | MASK.EDGE_BG | MASK.EDGE_SPR));

while (true) ;
