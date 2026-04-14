/*
Peek-Poke Demo: Direct NES hardware register access.
The screen scrolls using poke() to write the PPU scroll
register directly, and toggles grayscale every ~6 seconds
via ppu_mask(). peek() resets the PPU address latch each frame.
Uses a ushort frame counter (exceeds byte range).

PPU Registers used:
  $2001 = PPU_MASK  (rendering control)
  $2002 = PPU_STATUS (reading resets address latch)
  $2005 = PPU_SCROLL (first write = X, second write = Y)
*/

// set palette colors
pal_col(0, DarkBlue);
pal_col(1, Magenta);
pal_col(2, LightGray);
pal_col(3, White);

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("PEEK-POKE DEMO");
vram_adr(NTADR_A(2, 5));
vram_write("SCROLL VIA POKE!");

// enable PPU rendering
ppu_on_all();

byte scroll_x = 0;
ushort frame_count = 0;
byte grayscale = 0;

while (true)
{
    ppu_wait_nmi();

    // peek: reading $2002 resets the PPU address latch
    // this is REQUIRED before writing scroll registers
    peek(PPU_STATUS);

    // poke: write X and Y scroll directly to $2005
    poke(PPU_SCROLL, scroll_x);
    poke(PPU_SCROLL, 0);

    scroll_x = (byte)(scroll_x + 1);

    // toggle grayscale every ~360 frames (~6 seconds)
    // uses ushort because the count exceeds byte range (255)
    frame_count++;
    if (frame_count == 360)
    {
        frame_count = 0;
        if (grayscale != 0)
        {
            grayscale = 0;
            // ppu_mask: normal rendering
            ppu_mask(MASK.BG | MASK.SPR | MASK.EDGE_BG | MASK.EDGE_SPR);
        }
        else
        {
            grayscale = 1;
            // ppu_mask: grayscale mode
            ppu_mask(MASK.MONO | MASK.BG | MASK.SPR | MASK.EDGE_BG | MASK.EDGE_SPR);
        }
    }
}
