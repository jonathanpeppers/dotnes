// Minimal nested loop test: fill 4 rows of tiles using nested loops + vrambuf_put
using NES;
using static NES.NESLib;

// Setup
setup_sounds();
setup_graphics();

// Fill 4 visible rows with floor tiles using nested loops
byte[] buf = new byte[30];
for (byte row = 0; row < 4; row++)
{
    // Inner loop: fill buffer with alternating tile pattern
    for (byte col = 0; col < 30; col += 2)
    {
        buf[col] = 0xF4;
        buf[(byte)(col + 1)] = 0xF6;
    }
    // Write to nametable
    ushort addr = NTADR_A(1, (byte)(row + 10));
    vrambuf_put(addr, buf, 30);
    vrambuf_flush();
}

ppu_on_all();
while (true) ;

static void setup_sounds()
{
}

static void setup_graphics()
{
    ppu_off();
    oam_clear();
    byte[] pal = [
        0x0F, 0x11, 0x21, 0x31,
        0x0F, 0x14, 0x24, 0x34,
        0x0F, 0x15, 0x25, 0x35,
        0x0F, 0x16, 0x26, 0x36,
        0x0F, 0x11, 0x21, 0x31,
        0x0F, 0x14, 0x24, 0x34,
        0x0F, 0x15, 0x25, 0x35,
        0x0F, 0x16, 0x26, 0x36
    ];
    pal_all(pal);
    vram_fill(0x00, 0x1000);
    bank_spr(1);
    bank_bg(0);
    vrambuf_clear();
    ppu_on_all();
}
