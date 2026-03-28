/*
MMC1 (Mapper 1) demo.
Demonstrates the MMC1 serial shift register API.
Sets up the Control register with PRG/CHR banking modes,
selects PRG and CHR banks, then displays a message.
Uses NESMapper=1 in the project file.
*/

// Configure MMC1 Control register:
// Horizontal mirroring + fix last PRG bank at $C000 + 8KB CHR mode
mmc1_set_mirroring((byte)MMC1Mirror.Horizontal | MMC1_PRG_FIX_LAST);

// Select PRG bank 0 at $8000-$BFFF
mmc1_set_prg_bank(0);

// Select CHR banks 0 and 1
mmc1_set_chr_bank(0, 1);

// Set palette colors
pal_col(0, 0x0F);   // black background
pal_col(1, 0x20);   // white
pal_col(2, 0x16);   // red
pal_col(3, 0x11);   // blue

// Write text to nametable
vram_adr(NTADR_A(6, 4));
vram_write("MMC1  MAPPER 1");

vram_adr(NTADR_A(4, 8));
vram_write("SERIAL SHIFT REG");

vram_adr(NTADR_A(5, 12));
vram_write("PRG BANK  CHR BANK");

vram_adr(NTADR_A(7, 14));
vram_write("0          0  1");

vram_adr(NTADR_A(4, 18));
vram_write("MIRRORING  HORIZ");

vram_adr(NTADR_A(3, 22));
vram_write("PRG FIX LAST  ON");

// Enable PPU rendering
ppu_on_all();

// Infinite loop
while (true) ;
