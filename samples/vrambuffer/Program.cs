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

// write text lines one per frame using fixed NTADR addresses
// (NTADR is compile-time only, so each call needs constant args)

vrambuf_put(NTADR_A(2, 2), "HELLO WORLD!");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 4), "VRAM BUFFER");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 6), "DEMO FOR NES");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 8), "LINE BY LINE");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 10), "UPDATING VRAM");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 12), "WHILE PPU ON");
ppu_wait_nmi();
vrambuf_clear();

vrambuf_put(NTADR_A(2, 14), "DOTNES SAMPLE");
ppu_wait_nmi();
vrambuf_clear();

// scroll position
scroll(0, 0);

while (true)
    ;
