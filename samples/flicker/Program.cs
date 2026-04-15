/*
Flicker demo - if you have more objects than will fit into
the 64 hardware sprites, you can omit some sprites each frame.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/flicker.c

Uses 24 actors with 4 sprites each (96 total), but NES only has 64 sprites.
Each frame we cycle through actors starting from where we left off,
creating a flickering effect that shows all sprites over time.
We also use oam_meta_spr_pal() to change the color of each metasprite.
*/

// define a 2x2 metasprite
byte[] metasprite = [
    0, 0, 0xD8, 0,
    0, 8, 0xD9, 0,
    8, 0, 0xDA, 0,
    8, 8, 0xDB, 0,
    128
];

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

// actor positions and velocities (24 actors)
byte[] actor_x = new byte[24];
byte[] actor_y = new byte[24];
byte[] actor_dx = new byte[24];
byte[] actor_dy = new byte[24];

// setup
oam_clear();
pal_all(PALETTE);
ppu_on_all();

// initialize actors with pseudo-random values
byte i;
for (i = 0; i < 24; i = (byte)(i + 1))
{
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (byte)((rand8() & 7) - 3);
    actor_dy[i] = (byte)((rand8() & 7) - 3);
}

// main loop - i persists across frames for flicker effect
while (true)
{
    using (var frame = oam_begin())
    {
        byte count = 0;

        // draw up to 15 actors per frame (15 * 4 = 60 sprites, under the 64 limit)
        while (count < 15)
        {
            // palette color cycles with actor index (i & 3)
            byte pal = (byte)(i & 3);
            frame.meta_spr_pal(actor_x[i], actor_y[i], pal, metasprite);

            // update position
            actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
            actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);

            // advance and wrap around actor array
            i = (byte)(i + 1);
            if (i >= 24)
            {
                i = (byte)(i - 24);
            }

            count = (byte)(count + 1);
        }
    }

    ppu_wait_nmi();
}
