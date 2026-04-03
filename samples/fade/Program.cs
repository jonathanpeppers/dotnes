using static NES.NESColor;

/*
Demonstrates palette brightness fading effects.
Uses pal_bright(), pal_spr_bright(), and pal_bg_bright()
with delay() for timed transitions.
*/

// set palette colors
pal_col(0, DarkBlue);
pal_col(1, Magenta);
pal_col(2, LightGray);
pal_col(3, White);

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("FADE DEMO");

vram_adr(NTADR_A(2, 5));
vram_write("PAL BRIGHT");

vram_adr(NTADR_A(2, 10));
vram_write("BG BRIGHT");

vram_adr(NTADR_A(2, 15));
vram_write("SPR BRIGHT");

// start dark
pal_bright(0);

// enable rendering
ppu_on_all();

// infinite loop: cycle through fade effects
while (true)
{
    // Phase 1: global fade in / out
    fade_in();
    delay(60);
    fade_out();
    delay(30);

    // Phase 2: background-only fade in / out
    bg_fade_in();
    delay(60);
    bg_fade_out();
    delay(30);

    // Phase 3: sprite-only fade in / out
    spr_fade_in();
    delay(60);
    spr_fade_out();
    delay(30);
}

static void fade_in()
{
    for (byte i = 0; i <= 4; i = (byte)(i + 1))
    {
        pal_bright(i);
        delay(4);
    }
}

static void fade_out()
{
    for (byte i = 4; i != 255; i = (byte)(i - 1))
    {
        pal_bright(i);
        delay(4);
    }
}

static void bg_fade_in()
{
    for (byte i = 0; i <= 4; i = (byte)(i + 1))
    {
        pal_bg_bright(i);
        delay(4);
    }
}

static void bg_fade_out()
{
    for (byte i = 4; i != 255; i = (byte)(i - 1))
    {
        pal_bg_bright(i);
        delay(4);
    }
}

static void spr_fade_in()
{
    for (byte i = 0; i <= 4; i = (byte)(i + 1))
    {
        pal_spr_bright(i);
        delay(4);
    }
}

static void spr_fade_out()
{
    for (byte i = 4; i != 255; i = (byte)(i - 1))
    {
        pal_spr_bright(i);
        delay(4);
    }
}
