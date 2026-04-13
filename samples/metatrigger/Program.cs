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
    DarkViolet,                                    // screen color
    LightRose, White, LightOrange, 0x0,            // background palette 0
    Cyan, LightGray, LightCyan, 0x0,               // background palette 1
    DarkGray, Gray, LightGray, 0x0,                // background palette 2
    DarkRed, Red, LightRed, 0x0,                   // background palette 3
    Red, PaleRose, LightMagenta, 0x0,              // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,          // sprite palette 1
    Black, MediumGray, Green, 0x0,                  // sprite palette 2
    Black, LightOrange, LightGreen                  // sprite palette 3
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
    using var frame = oam_begin();

    // pad_trigger detects newly pressed buttons (edge detection)
    PAD trig = pad_trigger(0);
    if ((trig & PAD.LEFT) != 0)
        actor_dx[0] = 254;
    else if ((trig & PAD.RIGHT) != 0)
        actor_dx[0] = 2;
    else
        actor_dx[0] = 0;

    // A/B buttons change brightness (trigger = one press per tap)
    if ((trig & PAD.A) != 0)
        vbright = (byte)(vbright - 1);
    if ((trig & PAD.B) != 0)
        vbright = (byte)(vbright + 1);

    // pad_state reads currently held buttons (continuous)
    PAD state = pad_state(0);
    if ((state & PAD.UP) != 0)
        actor_dy[0] = 254;
    else if ((state & PAD.DOWN) != 0)
        actor_dy[0] = 2;
    else
        actor_dy[0] = 0;

    // draw and move all actors
    for (byte i = 0; i < 8; i = (byte)(i + 1))
    {
        oam_off = oam_meta_spr(actor_x[i], actor_y[i], oam_off, playerRStand);
        actor_x[i] = (byte)(actor_x[i] + actor_dx[i]);
        actor_y[i] = (byte)(actor_y[i] + actor_dy[i]);
    }

    pal_bright(vbright);
    ppu_wait_frame();
}
