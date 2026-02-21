// Horizontal scrolling with building generation using vertical VRAM writes.
// Port of 8bitworkshop's horizmask.c sample.

const byte PLAYROWS = 26;

byte[] PALETTE = {
    0x03,

    0x25, 0x30, 0x27, 0x00,
    0x1C, 0x20, 0x2C, 0x00,
    0x00, 0x10, 0x20, 0x00,
    0x06, 0x16, 0x26, 0x00,

    0x16, 0x35, 0x24, 0x00,
    0x00, 0x37, 0x25, 0x00,
    0x0D, 0x2D, 0x1A, 0x00,
    0x0D, 0x27, 0x2A
};

void scroll_demo()
{
    // Building state
    byte bldg_height = (byte)((rand8() & 7) + 2);
    byte bldg_width = (byte)((rand8() & 3) * 4 + 4);
    byte bldg_char = (byte)(rand8() & 15);
    byte x = 0;
    byte col_counter = 32; // next column to draw (tile units)
    byte sub = 0; // sub-pixel counter (0-7)
    byte[] buf = new byte[PLAYROWS];

    while (true)
    {
        if (sub == 0)
        {
            // Clear buffer
            byte i = 0;
            while (i < PLAYROWS)
            {
                buf[i] = 0;
                i++;
            }

            // Draw a random star in the sky
            buf[rand8() & 15] = 46; // '.'

            // Draw building roof
            byte roofIdx = (byte)(PLAYROWS - bldg_height - 1);
            buf[roofIdx] = (byte)(bldg_char & 3);

            // Draw building body
            byte j = (byte)(PLAYROWS - bldg_height);
            while (j < PLAYROWS)
            {
                buf[j] = bldg_char;
                j++;
            }

            // Write vertical column to offscreen nametable area
            if (col_counter < 32)
                vrambuf_put(NTADR_A(col_counter, 4), buf, PLAYROWS);
            else
                vrambuf_put(NTADR_B((byte)(col_counter & 31), 4), buf, PLAYROWS);

            // Advance column
            col_counter = (byte)((col_counter + 1) & 63);

            // Generate new building?
            bldg_width--;
            if (bldg_width == 0)
            {
                bldg_height = (byte)((rand8() & 7) + 2);
                bldg_width = (byte)((rand8() & 3) * 4 + 4);
                bldg_char = (byte)(rand8() & 15);
            }
        }

        // Advance sub-pixel counter
        sub++;
        if (sub == 8)
            sub = 0;

        ppu_wait_nmi();
        vrambuf_clear();
        split(x, 0);
        x++;
    }
}

// Set palette
pal_all(PALETTE);

// Write header text
vram_adr(NTADR_A(7, 0));
vram_write("HORIZMASK DEMO");
vram_adr(NTADR_A(0, 3));
vram_fill(5, 32);

// Set sprite 0 for split
oam_clear();
oam_spr(1, 30, 0xa0, 0, 0);

// Enable VRAM update buffer
vrambuf_clear();
set_vram_update(updbuf);

// Turn on rendering
ppu_on_all();

// Start scrolling (never returns)
scroll_demo();
