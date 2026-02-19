// set palette colors
pal_col(0, 0x02);  // dark blue
pal_col(1, 0x14);  // pink
pal_col(2, 0x20);  // grey
pal_col(3, 0x30);  // white

// clear vram buffer
vrambuf_clear();

// set NMI handler to use update buffer at $0100
set_vram_update(0x0100);

// enable PPU rendering
ppu_on_all();

// scroll demo — write text at increasing Y positions
byte y = 0;

while (true)
{
    // write a string into the VRAM update buffer at runtime Y position
    vrambuf_put(NTADR_A(2, y), "HELLO WORLD!");

    // increment y, stop at bottom of nametable
    if (y != 27)
        y = (byte)(y + 1);

    // set scroll position
    scroll(0, 0);

    // wait for NMI (flushes vram buffer)
    ppu_wait_nmi();

    // clear buffer for next frame
    vrambuf_clear();
}
