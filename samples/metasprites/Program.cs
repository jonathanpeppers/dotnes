/*
Metasprites demo - combines several hardware sprites into larger sprites.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/metasprites.c

Uses 4 hardware sprites in a 2x2 pattern (16x16 pixel metasprites).
16 actors bounce around the screen using oam_meta_spr().
*/

// define a 2x2 metasprite: x-offset, y-offset, tile, attribute; terminated by 128
byte[] metasprite = [
    0, 0, 0xD8, 0,
    0, 8, 0xD9, 0,
    8, 0, 0xDA, 0,
    8, 8, 0xDB, 0,
    128
];

byte[] PALETTE = [
    0x03,                // screen color
    0x11, 0x30, 0x27, 0x0,  // background palette 0
    0x1c, 0x20, 0x2c, 0x0,  // background palette 1
    0x00, 0x10, 0x20, 0x0,  // background palette 2
    0x06, 0x16, 0x26, 0x0,  // background palette 3
    0x16, 0x35, 0x24, 0x0,  // sprite palette 0
    0x00, 0x37, 0x25, 0x0,  // sprite palette 1
    0x0d, 0x2d, 0x3a, 0x0,  // sprite palette 2
    0x0d, 0x27, 0x2a         // sprite palette 3
];

// actor positions and velocities (16 actors)
byte[] actor_x = new byte[16];
byte[] actor_y = new byte[16];
byte[] actor_dx = new byte[16];
byte[] actor_dy = new byte[16];

// setup
oam_clear();
pal_all(PALETTE);
ppu_on_all();

// initialize actors with pseudo-random values
byte i = 0;
while (i < 16)
{
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (byte)((rand8() & 7) - 3);
    actor_dy[i] = (byte)((rand8() & 7) - 3);
    i = (byte)(i + 1);
}

// main loop
while (true)
{
    byte oam_id = 0;
    i = 0;
    while (i < 16)
    {
        oam_id = oam_meta_spr(actor_x[i], actor_y[i], oam_id, metasprite);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
        i = (byte)(i + 1);
    }
    if (oam_id != 0)
        oam_hide_rest(oam_id);
    ppu_wait_frame();
}
