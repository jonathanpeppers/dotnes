/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=bankswitch.c

Demonstrates MMC3 mapper bank switching via poke().
Sets the iNES header to mapper 4 (MMC3) with 4 PRG banks and 8 CHR banks.
Uses poke() to write MMC3 bank select ($8000) and bank data ($8001)
registers, switching CHR and PRG banks at runtime.
*/

// set palette colors
pal_col(1, 0x04);
pal_col(2, 0x20);
pal_col(3, 0x30);

// setup MMC3 CHR bank switching for background
// CHR $0000-$07FF = bank 0
poke(MMC3_BANK_SELECT, 0x00);
poke(MMC3_BANK_DATA, 0x00);
// CHR $0800-$0FFF = bank 2
poke(MMC3_BANK_SELECT, 0x01);
poke(MMC3_BANK_DATA, 0x02);

// select PRG bank 0 at $8000-$9FFF
poke(MMC3_BANK_SELECT, 0x06);
poke(MMC3_BANK_DATA, 0x00);

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("MMC3 BANK SWITCH");

vram_adr(NTADR_A(2, 4));
vram_write("MAPPER 4 ACTIVE");

// use strlen to get string length
byte len = strlen("HELLO MMC3");
vram_adr(NTADR_A(2, 6));
vram_write("STRLEN WORKS!");

// enable PPU rendering
ppu_on_all();

// infinite loop
while (true) ;
