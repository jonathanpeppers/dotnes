using static NES.NESColor;

/*
Demonstrates CNROM (mapper 3) CHR bank switching.
Sets the iNES header to mapper 3 with 2 CHR banks (16 KB total).
Pressing A on the controller toggles between CHR bank 0 and CHR bank 1,
showing different tile graphics on screen.
*/

// set palette colors
pal_col(0, Black);
pal_col(1, White);
pal_col(2, Red);
pal_col(3, Blue);

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("CNROM BANK SWITCH");

vram_adr(NTADR_A(2, 4));
vram_write("PRESS A TO SWITCH");

// fill a few rows with sequential tiles to visualize the CHR bank
vram_adr(NTADR_A(2, 8));
byte i = 0;
while (i < 28)
{
    vram_put(i);
    i = (byte)(i + 1);
}

vram_adr(NTADR_A(2, 10));
i = 28;
while (i < 56)
{
    vram_put(i);
    i = (byte)(i + 1);
}

// enable PPU rendering
ppu_on_all();

byte bank = 0;

// main loop
while (true)
{
    ppu_wait_frame();
    PAD pad = pad_poll(0);
    if ((pad & PAD.A) != 0)
    {
        bank = (byte)((bank + 1) & 1);
        cnrom_set_chr_bank(bank);
    }
}
