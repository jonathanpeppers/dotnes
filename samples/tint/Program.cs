/*
Demonstrates the PPU's tint and monochrome bits.
Use the controller to see different combinations.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/tint.c
*/

byte[] PALETTE = [
    0x2D,                    // screen color
    0x00, 0x30, 0x30, 0x0,   // background palette 0
    0x0C, 0x20, 0x2C, 0x0,   // background palette 1
    0x14, 0x10, 0x25, 0x0,   // background palette 2
    0x17, 0x16, 0x28, 0x0,   // background palette 3
    0x16, 0x35, 0x24, 0x0,   // sprite palette 0
    0x00, 0x37, 0x25, 0x0,   // sprite palette 1
    0x0D, 0x2D, 0x3A, 0x0,   // sprite palette 2
    0x0D, 0x27, 0x2A          // sprite palette 3
];

// setup
oam_clear();
pal_all(PALETTE);

// draw message on all rows
byte i = 0;
while (i < 30)
{
    vram_adr(NTADR_A(1, i));
    vram_write(" A:red B:green \x1e\x1f:blue \x1c\x1d:mono");
    i = (byte)(i + 1);
}

// attributes
vram_adr(0x23c0);
vram_fill(0x00, 8);
vram_fill(0x55, 8);
vram_fill(0xaa, 8);
vram_fill(0xff, 8);
vram_fill(0x11, 8);
vram_fill(0x33, 8);
vram_fill(0xdd, 8);

// enable rendering
ppu_on_all();

// infinite loop
while (true)
{
    PAD pad = pad_poll(0);
    byte mask = MASK.BG;
    if ((pad & PAD.A) != 0)
    {
        mask = (byte)(mask | MASK.TINT_RED);
    }
    if ((pad & PAD.B) != 0)
    {
        mask = (byte)(mask | MASK.TINT_GREEN);
    }
    if ((pad & (PAD.LEFT | PAD.RIGHT)) != 0)
    {
        mask = (byte)(mask | MASK.TINT_BLUE);
    }
    if ((pad & (PAD.UP | PAD.DOWN)) != 0)
    {
        mask = (byte)(mask | MASK.MONO);
    }
    ppu_mask(mask);
}
