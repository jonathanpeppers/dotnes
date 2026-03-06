/*
Scoreboard - press A to increment BCD score.
Demonstrates bcd_add(), vrambuf_put(), and pad_trigger().
Based on: https://github.com/jonathanpeppers/dotnes/issues/121
*/

// palette: black background, white text
pal_col(0, 0x0F);
pal_col(1, 0x30);
pal_bright(4);

bank_spr(0);
bank_bg(0);

// write static text while PPU is off
vram_adr(NTADR_A(10, 13));
vram_write("SCORE  0000");
vram_adr(NTADR_A(5, 16));
vram_write("PRESS A TO ADD SCORE");

// set up VRAM buffer for dynamic updates
vrambuf_clear();
set_vram_update(0x0100);
ppu_on_all();

ushort score = 0;
byte[] digits = new byte[4];

while (true)
{
    byte trig = pad_trigger(0);
    if ((trig & 1) != 0)
    {
        score = bcd_add(score, 1);
    }

    // extract BCD nibbles and convert to ASCII tile indices
    byte lo = (byte)(score & 0xFF);
    byte hi = (byte)((score >> 8) & 0xFF);
    digits[0] = (byte)((hi >> 4) + 0x30);
    digits[1] = (byte)((hi & 0x0F) + 0x30);
    digits[2] = (byte)((lo >> 4) + 0x30);
    digits[3] = (byte)((lo & 0x0F) + 0x30);

    vrambuf_put(NTADR_A(17, 13), digits, 4);

    ppu_wait_nmi();
    vrambuf_clear();
}
