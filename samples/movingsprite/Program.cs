byte[] PALETTE = [ 
    0x30,               // screen color

    0x11,0x30,0x27,0x0, // background palette 0
    0x1c,0x20,0x2c,0x0, // background palette 1
    0x00,0x10,0x20,0x0, // background palette 2
    0x06,0x16,0x26,0x0, // background palette 3

    0x14,0x34,0x0d,0x0, // sprite palette 0
    0x00,0x37,0x25,0x0, // sprite palette 1
    0x0d,0x2d,0x3a,0x0, // sprite palette 2
    0x0d,0x27,0x2a      // sprite palette 3
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
    oam_spr(x, y, 0xD8, 0, 0);
    oam_spr((byte)(x + 8), y, 0xDA, 0, 4);
    oam_spr(x, (byte)(y + 8), 0xD9, 0, 8);
    oam_spr((byte)(x + 8), (byte)(y + 8), 0xDB, 0, 12);
}
