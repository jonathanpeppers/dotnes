/*
Sprite demo.
Animate 32 hardware sprites (reduced from 64 due to zero-page memory constraints).
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/sprites.c
*/

byte[] PALETTE = [
    DarkViolet,                              // screen color
    Azure, White, LightOrange, 0x0,          // background palette 0
    Cyan, LightGray, LightCyan, 0x0,         // background palette 1
    DarkGray, Gray, LightGray, 0x0,          // background palette 2
    DarkRed, Red, LightRed, 0x0,             // background palette 3
    Red, PaleRose, LightMagenta, 0x0,        // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,    // sprite palette 1
    Black, MediumGray, PaleGreen, 0x0,        // sprite palette 2
    Black, LightOrange, LightGreen            // sprite palette 3
];

// actor positions and velocities (32 actors)
byte[] actor_x = new byte[32];
byte[] actor_y = new byte[32];
byte[] actor_dx = new byte[32];
byte[] actor_dy = new byte[32];

// initialize actors with random values
for (byte i = 0; i < 32; i = (byte)(i + 1))
{
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (byte)((rand8() & 7) - 3);
    actor_dy[i] = (byte)((rand8() & 7) - 3);
}

// setup PPU
oam_clear();
pal_all(PALETTE);
ppu_on_all();

// main loop
while (true)
{
    byte oam_id = 0;
    for (byte i = 0; i < 32; i = (byte)(i + 1))
    {
        oam_id = oam_spr(actor_x[i], actor_y[i], i, i, oam_id);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
    }
    if (oam_id != 0)
        oam_hide_rest(oam_id);
    ppu_wait_frame();
}
