/*
Big sprites demo.
Display 8x16 sprites using the NES PPU's 8x16 sprite mode via oam_size(SpriteSize.Size8x16).
Shows double-height sprites moving around the screen (wrapping via byte overflow).
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/sprites.c
*/

byte[] PALETTE = [
    0x03,                    // screen color
    0x11, 0x30, 0x27, 0x0,  // background palette 0
    0x1c, 0x20, 0x2c, 0x0,  // background palette 1
    0x00, 0x10, 0x20, 0x0,  // background palette 2
    0x06, 0x16, 0x26, 0x0,  // background palette 3
    0x16, 0x35, 0x24, 0x0,  // sprite palette 0
    0x00, 0x37, 0x25, 0x0,  // sprite palette 1
    0x0d, 0x2d, 0x3a, 0x0,  // sprite palette 2
    0x0d, 0x27, 0x2a         // sprite palette 3
];

// actor positions and velocities (8 big sprites)
byte[] actor_x = new byte[8];
byte[] actor_y = new byte[8];
byte[] actor_dx = new byte[8];
byte[] actor_dy = new byte[8];

// initialize actors with random values
for (byte i = 0; i < 8; i = (byte)(i + 1))
{
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (byte)((rand8() & 3) - 1);
    actor_dy[i] = (byte)((rand8() & 3) - 1);
}

// setup PPU
oam_clear();
pal_all(PALETTE);
oam_size(SpriteSize.Size8x16);     // enable 8x16 sprite mode
bank_spr(0);
ppu_on_all();

// main loop
while (true)
{
    byte oam_id = 0;
    for (byte i = 0; i < 8; i = (byte)(i + 1))
    {
        // In 8x16 mode, use even tile numbers (top tile = N, bottom tile = N+1)
        oam_id = oam_spr(actor_x[i], actor_y[i], (byte)(i * 2), i, oam_id);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
    }
    if (oam_id != 0)
        oam_hide_rest(oam_id);
    ppu_wait_frame();
}
