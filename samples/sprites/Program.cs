/*
Sprite demo.
Animate 32 hardware sprites (reduced from 64 due to zero-page memory constraints).
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

// actor positions and velocities (32 actors)
byte[] actor_x = new byte[32];
byte[] actor_y = new byte[32];
byte[] actor_dx = new byte[32];
byte[] actor_dy = new byte[32];

// initialize actors with random values
byte i = 0;
while (i < 32)
{
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (byte)((rand8() & 7) - 3);
    actor_dy[i] = (byte)((rand8() & 7) - 3);
    i = (byte)(i + 1);
}

// setup PPU
oam_clear();
pal_all(PALETTE);
ppu_on_all();

// main loop
while (true)
{
    byte oam_id = 0;
    i = 0;
    while (i < 32)
    {
        oam_id = oam_spr(actor_x[i], actor_y[i], i, i, oam_id);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
        i = (byte)(i + 1);
    }
    if (oam_id != 0)
        oam_hide_rest(oam_id);
    ppu_wait_frame();
}
