/*
Demonstrates pad_trigger() and pad_state() for different input modes.
pad_trigger() detects newly pressed buttons (edge detection).
pad_state() reads currently held buttons.
Press A/B to decrease/increase virtual brightness.
D-pad moves the cursor using held-button state.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/metatrigger.c
*/

// 2x2 metasprite facing right (tiles 0xD8-0xDB)
byte[] playerRStand = [
    0, 0, 0xD8, 0,
    0, 8, 0xD9, 0,
    8, 0, 0xDA, 0,
    8, 8, 0xDB, 0,
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
    0x25, 0x30, 0x27, 0x0,  // background palette 0
    0x1c, 0x20, 0x2c, 0x0,  // background palette 1
    0x00, 0x10, 0x20, 0x0,  // background palette 2
    0x06, 0x16, 0x26, 0x0,  // background palette 3
    0x16, 0x35, 0x24, 0x0,  // sprite palette 0
    0x00, 0x37, 0x25, 0x0,  // sprite palette 1
    0x0d, 0x2d, 0x1a, 0x0,  // sprite palette 2
    0x0d, 0x27, 0x2a         // sprite palette 3
];

// actor positions and velocities (8 actors)
byte[] actor_x = new byte[8];
byte[] actor_y = new byte[8];
byte[] actor_dx = new byte[8];
byte[] actor_dy = new byte[8];

// print instructions
vram_adr(NTADR_A(2, 2));
vram_write("PRESS A/B DEC/INC BRIGHT");
vram_adr(NTADR_A(2, 4));
vram_write("D-PAD USES PAD_STATE");

// setup graphics
oam_hide_rest(0);
pal_all(PALETTE);
ppu_on_all();

// brightness level (4 = normal)
byte vbright = 4;

// initialize actors
byte i = 0;
while (i < 8)
{
    actor_x[i] = (byte)(i * 32);
    actor_y[i] = (byte)(i * 16 + 64);
    actor_dx[i] = 0;
    actor_dy[i] = 0;
    i = (byte)(i + 1);
}

// main loop
while (true)
{
    byte oam_id = 0;

    // pad_trigger detects newly pressed buttons (edge detection)
    byte trig = pad_trigger(0);
    if ((trig & 0x40) != 0)       // PAD_LEFT
        actor_dx[0] = 254;
    else if ((trig & 0x80) != 0)  // PAD_RIGHT
        actor_dx[0] = 2;
    else
        actor_dx[0] = 0;

    // A/B buttons change brightness (trigger = one press per tap)
    if ((trig & 0x01) != 0)       // PAD_A - decrease brightness
        vbright = (byte)(vbright - 1);
    if ((trig & 0x02) != 0)       // PAD_B - increase brightness
        vbright = (byte)(vbright + 1);

    // pad_state reads currently held buttons (continuous)
    byte state = pad_state(0);
    if ((state & 0x10) != 0)      // PAD_UP
        actor_dy[0] = 254;
    else if ((state & 0x20) != 0) // PAD_DOWN
        actor_dy[0] = 2;
    else
        actor_dy[0] = 0;

    // draw and move all actors
    i = 0;
    while (i < 8)
    {
        oam_id = oam_meta_spr(actor_x[i], actor_y[i], oam_id, playerRStand);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
        i = (byte)(i + 1);
    }

    if (oam_id != 0)
        oam_hide_rest(oam_id);
    pal_bright(vbright);
    ppu_wait_frame();
}
