/*
Controller-driven palette brightness and PPU color-emphasis demo.
Uses fade_in(), fade_out(), pal_fade_to(), color_emphasis(),
pal_spr_bright(), and pal_bg_bright().
*/

// set palette colors
pal_col(0, DarkBlue);
pal_col(1, Magenta);
pal_col(2, LightGray);
pal_col(3, White);
pal_col(17, Magenta);
pal_col(18, LightGray);
pal_col(19, White);

// write text to name table
vram_adr(NTADR_A(2, 2));
vram_write("FADE DEMO");

vram_adr(NTADR_A(2, 5));
vram_write("HOLD A: RED EMPHASIS");

vram_adr(NTADR_A(2, 8));
vram_write("B: TOGGLE PARTIAL FADE");

vram_adr(NTADR_A(2, 11));
vram_write("UP: GLOBAL FADE");

vram_adr(NTADR_A(2, 14));
vram_write("LEFT: BG FADE");

vram_adr(NTADR_A(2, 17));
vram_write("RIGHT: SPR FADE");

using (var oam = new OamScope())
{
    oam.spr(104, 176, (byte)'S', 0);
    oam.spr(112, 176, (byte)'P', 0);
    oam.spr(120, 176, (byte)'R', 0);
    oam.spr(128, 176, (byte)'I', 0);
    oam.spr(136, 176, (byte)'T', 0);
    oam.spr(144, 176, (byte)'E', 0);
}

byte brightness = 4;
bool partialFadeHeld = false;
bool fullFadeHeld = false;
pal_bright(brightness);
ppu_on_all();

while (true)
{
    ppu_wait_nmi();
    PAD held = pad_poll(0);

    if (pad_pressed(held, PAD.A))
        color_emphasis(ColorEmphasis.Red);
    else
        color_emphasis(ColorEmphasis.Normal);

    if (!pad_pressed(held, PAD.B))
        partialFadeHeld = false;
    else if (!partialFadeHeld)
    {
        partialFadeHeld = true;
        if (brightness == 4)
        {
            pal_fade_to(4, 2);
            brightness = 2;
        }
        else
        {
            pal_fade_to(2, 4);
            brightness = 4;
        }
    }

    if (!pad_pressed(held, PAD.UP | PAD.LEFT | PAD.RIGHT))
        fullFadeHeld = false;
    else if (!fullFadeHeld)
    {
        fullFadeHeld = true;
        pal_bright(4);
        if (pad_pressed(held, PAD.UP))
        {
            fade_out(4);
            fade_in(4);
        }
        else if (pad_pressed(held, PAD.LEFT))
        {
            bg_fade_out();
            bg_fade_in();
        }
        else
        {
            spr_fade_out();
            spr_fade_in();
        }
        brightness = 4;
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
