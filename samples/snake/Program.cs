/*
Snake — classic snake game.
D-pad changes direction (edge-triggered).
Eat food to grow and score. Game over on wall or self collision.
APIs: pad_trigger, oam_spr, oam_begin, rand8, vrambuf_put, ppu_wait_nmi.
*/

#pragma warning disable CS0649, CS0219

byte[] PALETTE = [
    Black,                                   // screen color
    DarkGray, Gray, White,                   // bg palette 0
    Black, DarkGray, Gray, White,            // bg palette 1
    Black, DarkGray, Gray, White,            // bg palette 2
    Black, DarkGray, Gray, White,            // bg palette 3
    Black, Green, LightGreen, PaleGreen,     // spr palette 0 (green: snake)
    Black, Red, LightRed, PaleRed,           // spr palette 1 (red: food)
    Black, White, White, White,              // spr palette 2
    Black, White, White, White               // spr palette 3
];

// direction constants
const byte DIR_UP = 0;
const byte DIR_RIGHT = 1;
const byte DIR_DOWN = 2;
const byte DIR_LEFT = 3;

// snake body arrays (max 40 segments)
byte[] snake_x = new byte[40];
byte[] snake_y = new byte[40];
byte snake_len = 3;
byte dir = DIR_RIGHT;

// food position
byte food_x = 80;
byte food_y = 80;

// game state
byte frame_count = 0;
byte speed = 8;
byte game_over = 0;

// score digits (tile indices: 0x30='0')
byte s0 = 0x30;
byte s1 = 0x30;
byte s2 = 0x30;
byte[] sbuf = new byte[3];

// temp globals (needed to work around transpiler array limitations)
PAD trig = 0;
byte headX = 0;
byte headY = 0;
byte tmpX = 0;
byte tmpY = 0;
byte idx = 0;

// --- SETUP ---
pal_all(PALETTE);
oam_clear();

// initial snake at center, heading right
snake_x[0] = 128;
snake_y[0] = 120;
snake_x[1] = 120;
snake_y[1] = 120;
snake_x[2] = 112;
snake_y[2] = 120;

// write score header while PPU is off
vram_adr(NTADR_A(12, 1));
vram_write("SCORE 000");

// enable VRAM buffer for runtime updates
vrambuf_clear();
set_vram_update(updbuf);
ppu_on_all();

// --- MAIN LOOP ---
while (true)
{
    ppu_wait_nmi();
    vrambuf_clear();

    if (game_over == 0)
    {
        // read direction input (use global to avoid AND cascade bug)
        trig = pad_trigger(0);
        if ((trig & PAD.RIGHT) != 0)
        {
            if (dir != DIR_LEFT) dir = DIR_RIGHT;
        }
        if ((trig & PAD.LEFT) != 0)
        {
            if (dir != DIR_RIGHT) dir = DIR_LEFT;
        }
        if ((trig & PAD.UP) != 0)
        {
            if (dir != DIR_DOWN) dir = DIR_UP;
        }
        if ((trig & PAD.DOWN) != 0)
        {
            if (dir != DIR_UP) dir = DIR_DOWN;
        }

        // speed control: move once every N frames
        frame_count = (byte)(frame_count + 1);
        if (frame_count >= speed)
        {
            frame_count = 0;

            // shift body segments toward tail (read source to global, write to dest)
            idx = (byte)(snake_len - 1);
            while (idx > 0)
            {
                tmpX = (byte)(idx - 1);
                headX = snake_x[tmpX];
                headY = snake_y[tmpX];
                snake_x[idx] = headX;
                snake_y[idx] = headY;
                idx = tmpX;
            }

            // move head (use globals to avoid array self-modification bug)
            headX = snake_x[0];
            headY = snake_y[0];
            if (dir == DIR_UP) headY = (byte)(headY + 248); // -8 via wrapping add
            if (dir == DIR_RIGHT) headX = (byte)(headX + 8);
            if (dir == DIR_DOWN) headY = (byte)(headY + 8);
            if (dir == DIR_LEFT) headX = (byte)(headX + 248); // -8 via wrapping add
            snake_x[0] = headX;
            snake_y[0] = headY;

            // wall collision (play area: x 8..232, y 24..208)
            if (headX == 0) game_over = 1;
            if (headX >= 240) game_over = 1;
            if (headY < 24) game_over = 1;
            if (headY >= 216) game_over = 1;

            // self collision (use globals for array element comparison)
            idx = 1;
            while (idx < snake_len)
            {
                tmpX = snake_x[idx];
                tmpY = snake_y[idx];
                if (headX == tmpX)
                {
                    if (headY == tmpY)
                    {
                        game_over = 1;
                    }
                }
                idx = (byte)(idx + 1);
            }

            // food collision
            if (headX == food_x)
            {
                if (headY == food_y)
                {
                    // grow snake
                    if (snake_len < 40)
                    {
                        // initialize new tail segment with current tail position
                        idx = (byte)(snake_len - 1);
                        tmpX = snake_x[idx];
                        tmpY = snake_y[idx];
                        snake_x[snake_len] = tmpX;
                        snake_y[snake_len] = tmpY;
                        snake_len = (byte)(snake_len + 1);
                    }

                    // increment score
                    s2 = (byte)(s2 + 1);
                    if (s2 == 0x3A) { s2 = 0x30; s1 = (byte)(s1 + 1); }
                    if (s1 == 0x3A) { s1 = 0x30; s0 = (byte)(s0 + 1); }
                    if (s0 == 0x3A) s0 = 0x30;
                    sbuf[0] = s0;
                    sbuf[1] = s1;
                    sbuf[2] = s2;
                    vrambuf_put(NTADR_A(18, 1), sbuf, 3);

                    // spawn new food on 8-pixel grid
                    food_x = (byte)(rand8() & 0xF8);
                    if (food_x < 8) food_x = 8;
                    if (food_x > 232) food_x = 232;
                    food_y = (byte)(rand8() & 0xF8);
                    if (food_y < 24) food_y = 24;
                    if (food_y > 208) food_y = 208;
                }
            }
        }
    }

    // draw sprites (pre-compute array args into globals)
    using (var frame = oam_begin())
    {
        idx = 0;
        while (idx < snake_len)
        {
            tmpX = snake_x[idx];
            tmpY = snake_y[idx];
            oam_off = oam_spr(tmpX, tmpY, 0x01, 0, oam_off);
            idx = (byte)(idx + 1);
        }
        oam_off = oam_spr(food_x, food_y, 0x01, 1, oam_off);
    }
}
