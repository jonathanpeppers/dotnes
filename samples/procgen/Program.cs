// Procgen — Procedural maze generation
// Demonstrates: rand8(), set_rand(), vram_adr(), vram_put(),
// pad_trigger(), ppu_off(), ppu_on_all()
//
// Generates a random maze pattern on the nametable.
// Press START to regenerate with a new seed.

// Set palette colors
pal_col(0, 0x0F);   // black background
pal_col(1, 0x05);   // dark red (walls)
pal_col(2, 0x2A);   // green (paths)
pal_col(3, 0x30);   // white (text)

// Initial seed
byte seed = 42;
set_rand(seed);

// Generate initial maze
draw_maze();

// Enable rendering
ppu_on_all();

// Main loop: regenerate on START press
while (true)
{
    ppu_wait_nmi();
    byte trigger = pad_trigger(0);
    if ((trigger & (byte)PAD.START) != 0)
    {
        seed = (byte)(seed + 1);
        set_rand(seed);
        ppu_off();
        draw_maze();
        ppu_on_all();
    }
}

static void draw_maze()
{
    // Clear nametable
    vram_adr(NAMETABLE_A);
    vram_fill(0x00, 0x0400);

    // Write title
    vram_adr(NTADR_A(9, 1));
    vram_write("PROCGEN MAZE");

    // Fill maze area (rows 3-27, cols 1-30)
    for (byte row = 3; row < 28; row++)
    {
        vram_adr(NTADR_A(1, row));
        for (byte col = 1; col < 31; col++)
        {
            // Border walls
            if (row == 3 || row == 27 || col == 1 || col == 30)
            {
                vram_put(0x01);
            }
            // Grid points are always walls
            else if ((row & 0x01) == 0 && (col & 0x01) == 0)
            {
                vram_put(0x01);
            }
            else
            {
                byte r = rand8();
                if ((r & 0x03) == 0)
                    vram_put(0x01);  // wall
                else
                    vram_put(0x00);  // path
            }
        }
    }

    // Write instructions
    vram_adr(NTADR_A(4, 29));
    vram_write("PRESS START TO REGEN");
}
