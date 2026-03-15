/*
Demonstrates battery-backed SRAM ($6000-$7FFF).
A counter is stored in SRAM and survives power cycles.
Press A to increment, B to reset.
*/

// set palette colors
pal_col(0, 0x0F); // black background
pal_col(1, 0x30); // white text

// read saved counter from SRAM
byte count = peek(SRAM_START);

// draw title
vram_adr(NTADR_A(2, 2));
vram_write("BATTERY SRAM DEMO");

// draw instructions
vram_adr(NTADR_A(2, 6));
vram_write("A = ADD 1");
vram_adr(NTADR_A(2, 8));
vram_write("B = RESET");

// draw the counter label
vram_adr(NTADR_A(2, 12));
vram_write("COUNT:");

// show initial value
update_display(count);

ppu_on_all();

while (true)
{
    ppu_wait_frame();
    byte pad = pad_trigger(0);
    if ((pad & (byte)PAD.A) != 0)
    {
        count++;
        poke(SRAM_START, count);
        update_display(count);
    }
    if ((pad & (byte)PAD.B) != 0)
    {
        count = 0;
        poke(SRAM_START, count);
        update_display(count);
    }
}

static void update_display(byte value)
{
    // display as 3 decimal digits at row 12, col 9
    byte hundreds = (byte)(value / 100);
    byte tens = (byte)((value / 10) % 10);
    byte ones = (byte)(value % 10);

    byte[] buf = new byte[3];
    buf[0] = (byte)(hundreds + 0x30);
    buf[1] = (byte)(tens + 0x30);
    buf[2] = (byte)(ones + 0x30);

    vram_adr(NTADR_A(9, 12));
    vram_write(buf, 3);
}
