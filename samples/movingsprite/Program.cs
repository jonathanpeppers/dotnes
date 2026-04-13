byte[] PALETTE = [
    White,                                     // screen color

    Azure, White, LightOrange, 0x0,            // background palette 0
    Cyan, LightGray, LightCyan, 0x0,           // background palette 1
    DarkGray, Gray, LightGray, 0x0,            // background palette 2
    DarkRed, Red, LightRed, 0x0,               // background palette 3

    Magenta, PaleMagenta, Black, 0x0,           // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,      // sprite palette 1
    Black, MediumGray, PaleGreen, 0x0,          // sprite palette 2
    Black, LightOrange, LightGreen              // sprite palette 3
];

byte x = 40;
byte y = 40;

pal_all(PALETTE);
ppu_on_all();

while (true)
{
    ppu_wait_nmi();
    
    PAD pad = pad_poll(0);
    
    if ((pad & PAD.LEFT) != 0) x--;
    if ((pad & PAD.RIGHT) != 0) x++;
    if ((pad & PAD.UP) != 0) y--;
    if ((pad & PAD.DOWN) != 0) y++;
    
    // Draw 2x2 sprite (16x16 pixels)
    oam_spr_2x2(x, y, 0xD8, 0xD9, 0xDA, 0xDB, 0, 0);
}
