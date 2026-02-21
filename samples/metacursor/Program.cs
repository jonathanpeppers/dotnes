/*
Read from controllers with pad_poll().
Player 1 and 2 each control a metasprite cursor with the d-pad.
Other actors drift around the screen.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/metacursor.c
*/

// 2x2 metasprite facing right (tiles 0xD8-0xDB)
byte[] playerRStand = [
    0, 0, 0xD8, 0,
    0, 8, 0xD9, 0,
    8, 0, 0xDA, 0,
    8, 8, 0xDB, 0,
    128
];

// 2x2 metasprite facing left (flipped horizontally, OAM_FLIP_H = 0x40)
byte[] playerLStand = [
    8, 0, 0xD8, 0x40,
    8, 8, 0xD9, 0x40,
    0, 0, 0xDA, 0x40,
    0, 8, 0xDB, 0x40,
    128
];

// person to save metasprite (tiles 0xBA-0xBD, palette 1)
byte[] personToSave = [
    0, 0, 0xBA, 1,
    0, 8, 0xBB, 1,
    8, 0, 0xBC, 1,
    8, 8, 0xBD, 1,
    128
];

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

// actor positions and velocities (8 actors)
byte[] actor_x = new byte[8];
byte[] actor_y = new byte[8];
byte[] actor_dx = new byte[8];
byte[] actor_dy = new byte[8];

// print instructions
vram_adr(NTADR_A(2, 2));
vram_write("D-PAD TO MOVE CURSOR");

// setup graphics
oam_hide_rest(0);
pal_all(PALETTE);
ppu_on_all();

// initialize actors
for (byte i = 0; i < 8; i = (byte)(i + 1))
{
    actor_x[i] = (byte)(i * 32);
    actor_y[i] = (byte)(i * 16 + 64);
    actor_dx[i] = 0;
    actor_dy[i] = 0;
}

// main loop
while (true)
{
    byte oam_id = 0;

    // poll controller 0 (player 1 controls actor 0)
    PAD pad = pad_poll(0);
    if ((pad & PAD.LEFT) != 0)
        actor_dx[0] = 254; // -2 as unsigned byte
    else if ((pad & PAD.RIGHT) != 0)
        actor_dx[0] = 2;
    else
        actor_dx[0] = 0;

    if ((pad & PAD.UP) != 0)
        actor_dy[0] = 254; // -2
    else if ((pad & PAD.DOWN) != 0)
        actor_dy[0] = 2;
    else
        actor_dy[0] = 0;

    // poll controller 1 (player 2 controls actor 1)
    pad = pad_poll(1);
    if ((pad & PAD.LEFT) != 0)
        actor_dx[1] = 254;
    else if ((pad & PAD.RIGHT) != 0)
        actor_dx[1] = 2;
    else
        actor_dx[1] = 0;

    if ((pad & PAD.UP) != 0)
        actor_dy[1] = 254;
    else if ((pad & PAD.DOWN) != 0)
        actor_dy[1] = 2;
    else
        actor_dy[1] = 0;

    // draw and move all actors
    for (byte i = 0; i < 8; i = (byte)(i + 1))
    {
        oam_id = oam_meta_spr(actor_x[i], actor_y[i], oam_id, playerRStand);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
    }

    if (oam_id != 0)
        oam_hide_rest(oam_id);
    ppu_wait_frame();
}
