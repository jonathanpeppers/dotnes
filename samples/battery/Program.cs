/*
Demonstrates battery-backed SRAM ($6000-$7FFF).
A counter is stored in SRAM and survives power cycles.
Press A to increment, B to reset.
*/

// set palette colors
pal_col(0, Black);
pal_col(1, White);

// read saved counter from SRAM
byte count = peek(SRAM_START);

// draw title and instructions
vram_adr(NTADR_A(2, 2));
vram_write("BATTERY SRAM DEMO");
vram_adr(NTADR_A(2, 6));
vram_write("A = ADD 1");
vram_adr(NTADR_A(2, 8));
vram_write("B = RESET");
vram_adr(NTADR_A(2, 12));
vram_write("COUNT:");

// show initial value using vram_put (one tile per digit)
display_count(count);

ppu_on_all();

while (true)
{
    ppu_wait_frame();
    PAD pad = pad_trigger(0);
    if ((pad & PAD.A) != 0)
    {
        count++;
        poke(SRAM_START, count);
        display_count(count);
    }
    if ((pad & PAD.B) != 0)
    {
        count = 0;
        poke(SRAM_START, count);
        display_count(count);
    }
}

static void display_count(byte value)
{
    vram_adr(0x2189);
    vram_put((byte)((value & 0x0F) + 0x30));
}
