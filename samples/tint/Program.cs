/*
Demonstrates the PPU's tint and monochrome bits.
Use the controller to see different combinations.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/tint.c
*/

byte[] PALETTE = [
    MediumGray,                              // screen color
    DarkGray, White, White, 0x0,             // background palette 0
    DarkCyan, LightGray, LightCyan, 0x0,     // background palette 1
    Magenta, Gray, LightRose, 0x0,           // background palette 2
    Orange, Red, Yellow, 0x0,                // background palette 3
    Red, PaleRose, LightMagenta, 0x0,        // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,    // sprite palette 1
    0x0D, MediumGray, PaleGreen, 0x0,        // sprite palette 2
    0x0D, LightOrange, LightGreen            // sprite palette 3
];

// setup
pal_all(PALETTE);
oam_clear();

// fill nametable with the message text on every row
vram_adr(NTADR_A(0, 0));
for (byte i = 0; i < 30; i++)
{
    vram_write("  A:red B:green \x1e\x1f:blue \x1c\x1d:mono ");
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

// main loop
MASK mask = MASK.BG;
while (true)
{
    ppu_wait_nmi();
    PAD pad = pad_poll(0);
    if ((pad & PAD.A) != 0)
        mask = mask | MASK.TINT_RED;
    if ((pad & PAD.B) != 0)
        mask = mask | MASK.TINT_GREEN;
    if ((pad & PAD.LEFT) != 0)
        mask = mask | MASK.TINT_BLUE;
    if ((pad & PAD.RIGHT) != 0)
        mask = mask | MASK.TINT_BLUE;
    if ((pad & PAD.UP) != 0)
        mask = mask | MASK.MONO;
    if ((pad & PAD.DOWN) != 0)
        mask = mask | MASK.MONO;
    ppu_mask(mask);
    mask = MASK.BG;
}
