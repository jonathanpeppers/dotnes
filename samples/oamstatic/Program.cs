byte[] PALETTE = [
    0x30,               // screen color

    0x11,0x30,0x27,0x0, // background palette 0
    0x1c,0x20,0x2c,0x0, // background palette 1
    0x00,0x10,0x20,0x0, // background palette 2
    0x06,0x16,0x26,0x0, // background palette 3

    0x14,0x34,0x0d,0x0, // sprite palette 0
    0x00,0x37,0x25,0x0, // sprite palette 1
    0x0d,0x2d,0x3a,0x0, // sprite palette 2
    0x0d,0x27,0x2a      // sprite palette 3
];

pal_all(PALETTE);
oam_clear();

G.spr_x = 40;
G.spr_y = 40;

// Compound expression with static fields: (byte)(static_field - constant)
G.spr = oam_spr((byte)(G.spr_x - 4), (byte)(G.spr_y - 8), 0xC0, 0x03, 0);
// Compound expression with static field + constant (add)
G.spr = oam_spr((byte)(G.spr_x + 8), (byte)(G.spr_y + 8), 0xC1, 0x03, G.spr);
ppu_on_all();

while (true) ;

static class G
{
    public static byte spr_x;
    public static byte spr_y;
    public static byte spr;
}
