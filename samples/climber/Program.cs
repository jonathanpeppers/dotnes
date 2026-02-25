// climber.c port — A platform game with randomly generated stage
// Uses FamiTone2 library for music and sound effects.

#pragma warning disable CS0649, CS8321, CS0219

using System.Runtime.InteropServices;

// Link FamiTone2 functions
[DllImport("ext")] static extern void music_play(byte song);
[DllImport("ext")] static extern void music_stop();
[DllImport("ext")] static extern void sfx_play(byte sound, byte channel);

// Constants
const byte COLS = 30;
const byte MAX_FLOORS = 20;
const byte GAPSIZE = 4;
const byte BOTTOM_FLOOR_Y = 2;
const byte MAX_ACTORS = 8;
const byte SCREEN_Y_BOTTOM = 208;
const byte ACTOR_MIN_X = 16;
const byte ACTOR_MAX_X = 228;
const byte JUMP_VELOCITY = 18;
const byte CH_FLOOR = 0xf4;
const byte CH_LADDER = 0xd4;
const byte CH_ITEM = 0xc4;
const byte CH_BLANK = 0x20;
const byte SND_START = 0;
const byte SND_HIT = 1;
const byte SND_COIN = 2;
const byte SND_JUMP = 3;
const byte INACTIVE = 0;
const byte STANDING = 1;
const byte WALKING = 2;
const byte CLIMBING = 3;
const byte JUMPING = 4;
const byte FALLING = 5;
const byte PACING = 6;

// Pure utility functions (no captured state)
static byte rndint(byte a, byte b)
{
    return (byte)((byte)(rand8() % (byte)(b - a)) + a);
}

// Setup sounds (no captured state)
static void setup_sounds()
{
    famitone_init("danger_streets_music_data");
    sfx_init("demo_sounds");
    nmi_set_callback("famitone_update");
}

// Setup graphics (no captured state)
static void setup_graphics()
{
    ppu_off();
    oam_clear();
    pal_all(new byte[] {
        0x03,
        0x11, 0x30, 0x27, 0x00,
        0x1c, 0x20, 0x2c, 0x00,
        0x00, 0x10, 0x20, 0x00,
        0x06, 0x16, 0x26, 0x00,
        0x16, 0x35, 0x24, 0x00,
        0x00, 0x37, 0x25, 0x00,
        0x0d, 0x2d, 0x3a, 0x00,
        0x0d, 0x27, 0x2a
    });
    vram_adr(0x2000);
    vram_fill(CH_BLANK, 0x1000);
    vrambuf_clear();
    set_vram_update(updbuf);
    ppu_on_all();
}

// Main entry
setup_sounds();
while (true)
{
    setup_graphics();
    sfx_play(SND_START, 0);

    // === All game state declared here (local scope, no closures) ===

    // Floor data — structure of arrays
    byte[] floor_ypos = new byte[MAX_FLOORS];
    byte[] floor_height = new byte[MAX_FLOORS];
    byte[] floor_gap = new byte[MAX_FLOORS];
    byte[] floor_ladder1 = new byte[MAX_FLOORS];
    byte[] floor_ladder2 = new byte[MAX_FLOORS];

    // Actor data — structure of arrays
    byte[] actor_x = new byte[MAX_ACTORS];
    byte[] actor_floor = new byte[MAX_ACTORS];
    byte[] actor_state = new byte[MAX_ACTORS];
    byte[] actor_dir = new byte[MAX_ACTORS];

    // Metasprite data (ROM tables)
    byte[] playerRStand = new byte[] { 0, 0, 0xd8, 0, 0, 8, 0xd9, 0, 8, 0, 0xda, 0, 8, 8, 0xdb, 0, 128 };
    byte[] playerLStand = new byte[] { 8, 0, 0xd8, 0x40, 8, 8, 0xd9, 0x40, 0, 0, 0xda, 0x40, 0, 8, 0xdb, 0x40, 128 };

    // Draw buffer
    byte[] buf = new byte[COLS];

    // --- make_floors ---
    {
        byte y = BOTTOM_FLOOR_Y;
        for (byte i = 0; i < MAX_FLOORS; i++)
        {
            floor_height[i] = (byte)(rndint(2, 5) * 2);
            if (i >= 5)
                floor_gap[i] = rndint(0, 13);
            else
                floor_gap[i] = 0;
            floor_ladder1[i] = rndint(1, 14);
            floor_ladder2[i] = rndint(1, 14);
            floor_ypos[i] = y;
            y = (byte)(y + floor_height[i]);
        }
        floor_height[MAX_FLOORS - 1] = 15;
        floor_gap[MAX_FLOORS - 1] = 0;
        floor_ladder1[MAX_FLOORS - 1] = 0;
        floor_ladder2[MAX_FLOORS - 1] = 0;
    }

    music_play(0);

    // --- draw_entire_stage (first nametable: 30 rows) ---
    // Pre-compute a row-to-floor mapping to avoid runtime comparisons
    byte[] row_type = new byte[30];
    byte[] row_floor_idx = new byte[30];

    // Mark each row with its floor and type
    // Iterate floors 0-4 (guaranteed to fit in 30 rows)
    for (byte f = 0; f < 5; f++)
    {
        byte fpy = floor_ypos[f];
        // floor bottom row
        row_type[fpy] = 1;
        row_floor_idx[fpy] = f;
        // floor top row
        byte fpy1 = (byte)(fpy + 1);
        row_type[fpy1] = 2;
        row_floor_idx[fpy1] = f;
        // above floor rows (ladders, walls)
        byte fph = floor_height[f];
        for (byte dy = 2; dy < fph; dy++)
        {
            byte r = (byte)(fpy + dy);
            if (r < 30)
            {
                row_type[r] = 3;
                row_floor_idx[r] = f;
            }
        }
    }

    // Draw each row
    byte ntrow = 29;
    for (byte row = 0; row < 30; row++)
    {
        Array.Fill(buf, CH_BLANK);

        byte rtype = row_type[row];
        if (rtype == 1)
        {
            // Floor bottom tiles
            for (byte col = 0; col < COLS; col += 2)
            {
                buf[col] = (byte)(CH_FLOOR + 1);
                buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 3);
            }
        }
        if (rtype == 2)
        {
            // Floor top tiles
            for (byte col = 0; col < COLS; col += 2)
            {
                buf[col] = CH_FLOOR;
                buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 2);
            }
        }
        if (rtype == 3)
        {
            // Above floor — draw ladders
            byte fi = row_floor_idx[row];
            byte lad1 = floor_ladder1[fi];
            if (lad1 != 0)
            {
                byte lc = (byte)(lad1 * 2);
                buf[lc] = CH_LADDER;
                buf[(byte)(lc + 1)] = (byte)(CH_LADDER + 1);
            }
            byte lad2 = floor_ladder2[fi];
            if (lad2 != 0)
            {
                byte lc = (byte)(lad2 * 2);
                buf[lc] = CH_LADDER;
                buf[(byte)(lc + 1)] = (byte)(CH_LADDER + 1);
            }
        }

        vrambuf_put(NTADR_A(0, ntrow), buf, COLS);
        vrambuf_flush();
        ntrow = (byte)(ntrow - 1);
    }

    // --- Initialize player ---
    actor_state[0] = STANDING;
    actor_x[0] = 64;
    actor_floor[0] = 0;
    actor_dir[0] = 0;

    ppu_on_all();

    // --- Game loop ---
    while (true)
    {
        vrambuf_flush();

        // Draw player sprite
        oam_off = 0;
        if (actor_dir[0] == 0)
            oam_meta_spr_pal(actor_x[0], SCREEN_Y_BOTTOM, 3, playerRStand);
        else
            oam_meta_spr_pal(actor_x[0], SCREEN_Y_BOTTOM, 3, playerLStand);
        oam_hide_rest(oam_off);

        // Read input
        byte joy = (byte)pad_poll(0);

        // Player movement
        if ((joy & 0x40) != 0)
        {
            if (actor_x[0] > ACTOR_MIN_X)
                actor_x[0] = (byte)(actor_x[0] - 1);
            actor_dir[0] = 1;
        }
        if ((joy & 0x80) != 0)
        {
            if (actor_x[0] < ACTOR_MAX_X)
                actor_x[0] = (byte)(actor_x[0] + 1);
            actor_dir[0] = 0;
        }
    }
}
