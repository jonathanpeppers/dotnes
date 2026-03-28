/*
Port of conio.c from 8bitworkshop.
Original: https://8bitworkshop.com/ (conio.c)

Demonstrates console I/O equivalents using NES VRAM functions.
Original uses CC65's conio library (bgcolor, clrscr, cputc, cprintf, etc.)
dotnes uses: pal_col, vram_fill, vram_adr, vram_write, vram_put, vram_inc, pad_poll.

See docs/conio-missing-features.md for CC65 features not yet in dotnes.
*/

// NES screen is 32 tiles wide, 30 tiles tall

// Set background color to blue (bgcolor(COLOR_BLUE) equivalent)
pal_col(0, 0x02);   // dark blue background
pal_col(1, 0x30);   // white text

// Clear the screen (clrscr() equivalent)
vram_adr(NTADR_A(0, 0));
vram_fill(0x00, 960);   // 32 * 30 = 960 tiles

// Draw border around the screen

// Top line: '+' corner, 30 dashes, '+' corner
vram_adr(NTADR_A(0, 0));
vram_put(0x2B);            // '+' upper-left corner
vram_fill(0x2D, 30);       // '-' horizontal line
vram_put(0x2B);            // '+' upper-right corner

// Bottom line
vram_adr(NTADR_A(0, 29));
vram_put(0x2B);            // '+' lower-left corner
vram_fill(0x2D, 30);       // '-' horizontal line
vram_put(0x2B);            // '+' lower-right corner

// Left vertical line (rows 1..28)
vram_adr(NTADR_A(0, 1));
vram_inc(VramIncrement.By32);               // +32 increment for vertical writing
vram_fill(0x7C, 28);       // '|' vertical line
vram_inc(VramIncrement.By1);               // restore +1 increment

// Right vertical line (rows 1..28)
vram_adr(NTADR_A(31, 1));
vram_inc(VramIncrement.By32);               // +32 increment
vram_fill(0x7C, 28);       // '|' vertical line
vram_inc(VramIncrement.By1);               // restore +1 increment

// Write "Hello world!" centered on screen
// x = (32 - 12) / 2 = 10, y = 30 / 2 = 15
vram_adr(NTADR_A(10, 15));
vram_write("Hello world!");

// Enable PPU rendering
ppu_on_all();

// Wait for joystick button press (joy_read equivalent)
while (true)
{
    ppu_wait_nmi();
    PAD pad = pad_poll(0);
    if (pad != 0)
        break;
}

// Clear screen after button press (clrscr equivalent)
ppu_off();
vram_adr(NTADR_A(0, 0));
vram_fill(0x00, 960);
ppu_on_all();

// Infinite loop (NES programs never exit)
while (true) ;
