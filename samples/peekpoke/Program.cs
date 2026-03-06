/*
Peek-Poke Demo: Direct NES hardware register access.
The screen scrolls smoothly and toggles between normal
and grayscale rendering every ~1 second.

PPU Registers:
  $2001 = PPU_MASK (rendering control)
  $2002 = PPU_STATUS (vblank flag, sprite 0 hit)
  $2005 = PPU_SCROLL (scroll position)
*/

// set palette colors
pal_col(0, 0x02);   // dark blue background
pal_col(1, 0x14);   // purple
pal_col(2, 0x20);   // grey
pal_col(3, 0x30);   // white

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("PEEK-POKE DEMO");
vram_adr(NTADR_A(2, 5));
vram_write("DIRECT HW ACCESS");

// enable PPU rendering
ppu_on_all();

// peek: read PPU status to reset address latch
peek(0x2002);

// poke: initialize scroll position via PPU registers
poke(0x2005, 0);    // scroll X = 0
poke(0x2005, 0);    // scroll Y = 0

// animation state
byte scroll_x = 0;
byte frame_count = 0;
byte grayscale = 0;

while (true)
{
    ppu_wait_nmi();

    // scroll the screen horizontally each frame
    scroll(scroll_x, 0);
    scroll_x = (byte)(scroll_x + 1);

    // toggle grayscale every ~60 frames (~1 second)
    frame_count = (byte)(frame_count + 1);
    if (frame_count == 60)
    {
        frame_count = 0;
        if (grayscale != 0)
        {
            grayscale = 0;
            // ppu_mask: normal rendering
            ppu_mask(0x1E);
        }
        else
        {
            grayscale = 1;
            // ppu_mask: enable grayscale mode
            ppu_mask(0x1F);
        }
    }
}
