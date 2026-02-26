// climber.c port — A platform game with randomly generated stage
// Uses FamiTone2 library for music and sound effects.
// Vertical scrolling (horizontal mirroring) with offscreen row updates.

#pragma warning disable CS0649, CS8321, CS0219

using System.Runtime.InteropServices;

// Link FamiTone2 functions
[DllImport("ext")] static extern void music_play(byte song);
[DllImport("ext")] static extern void music_stop();
[DllImport("ext")] static extern void sfx_play(byte sound, byte channel);

// Constants
const byte COLS = 30;
const byte ROWS = 60;
const byte MAX_FLOORS = 20;
const byte GAPSIZE = 4;
const byte BOTTOM_FLOOR_Y = 2;
const byte MAX_ACTORS = 8;
const byte SCREEN_Y_BOTTOM = 208;
const byte ACTOR_MIN_X = 16;
const byte ACTOR_MAX_X = 228;
const byte ACTOR_SCROLL_UP_Y = 110;
const byte ACTOR_SCROLL_DOWN_Y = 140;
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
const byte PAD_A = 0x01;
const byte PAD_LEFT = 0x40;
const byte PAD_RIGHT = 0x80;
const byte PAD_UP = 0x08;
const byte PAD_DOWN = 0x04;

// Pure utility function (no captured state)
static byte rndint(byte a, byte b)
{
    return (byte)((byte)(rand8() % (byte)(b - a)) + a);
}

// Setup sounds
static void setup_sounds()
{
    famitone_init("danger_streets_music_data");
    sfx_init("demo_sounds");
    nmi_set_callback("famitone_update");
}

// Setup graphics
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

    // === All game state (local scope) ===

    // Floor data (SoA)
    byte[] floor_ypos = new byte[MAX_FLOORS];
    byte[] floor_height = new byte[MAX_FLOORS];
    byte[] floor_gap = new byte[MAX_FLOORS];
    byte[] floor_ladder1 = new byte[MAX_FLOORS];
    byte[] floor_ladder2 = new byte[MAX_FLOORS];
    byte[] floor_objtype = new byte[MAX_FLOORS];
    byte[] floor_objpos = new byte[MAX_FLOORS];

    // Actor data (SoA) — 16-bit Y stored as lo/hi byte pairs
    byte[] actor_x = new byte[MAX_ACTORS];
    byte[] actor_yy_lo = new byte[MAX_ACTORS];
    byte[] actor_yy_hi = new byte[MAX_ACTORS];
    byte[] actor_floor = new byte[MAX_ACTORS];
    byte[] actor_state = new byte[MAX_ACTORS];
    byte[] actor_yvel = new byte[MAX_ACTORS];
    byte[] actor_xvel = new byte[MAX_ACTORS];
    byte[] actor_dir = new byte[MAX_ACTORS];
    byte[] actor_name = new byte[MAX_ACTORS];
    byte[] actor_pal = new byte[MAX_ACTORS];
    byte[] actor_onscreen = new byte[MAX_ACTORS];

    // Scroll state (16-bit as lo/hi)
    byte scroll_yy_lo = 0;
    byte scroll_yy_hi = 0;
    byte scroll_tile_y = 0;
    byte player_screen_y = 0;
    byte score = 0;
    byte vbright = 4;

    // Metasprite data (ROM tables)
    byte[] playerRStand = new byte[] { 0, 0, 0xd8, 0, 0, 8, 0xd9, 0, 8, 0, 0xda, 0, 8, 8, 0xdb, 0, 128 };
    byte[] playerRRun1 = new byte[] { 0, 0, 0xdc, 0, 0, 8, 0xdd, 0, 8, 0, 0xde, 0, 8, 8, 0xdf, 0, 128 };
    byte[] playerRRun2 = new byte[] { 0, 0, 0xe0, 0, 0, 8, 0xe1, 0, 8, 0, 0xe2, 0, 8, 8, 0xe3, 0, 128 };
    byte[] playerRRun3 = new byte[] { 0, 0, 0xe4, 0, 0, 8, 0xe5, 0, 8, 0, 0xe6, 0, 8, 8, 0xe7, 0, 128 };
    byte[] playerRJump = new byte[] { 0, 0, 0xe8, 0, 0, 8, 0xe9, 0, 8, 0, 0xea, 0, 8, 8, 0xeb, 0, 128 };
    byte[] playerRClimb = new byte[] { 0, 0, 0xec, 0, 0, 8, 0xed, 0, 8, 0, 0xee, 0, 8, 8, 0xef, 0, 128 };
    byte[] playerRSad = new byte[] { 0, 0, 0xf0, 0, 0, 8, 0xf1, 0, 8, 0, 0xf2, 0, 8, 8, 0xf3, 0, 128 };
    byte[] playerLStand = new byte[] { 8, 0, 0xd8, 0x40, 8, 8, 0xd9, 0x40, 0, 0, 0xda, 0x40, 0, 8, 0xdb, 0x40, 128 };
    byte[] playerLRun1 = new byte[] { 8, 0, 0xdc, 0x40, 8, 8, 0xdd, 0x40, 0, 0, 0xde, 0x40, 0, 8, 0xdf, 0x40, 128 };
    byte[] playerLRun2 = new byte[] { 8, 0, 0xe0, 0x40, 8, 8, 0xe1, 0x40, 0, 0, 0xe2, 0x40, 0, 8, 0xe3, 0x40, 128 };
    byte[] playerLRun3 = new byte[] { 8, 0, 0xe4, 0x40, 8, 8, 0xe5, 0x40, 0, 0, 0xe6, 0x40, 0, 8, 0xe7, 0x40, 128 };
    byte[] playerLJump = new byte[] { 8, 0, 0xe8, 0x40, 8, 8, 0xe9, 0x40, 0, 0, 0xea, 0x40, 0, 8, 0xeb, 0x40, 128 };
    byte[] playerLClimb = new byte[] { 8, 0, 0xec, 0x40, 8, 8, 0xed, 0x40, 0, 0, 0xee, 0x40, 0, 8, 0xef, 0x40, 128 };
    byte[] playerLSad = new byte[] { 8, 0, 0xf0, 0x40, 8, 8, 0xf1, 0x40, 0, 0, 0xf2, 0x40, 0, 8, 0xf3, 0x40, 128 };
    byte[] personToSave = new byte[] { 0, 0, 0xba, 3, 0, 8, 0xbc, 0, 8, 0, 0xbb, 3, 8, 8, 0xbd, 0, 128 };

    // Buffers
    byte[] buf = new byte[COLS];
    byte[] attrbuf = new byte[8];

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
            if (i > 0)
            {
                floor_objtype[i] = rndint(1, 4);
                floor_objpos[i] = rndint(1, 14);
            }
            floor_ypos[i] = y;
            y = (byte)(y + floor_height[i]);
        }
        floor_height[MAX_FLOORS - 1] = 15;
        floor_gap[MAX_FLOORS - 1] = 0;
        floor_ladder1[MAX_FLOORS - 1] = 0;
        floor_ladder2[MAX_FLOORS - 1] = 0;
        floor_objtype[MAX_FLOORS - 1] = 0;
    }

    music_play(0);

    // Initialize player
    actor_state[0] = STANDING;
    actor_x[0] = 64;
    actor_floor[0] = 0;
    actor_pal[0] = 3;
    {
        ushort pyy = (ushort)(floor_ypos[0] * 8 + 16);
        actor_yy_lo[0] = (byte)pyy;
        actor_yy_hi[0] = (byte)(pyy >> 8);
    }

    // --- draw_entire_stage (60 rows, fills both nametables) ---
    for (byte rh = 0; rh < ROWS; rh++)
    {
        Array.Fill(buf, (byte)0);
        byte found_dy = 0;
        for (byte f = 0; f < MAX_FLOORS; f++)
        {
            byte dy = (byte)(rh - floor_ypos[f]);
            if (dy >= 253) dy = 0;
            if (dy < floor_height[f])
            {
                found_dy = dy;
                if (dy <= 1)
                {
                    for (byte col = 0; col < COLS; col += 2)
                    {
                        if (dy != 0)
                        {
                            buf[col] = CH_FLOOR;
                            buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 2);
                        }
                        else
                        {
                            buf[col] = (byte)(CH_FLOOR + 1);
                            buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 3);
                        }
                    }
                    if (floor_gap[f] != 0)
                    {
                        byte gstart = (byte)(floor_gap[f] * 2);
                        for (byte g = 0; g < GAPSIZE; g++)
                            buf[(byte)(gstart + g)] = 0;
                    }
                }
                else
                {
                    if (f < MAX_FLOORS - 1)
                    {
                        buf[0] = (byte)(CH_FLOOR + 1);
                        buf[COLS - 1] = CH_FLOOR;
                    }
                    if (floor_ladder1[f] != 0)
                    {
                        byte lc = (byte)(floor_ladder1[f] * 2);
                        buf[lc] = CH_LADDER;
                        buf[(byte)(lc + 1)] = (byte)(CH_LADDER + 1);
                    }
                    if (floor_ladder2[f] != 0)
                    {
                        byte lc = (byte)(floor_ladder2[f] * 2);
                        buf[lc] = CH_LADDER;
                        buf[(byte)(lc + 1)] = (byte)(CH_LADDER + 1);
                    }
                }
                if (floor_objtype[f] != 0)
                {
                    byte ch = (byte)(floor_objtype[f] * 4 + CH_ITEM);
                    if (dy == 2)
                    {
                        buf[(byte)(floor_objpos[f] * 2)] = (byte)(ch + 1);
                        buf[(byte)(floor_objpos[f] * 2 + 1)] = (byte)(ch + 3);
                    }
                    if (dy == 3)
                    {
                        buf[(byte)(floor_objpos[f] * 2)] = ch;
                        buf[(byte)(floor_objpos[f] * 2 + 1)] = (byte)(ch + 2);
                    }
                }
                // Spawn actors on this floor
                if (dy == 0 && f >= 2)
                {
                    byte aidx = (byte)((byte)(f % (byte)(MAX_ACTORS - 1)) + 1);
                    if (actor_onscreen[aidx] == 0)
                    {
                        actor_state[aidx] = STANDING;
                        actor_name[aidx] = 1; // ACTOR_ENEMY
                        actor_x[aidx] = rand8();
                        ushort ayy = (ushort)(floor_ypos[f] * 8 + 16);
                        actor_yy_lo[aidx] = (byte)ayy;
                        actor_yy_hi[aidx] = (byte)(ayy >> 8);
                        actor_floor[aidx] = f;
                        actor_onscreen[aidx] = 1;
                        if (f == MAX_FLOORS - 1)
                        {
                            actor_name[aidx] = 2; // ACTOR_RESCUE
                            actor_state[aidx] = PACING;
                            actor_x[aidx] = 0;
                            actor_pal[aidx] = 1;
                        }
                    }
                }
                break;
            }
        }
        // Nametable row address
        byte rowy = (byte)((byte)(ROWS - 1) - (byte)(rh % ROWS));
        ushort addr;
        if (rowy < 30)
            addr = NTADR_A(1, rowy);
        else
            addr = NTADR_C(1, (byte)(rowy - 30));

        // Attribute table (every 4 tile rows)
        byte tile_y_attr = (byte)(rowy < 30 ? rowy : (byte)(rowy - 30));
        if ((tile_y_attr & 3) == 0)
        {
            byte a;
            if (found_dy == 1) a = 0x05;
            else if (found_dy == 3) a = 0x50;
            else a = 0x00;
            Array.Fill(attrbuf, a);
            byte ato = (byte)((tile_y_attr >> 2) << 3);
            // Base: 0x23C0 (nametable A) or 0x2BC0 (nametable C)
            if (rowy < 30)
                vrambuf_put((ushort)(0x23C0 + ato), attrbuf, 8);
            else
                vrambuf_put((ushort)(0x2BC0 + ato), attrbuf, 8);
        }
        vrambuf_put(addr, buf, COLS);
        vrambuf_flush();
    }

    // --- Main game loop ---
    while (actor_floor[0] != MAX_FLOORS - 1)
    {
        vrambuf_flush();

        // --- Draw all sprites ---
        oam_off = 0;
        for (byte ai = 0; ai < MAX_ACTORS; ai++)
        {
            if (actor_state[ai] == INACTIVE)
            {
                actor_onscreen[ai] = 0;
                continue;
            }
            // screen_y = SCREEN_Y_BOTTOM - (actor_yy - scroll_yy), byte arithmetic
            byte rel_lo = (byte)(actor_yy_lo[ai] - scroll_yy_lo);
            byte rel_hi = (byte)(actor_yy_hi[ai] - scroll_yy_hi);
            if (actor_yy_lo[ai] < scroll_yy_lo) rel_hi = (byte)(rel_hi - 1);
            if (rel_hi != 0) { actor_onscreen[ai] = 0; continue; }
            byte screen_y = (byte)(SCREEN_Y_BOTTOM - rel_lo);
            if (screen_y > 224) { actor_onscreen[ai] = 0; continue; }

            byte dir = actor_dir[ai];
            byte st = actor_state[ai];
            if (st == STANDING)
            {
                if (dir != 0) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLStand);
                else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRStand);
            }
            if (st == WALKING)
            {
                byte frame = (byte)((actor_x[ai] >> 1) & 7);
                if (dir != 0)
                {
                    if (frame < 3) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLRun1);
                    else if (frame < 6) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLRun2);
                    else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLRun3);
                }
                else
                {
                    if (frame < 3) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRRun1);
                    else if (frame < 6) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRRun2);
                    else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRRun3);
                }
            }
            if (st == JUMPING)
            {
                if (dir != 0) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLJump);
                else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRJump);
            }
            if (st == FALLING)
            {
                if (dir != 0) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLSad);
                else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRSad);
            }
            if (st == CLIMBING)
            {
                if ((actor_yy_lo[ai] & 4) != 0) oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerLClimb);
                else oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], playerRClimb);
            }
            if (st == PACING)
                oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], personToSave);
            actor_onscreen[ai] = 1;
            if (ai == 0) player_screen_y = screen_y;
        }
        // Scoreboard
        oam_off = oam_spr(24, 24, (byte)(0x30 + (score >> 4)), 2, oam_off);
        oam_off = oam_spr(32, 24, (byte)(0x30 + (score & 0x0f)), 2, oam_off);
        oam_hide_rest(oam_off);

        // --- Player movement ---
        byte joy = (byte)pad_poll(0);
        {
            byte pi = 0;
            byte pf = actor_floor[pi];
            byte ps = actor_state[pi];
            ushort floor_yy = (ushort)(floor_ypos[pf] * 8 + 16);
            byte fyy_lo = (byte)floor_yy;
            byte fyy_hi = (byte)(floor_yy >> 8);
            byte ceil_y_base = (byte)(floor_ypos[pf] + floor_height[pf]);
            ushort ceil_yy = (ushort)(ceil_y_base * 8 + 16);
            byte cyy_lo = (byte)ceil_yy;
            byte cyy_hi = (byte)(ceil_yy >> 8);

            if (ps == STANDING || ps == WALKING)
            {
                if ((joy & PAD_A) != 0)
                {
                    actor_state[pi] = JUMPING;
                    actor_xvel[pi] = 0;
                    actor_yvel[pi] = JUMP_VELOCITY;
                    if ((joy & PAD_LEFT) != 0) actor_xvel[pi] = 0xff;
                    if ((joy & PAD_RIGHT) != 0) actor_xvel[pi] = 1;
                    sfx_play(SND_JUMP, 0);
                }
                else if ((joy & PAD_LEFT) != 0)
                {
                    actor_x[pi] = (byte)(actor_x[pi] - 1);
                    actor_dir[pi] = 1;
                    actor_state[pi] = WALKING;
                }
                else if ((joy & PAD_RIGHT) != 0)
                {
                    actor_x[pi] = (byte)(actor_x[pi] + 1);
                    actor_dir[pi] = 0;
                    actor_state[pi] = WALKING;
                }
                else if ((joy & PAD_UP) != 0)
                {
                    byte lx = 0;
                    if (floor_ladder1[pf] != 0)
                    {
                        byte ladx = (byte)(floor_ladder1[pf] * 16);
                        if ((byte)(actor_x[pi] - ladx) < 16) lx = ladx;
                    }
                    if (lx == 0 && floor_ladder2[pf] != 0)
                    {
                        byte ladx = (byte)(floor_ladder2[pf] * 16);
                        if ((byte)(actor_x[pi] - ladx) < 16) lx = ladx;
                    }
                    if (lx != 0)
                    {
                        actor_x[pi] = (byte)(lx + 8);
                        actor_state[pi] = CLIMBING;
                    }
                }
                else if ((joy & PAD_DOWN) != 0)
                {
                    if (pf > 0)
                    {
                        byte bf = (byte)(pf - 1);
                        byte lx = 0;
                        if (floor_ladder1[bf] != 0)
                        {
                            byte ladx = (byte)(floor_ladder1[bf] * 16);
                            if ((byte)(actor_x[pi] - ladx) < 16) lx = ladx;
                        }
                        if (lx == 0 && floor_ladder2[bf] != 0)
                        {
                            byte ladx = (byte)(floor_ladder2[bf] * 16);
                            if ((byte)(actor_x[pi] - ladx) < 16) lx = ladx;
                        }
                        if (lx != 0)
                        {
                            actor_x[pi] = (byte)(lx + 8);
                            actor_state[pi] = CLIMBING;
                            actor_floor[pi] = bf;
                        }
                    }
                }
                else
                {
                    actor_state[pi] = STANDING;
                }
            }

            if (ps == CLIMBING)
            {
                if ((joy & PAD_UP) != 0)
                {
                    if (actor_yy_hi[pi] > cyy_hi || (actor_yy_hi[pi] == cyy_hi && actor_yy_lo[pi] >= cyy_lo))
                    {
                        actor_floor[pi] = (byte)(pf + 1);
                        actor_state[pi] = STANDING;
                    }
                    else
                    {
                        actor_yy_lo[pi] = (byte)(actor_yy_lo[pi] + 1);
                        if (actor_yy_lo[pi] == 0) actor_yy_hi[pi] = (byte)(actor_yy_hi[pi] + 1);
                    }
                }
                else if ((joy & PAD_DOWN) != 0)
                {
                    if (actor_yy_hi[pi] < fyy_hi || (actor_yy_hi[pi] == fyy_hi && actor_yy_lo[pi] <= fyy_lo))
                    {
                        actor_state[pi] = STANDING;
                    }
                    else
                    {
                        if (actor_yy_lo[pi] == 0) actor_yy_hi[pi] = (byte)(actor_yy_hi[pi] - 1);
                        actor_yy_lo[pi] = (byte)(actor_yy_lo[pi] - 1);
                    }
                }
            }

            if (ps == JUMPING || ps == FALLING)
            {
                actor_x[pi] = (byte)(actor_x[pi] + actor_xvel[pi]);
                // yvel / 4 (signed): extract sign, shift, apply
                byte absvel = actor_yvel[pi];
                byte neg = 0;
                if (absvel >= 128) { absvel = (byte)(0 - absvel); neg = 1; }
                byte dv = (byte)(absvel >> 2);
                if (neg != 0)
                {
                    byte prev_lo = actor_yy_lo[pi];
                    actor_yy_lo[pi] = (byte)(prev_lo - dv);
                    if (actor_yy_lo[pi] > prev_lo) actor_yy_hi[pi] = (byte)(actor_yy_hi[pi] - 1);
                }
                else
                {
                    byte prev_lo = actor_yy_lo[pi];
                    actor_yy_lo[pi] = (byte)(prev_lo + dv);
                    if (actor_yy_lo[pi] < prev_lo) actor_yy_hi[pi] = (byte)(actor_yy_hi[pi] + 1);
                }
                actor_yvel[pi] = (byte)(actor_yvel[pi] - 1);
                // Landed?
                if (actor_yy_hi[pi] < fyy_hi || (actor_yy_hi[pi] == fyy_hi && actor_yy_lo[pi] <= fyy_lo))
                {
                    actor_yy_lo[pi] = fyy_lo;
                    actor_yy_hi[pi] = fyy_hi;
                    actor_state[pi] = STANDING;
                }
            }

            // Clamp X
            if (actor_x[pi] > ACTOR_MAX_X) actor_x[pi] = ACTOR_MAX_X;
            if (actor_x[pi] < ACTOR_MIN_X) actor_x[pi] = ACTOR_MIN_X;

            // Check gap fall
            ps = actor_state[pi];
            if (ps == STANDING || ps == WALKING)
            {
                byte gap = floor_gap[pf];
                if (gap != 0)
                {
                    byte gx1 = (byte)(gap * 16 + 4);
                    if (actor_x[pi] > gx1 && actor_x[pi] < (byte)(gx1 + GAPSIZE * 8 - 4))
                    {
                        if (pf > 0) actor_floor[pi] = (byte)(pf - 1);
                        actor_state[pi] = FALLING;
                        actor_xvel[pi] = 0;
                        actor_yvel[pi] = 0;
                    }
                }
            }

            // Pickup object
            if (actor_state[pi] <= WALKING && floor_objtype[pf] != 0)
            {
                byte objx = (byte)(floor_objpos[pf] * 16);
                if (actor_x[pi] >= objx && actor_x[pi] < (byte)(objx + 16))
                {
                    byte ot = floor_objtype[pf];
                    floor_objtype[pf] = 0;
                    if (ot == 1)
                    {
                        if (pf > 0) actor_floor[pi] = (byte)(pf - 1);
                        actor_state[pi] = FALLING;
                        actor_xvel[pi] = 0;
                        actor_yvel[pi] = 0;
                        sfx_play(SND_HIT, 0);
                        vbright = 8;
                    }
                    else
                    {
                        score = (byte)bcd_add(score, 1);
                        sfx_play(SND_COIN, 0);
                    }
                }
            }

            // Scroll check
            if (player_screen_y < ACTOR_SCROLL_UP_Y)
            {
                byte new_lo = (byte)(scroll_yy_lo + 1);
                if (new_lo == 0) scroll_yy_hi = (byte)(scroll_yy_hi + 1);
                scroll_yy_lo = new_lo;
            }
            if (player_screen_y > ACTOR_SCROLL_DOWN_Y && (scroll_yy_lo != 0 || scroll_yy_hi != 0))
            {
                if (scroll_yy_lo == 0) scroll_yy_hi = (byte)(scroll_yy_hi - 1);
                scroll_yy_lo = (byte)(scroll_yy_lo - 1);
            }
        }

        // --- Move enemies ---
        for (byte ei = 1; ei < MAX_ACTORS; ei++)
        {
            if (actor_state[ei] == INACTIVE || actor_state[ei] == PACING)
                continue;
            byte ej = rand8();
            byte ef = actor_floor[ei];
            byte es = actor_state[ei];
            ushort efloor_yy = (ushort)(floor_ypos[ef] * 8 + 16);
            byte efyy_lo = (byte)efloor_yy;
            byte efyy_hi = (byte)(efloor_yy >> 8);

            if (es == STANDING || es == WALKING)
            {
                if ((ej & PAD_LEFT) != 0)
                {
                    actor_x[ei] = (byte)(actor_x[ei] - 1);
                    actor_dir[ei] = 1;
                    actor_state[ei] = WALKING;
                }
                else if ((ej & PAD_RIGHT) != 0)
                {
                    actor_x[ei] = (byte)(actor_x[ei] + 1);
                    actor_dir[ei] = 0;
                    actor_state[ei] = WALKING;
                }
                else
                {
                    actor_state[ei] = STANDING;
                }
                if (actor_x[ei] > ACTOR_MAX_X) actor_x[ei] = ACTOR_MAX_X;
                if (actor_x[ei] < ACTOR_MIN_X) actor_x[ei] = ACTOR_MIN_X;
                byte egap = floor_gap[ef];
                if (egap != 0)
                {
                    byte gx1 = (byte)(egap * 16 + 4);
                    if (actor_x[ei] > gx1 && actor_x[ei] < (byte)(gx1 + GAPSIZE * 8 - 4))
                    {
                        if (ef > 0) actor_floor[ei] = (byte)(ef - 1);
                        actor_state[ei] = FALLING;
                        actor_xvel[ei] = 0;
                        actor_yvel[ei] = 0;
                    }
                }
            }
            if (es == JUMPING || es == FALLING)
            {
                actor_x[ei] = (byte)(actor_x[ei] + actor_xvel[ei]);
                byte eabsvel = actor_yvel[ei];
                byte eneg = 0;
                if (eabsvel >= 128) { eabsvel = (byte)(0 - eabsvel); eneg = 1; }
                byte edv = (byte)(eabsvel >> 2);
                if (eneg != 0)
                {
                    byte prev_lo = actor_yy_lo[ei];
                    actor_yy_lo[ei] = (byte)(prev_lo - edv);
                    if (actor_yy_lo[ei] > prev_lo) actor_yy_hi[ei] = (byte)(actor_yy_hi[ei] - 1);
                }
                else
                {
                    byte prev_lo = actor_yy_lo[ei];
                    actor_yy_lo[ei] = (byte)(prev_lo + edv);
                    if (actor_yy_lo[ei] < prev_lo) actor_yy_hi[ei] = (byte)(actor_yy_hi[ei] + 1);
                }
                actor_yvel[ei] = (byte)(actor_yvel[ei] - 1);
                if (actor_yy_hi[ei] < efyy_hi || (actor_yy_hi[ei] == efyy_hi && actor_yy_lo[ei] <= efyy_lo))
                {
                    actor_yy_lo[ei] = efyy_lo;
                    actor_yy_hi[ei] = efyy_hi;
                    actor_state[ei] = STANDING;
                }
            }
        }

        // --- Collision check ---
        if (actor_state[0] != FALLING && actor_floor[0] > 0)
        {
            for (byte ci = 1; ci < MAX_ACTORS; ci++)
            {
                if (actor_onscreen[ci] != 0 && actor_floor[ci] == actor_floor[0])
                {
                    byte dx = (byte)(actor_x[0] - actor_x[ci]);
                    if (dx >= 248) dx = (byte)(0 - dx);
                    byte dyl = (byte)(actor_yy_lo[0] - actor_yy_lo[ci]);
                    if (dyl >= 248) dyl = (byte)(0 - dyl);
                    if (dx < 8 && dyl < 8)
                    {
                        if (actor_floor[0] > 0) actor_floor[0] = (byte)(actor_floor[0] - 1);
                        actor_state[0] = FALLING;
                        actor_xvel[0] = 0;
                        actor_yvel[0] = 0;
                        sfx_play(SND_HIT, 0);
                        vbright = 8;
                        break;
                    }
                }
            }
        }

        // Flash effect
        if (vbright > 4)
        {
            vbright = (byte)(vbright - 1);
            pal_bright(vbright);
        }

        // Set scroll registers: scroll(0, 479 - ((yy + 224) % 480))
        // Computed using byte-level arithmetic (yy = scroll_yy_hi:scroll_yy_lo)
        {
            byte sv_lo;
            byte sv_hi;
            if (scroll_yy_hi == 0)
            {
                // yy < 256: yy + 224 < 480, so no modulo needed
                // scroll_val = 255 - scroll_yy_lo
                sv_lo = (byte)(255 - scroll_yy_lo);
                sv_hi = 0;
            }
            else
            {
                // yy >= 256: yy + 224 >= 480, subtract 480
                // scroll_val = 735 - yy (735 = 0x02DF)
                sv_lo = (byte)(0xDF - scroll_yy_lo);
                sv_hi = 2;
                if (scroll_yy_lo > 0xDF) sv_hi = (byte)(sv_hi - 1);
                sv_hi = (byte)(sv_hi - scroll_yy_hi);
            }
            // Pass to scroll: x=0, y=sv_hi:sv_lo (16-bit)
            // Construct ushort: sv_lo + sv_hi * 256
            if (sv_hi == 0)
            {
                scroll(0, sv_lo);
            }
            else
            {
                // byte + ushort constant produces 16-bit result
                ushort scroll_val = (ushort)(sv_lo + 256);
                scroll(0, scroll_val);
            }
        }
    }

    // Player reached top floor
    music_stop();
    delay(100);
}
