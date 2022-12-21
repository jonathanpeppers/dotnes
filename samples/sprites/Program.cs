/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=sprites.c

Sprite demo.
Animate all 64 hardware sprites.
*/

/*{pal:"nes",layout:"nes"}*/
byte[] PALETTE = new byte[32]
{
    0x03,               // screen color

    0x11,0x30,0x27,0x0, // background palette 0
    0x1c,0x20,0x2c,0x0, // background palette 1
    0x00,0x10,0x20,0x0, // background palette 2
    0x06,0x16,0x26,0x0, // background palette 3

    0x16,0x35,0x24,0x0, // sprite palette 0
    0x00,0x37,0x25,0x0, // sprite palette 1
    0x0d,0x2d,0x3a,0x0, // sprite palette 2
    0x0d,0x27,0x2a      // sprite palette 3
};

// number of actors
const int NUM_ACTORS = 64; // 64 sprites (maximum)

// actor x/y positions
byte[] actor_x = new byte[NUM_ACTORS]; // horizontal coordinates
byte[] actor_y = new byte[NUM_ACTORS]; // vertical coordinates

// actor x/y deltas per frame (signed)
byte[] actor_dx = new byte[NUM_ACTORS]; // horizontal velocity
byte[] actor_dy = new byte[NUM_ACTORS]; // vertical velocity

byte i;      // actor index
byte oam_id; // sprite ID

// initialize actors with random values
for (i = 0; i < NUM_ACTORS; i++)
{
    actor_x[i] = rand();
    actor_y[i] = rand();
    actor_dx[i] = (byte)((rand() & 7) - 3);
    actor_dy[i] = (byte)((rand() & 7) - 3);
}

// initialize PPU
// clear sprites
oam_clear();
// set palette colors
pal_all(PALETTE);
// turn on PPU
ppu_on_all();

// loop forever
while (true)
{
    // start with OAMid/sprite 0
    oam_id = 0;
    // draw and move all actors
    for (i = 0; i < NUM_ACTORS; i++)
    {
        oam_id = oam_spr(actor_x[i], actor_y[i], i, i, oam_id);
        actor_x[i] += actor_dx[i];
        actor_y[i] += actor_dy[i];
    }
    // hide rest of sprites
    // if we haven't wrapped oam_id around to 0
    if (oam_id != 0)
    {
        oam_hide_rest(oam_id);
    }
    // wait for next frame
    ppu_wait_frame();
}
