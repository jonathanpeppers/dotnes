/*
Animation demo - frame-by-frame sprite animation.
Cycles through metasprite frames using a counter and bit masking
to create an animated character walking across the screen.

APIs showcased:
- oam_meta_spr() for metasprite rendering
- ppu_wait_nmi() for frame timing
- Frame counter + bit mask for animation rate control
*/

// Walk frame 0: 2x2 metasprite (tiles 0xD8-0xDB)
byte[] walk0 = [
    0, 0, 0xD8, 0,
    0, 8, 0xD9, 0,
    8, 0, 0xDA, 0,
    8, 8, 0xDB, 0,
    128
];

// Walk frame 1: 2x2 metasprite (tiles 0xDC-0xDF)
byte[] walk1 = [
    0, 0, 0xDC, 0,
    0, 8, 0xDD, 0,
    8, 0, 0xDE, 0,
    8, 8, 0xDF, 0,
    128
];

byte[] PALETTE = [
    Black,                                   // screen color
    Azure, White, LightOrange, 0x0,          // background palette 0
    Cyan, LightGray, LightCyan, 0x0,         // background palette 1
    DarkGray, Gray, LightGray, 0x0,          // background palette 2
    DarkRed, Red, LightRed, 0x0,             // background palette 3
    Red, PaleRose, LightMagenta, 0x0,        // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,    // sprite palette 1
    Black, MediumGray, PaleGreen, 0x0,        // sprite palette 2
    Black, LightOrange, LightGreen            // sprite palette 3
];

// setup — must be before scalar locals so PALETTE address isn't lost
oam_clear();
pal_all(PALETTE);
ppu_on_all();

byte x = 0;
byte y = 112;
byte frame_counter = 0;
// main loop
while (true)
{
    ppu_wait_nmi();

    // Select animation frame based on counter
    // Bit 3 toggles every 8 frames for ~7.5 fps animation
    byte oam_id = 0;
    if ((frame_counter & 8) == 0)
        oam_id = oam_meta_spr(x, y, 0, walk0);
    else
        oam_id = oam_meta_spr(x, y, 0, walk1);

    oam_hide_rest(oam_id);

    // Move character to the right (wraps at byte overflow)
    x = (byte)(x + 1);

    // Increment frame counter
    frame_counter = (byte)(frame_counter + 1);
}
