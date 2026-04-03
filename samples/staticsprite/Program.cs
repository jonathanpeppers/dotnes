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

pal_all(PALETTE);
oam_spr(40, 40, 0xD8, 0, 0);
oam_spr(48, 40, 0xDA, 0, 4);
oam_spr(40, 48, 0xD9, 0, 8);
oam_spr(48, 48, 0xDB, 0, 12);
ppu_on_all();

while (true) ;
