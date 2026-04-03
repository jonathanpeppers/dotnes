using static NES.NESColor;

/*
Scoreboard - press A to increment BCD score.
Demonstrates vrambuf_put(), pad_trigger(), ppu_wait_nmi().
Based on: https://github.com/jonathanpeppers/dotnes/issues/121
*/

// palette: dark blue background, white text
pal_col(0, DarkBlue);
pal_col(1, White);
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
set_vram_update(updbuf);
ppu_on_all();

// Track digits as tile indices directly (0x30='0' .. 0x39='9')
// to avoid local+constant arithmetic which the transpiler constant-folds
byte d0 = 0x30;
byte d1 = 0x30;
byte d2 = 0x30;
byte d3 = 0x30;
byte[] digits = new byte[4];

while (true)
{
    PAD trig = pad_trigger(0);
    if ((trig & PAD.A) != 0)
    {
        d3 = (byte)(d3 + 1);
        if (d3 == 0x3A) { d3 = 0x30; d2 = (byte)(d2 + 1); }
        if (d2 == 0x3A) { d2 = 0x30; d1 = (byte)(d1 + 1); }
        if (d1 == 0x3A) { d1 = 0x30; d0 = (byte)(d0 + 1); }
        if (d0 == 0x3A) d0 = 0x30;
    }

    digits[0] = d0;
    digits[1] = d1;
    digits[2] = d2;
    digits[3] = d3;

    vrambuf_put(NTADR_A(17, 13), digits, 4);

    ppu_wait_nmi();
    vrambuf_clear();
}
