byte[] PALETTE = [
    White,                                     // screen color

    Azure, White, LightOrange, 0x0,            // background palette 0
    Cyan, LightGray, LightCyan, 0x0,           // background palette 1
    DarkGray, Gray, LightGray, 0x0,            // background palette 2
    DarkRed, Red, LightRed, 0x0,               // background palette 3

    Magenta, PaleMagenta, 0x0D, 0x0,           // sprite palette 0
    DarkGray, PaleOrange, LightRose, 0x0,      // sprite palette 1
    0x0D, MediumGray, PaleGreen, 0x0,          // sprite palette 2
    0x0D, LightOrange, LightGreen              // sprite palette 3
];

pal_all(PALETTE);
oam_clear();

G.spr_x = 124;
G.spr_y = 108;

// Compound expression with static fields: (byte)(static_field - constant)
// Face: at (spr_x - 4, spr_y - 4) — tests Sub path
G.spr = oam_spr((byte)(G.spr_x - 4), (byte)(G.spr_y - 4), 0xC0, 0x03, 0);
// Compound expression with static field + constant (add)
// Body: at (spr_x - 4, spr_y + 4) — tests Add path, 8px below face
G.spr = oam_spr((byte)(G.spr_x - 4), (byte)(G.spr_y + 4), 0xC1, 0x03, G.spr);
ppu_on_all();

while (true) ;

static class G
{
    public static byte spr_x;
    public static byte spr_y;
    public static byte spr;
}
