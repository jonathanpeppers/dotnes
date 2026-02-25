// climber.c port — A platform game with randomly generated stage
// Uses FamiTone2 library for music and sound effects.
// Scrolls vertically (horizontal mirroring) with offscreen nametable updates.

#pragma warning disable CS0649, CS8321, CS0219 // Field never assigned, local function never used, variable assigned but never used

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

// SFX indices
const byte SND_START = 0;
const byte SND_HIT = 1;
const byte SND_COIN = 2;
const byte SND_JUMP = 3;

// Actor states
const byte INACTIVE = 0;
const byte STANDING = 1;
const byte WALKING = 2;
const byte CLIMBING = 3;
const byte JUMPING = 4;
const byte FALLING = 5;
const byte PACING = 6;

// Actor types
const byte ACTOR_PLAYER = 0;
const byte ACTOR_ENEMY = 1;
const byte ACTOR_RESCUE = 2;

// Floor item types
const byte ITEM_NONE = 0;
const byte ITEM_MINE = 1;
const byte ITEM_HEART = 2;
const byte ITEM_POWER = 3;

// Metasprite data — right-facing 2x2 sprites
byte[] playerRStand = new byte[] { 0, 0, 0xd8, 0, 0, 8, 0xd9, 0, 8, 0, 0xda, 0, 8, 8, 0xdb, 0, 128 };
byte[] playerRRun1 = new byte[] { 0, 0, 0xdc, 0, 0, 8, 0xdd, 0, 8, 0, 0xde, 0, 8, 8, 0xdf, 0, 128 };
byte[] playerRRun2 = new byte[] { 0, 0, 0xe0, 0, 0, 8, 0xe1, 0, 8, 0, 0xe2, 0, 8, 8, 0xe3, 0, 128 };
byte[] playerRRun3 = new byte[] { 0, 0, 0xe4, 0, 0, 8, 0xe5, 0, 8, 0, 0xe6, 0, 8, 8, 0xe7, 0, 128 };
byte[] playerRJump = new byte[] { 0, 0, 0xe8, 0, 0, 8, 0xe9, 0, 8, 0, 0xea, 0, 8, 8, 0xeb, 0, 128 };
byte[] playerRClimb = new byte[] { 0, 0, 0xec, 0, 0, 8, 0xed, 0, 8, 0, 0xee, 0, 8, 8, 0xef, 0, 128 };
byte[] playerRSad = new byte[] { 0, 0, 0xf0, 0, 0, 8, 0xf1, 0, 8, 0, 0xf2, 0, 8, 8, 0xf3, 0, 128 };

// Left-facing 2x2 sprites (flipped)
byte[] playerLStand = new byte[] { 8, 0, 0xd8, 0x40, 8, 8, 0xd9, 0x40, 0, 0, 0xda, 0x40, 0, 8, 0xdb, 0x40, 128 };
byte[] playerLRun1 = new byte[] { 8, 0, 0xdc, 0x40, 8, 8, 0xdd, 0x40, 0, 0, 0xde, 0x40, 0, 8, 0xdf, 0x40, 128 };
byte[] playerLRun2 = new byte[] { 8, 0, 0xe0, 0x40, 8, 8, 0xe1, 0x40, 0, 0, 0xe2, 0x40, 0, 8, 0xe3, 0x40, 128 };
byte[] playerLRun3 = new byte[] { 8, 0, 0xe4, 0x40, 8, 8, 0xe5, 0x40, 0, 0, 0xe6, 0x40, 0, 8, 0xe7, 0x40, 128 };
byte[] playerLJump = new byte[] { 8, 0, 0xe8, 0x40, 8, 8, 0xe9, 0x40, 0, 0, 0xea, 0x40, 0, 8, 0xeb, 0x40, 128 };
byte[] playerLClimb = new byte[] { 8, 0, 0xec, 0x40, 8, 8, 0xed, 0x40, 0, 0, 0xee, 0x40, 0, 8, 0xef, 0x40, 128 };
byte[] playerLSad = new byte[] { 8, 0, 0xf0, 0x40, 8, 8, 0xf1, 0x40, 0, 0, 0xf2, 0x40, 0, 8, 0xf3, 0x40, 128 };

// Rescue person metasprite
byte[] personToSave = new byte[] { 0, 0, 0xbb, 3, 0, 8, 0xbd, 0, 8, 0, 0xbc, 3, 8, 8, 0xbe, 0, 128 };

// Globals
ushort scroll_pixel_yy = 0;
byte scroll_tile_y = 0;
byte player_screen_y = 0;
byte score = 0;
byte vbright = 4;

// Floor struct: ypos, height, gap, ladder1, ladder2, objtype, objpos
Floor[] floors = new Floor[MAX_FLOORS];

// Actor struct: yy(ushort), x, floor, state, yvel(sbyte), xvel(sbyte), name, pal, dir, onscreen
Actor[] actors = new Actor[MAX_ACTORS];

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
    music_play(0);
    ppu_on_all();
    while (true) ;
}

struct Floor
{
    public byte ypos;
    public byte height;
    public byte gap;
    public byte ladder1;
    public byte ladder2;
    public byte objtype;
    public byte objpos;
}

struct Actor
{
    public ushort yy;
    public byte x;
    public byte floor;
    public byte state;
    public sbyte yvel;
    public sbyte xvel;
    public byte name;
    public byte pal;
    public byte dir;
    public byte onscreen;
}
