/*
Pong — 2-player game.
Player 1: controller 1 UP/DOWN.
Player 2: controller 2 UP/DOWN.
Ball bounces off walls and paddles. Score at top.
APIs: pad_poll, oam_spr, oam_begin, vrambuf_put, ppu_wait_nmi.
*/

byte[] PALETTE = [
    Black,                                     // screen color
    DarkGray, Gray, White, 0x0,                // bg palette 0
    DarkGray, Gray, White, 0x0,                // bg palette 1
    DarkGray, Gray, White, 0x0,                // bg palette 2
    DarkGray, Gray, White, 0x0,                // bg palette 3
    Black, White, White, 0x0,                  // spr palette 0
    Black, White, White, 0x0,                  // spr palette 1
    Black, White, White, 0x0,                  // spr palette 2
    Black, White, White, 0x0                   // spr palette 3
];

// paddle y positions
byte p1_y = 104;
byte p2_y = 104;

// ball position
byte ball_x = 124;
byte ball_y = 120;

// ball direction flags (1=positive, 0=negative)
byte ball_right = 1;
byte ball_down = 1;

// score digits (tile indices: 0x30='0')
byte s1 = 0x30;
byte s2 = 0x30;

// display buffer
byte[] sbuf = new byte[1];

// setup
pal_all(PALETTE);
oam_clear();

// write initial score
vram_adr(NTADR_A(12, 2));
vram_write("0  -  0");

// enable VRAM buffer for runtime updates
vrambuf_clear();
set_vram_update(0x0100);
ppu_on_all();

// game loop
while (true)
{
    ppu_wait_nmi();
    vrambuf_clear();

    // read controller 1 and move paddle 1
    PAD pad = pad_poll(0);
    if ((pad & PAD.UP) != 0)
    {
        if (p1_y > 16)
            p1_y = (byte)(p1_y - 2);
    }
    if ((pad & PAD.DOWN) != 0)
    {
        if (p1_y < 200)
            p1_y = (byte)(p1_y + 2);
    }

    // read controller 2 and move paddle 2
    pad = pad_poll(1);
    if ((pad & PAD.UP) != 0)
    {
        if (p2_y > 16)
            p2_y = (byte)(p2_y - 2);
    }
    if ((pad & PAD.DOWN) != 0)
    {
        if (p2_y < 200)
            p2_y = (byte)(p2_y + 2);
    }

    // move ball horizontally (constant speed, direction flag)
    if (ball_right != 0)
        ball_x = (byte)(ball_x + 2);
    if (ball_right == 0)
        ball_x = (byte)(ball_x - 2);

    // move ball vertically
    if (ball_down != 0)
        ball_y = (byte)(ball_y + 1);
    if (ball_down == 0)
        ball_y = (byte)(ball_y - 1);

    // top wall bounce
    if (ball_y < 16)
    {
        ball_y = 16;
        ball_down = 1;
    }

    // bottom wall bounce
    if (ball_y > 216)
    {
        ball_y = 216;
        ball_down = 0;
    }

    // paddle 1 collision (left side) — cheap X pre-check before rect_overlap
    if (ball_right == 0 && ball_x < 24 && rect_overlap(ball_x, ball_y, 8, 8, 16, p1_y, 8, 24))
    {
        ball_right = 1;
        ball_x = 24;
    }

    // paddle 2 collision (right side) — cheap X pre-check before rect_overlap
    if (ball_right != 0 && ball_x > 224 && rect_overlap(ball_x, ball_y, 8, 8, 232, p2_y, 8, 24))
    {
        ball_right = 0;
        ball_x = 224;
    }

    // player 2 scores (ball passed left edge)
    if (ball_right == 0)
    {
        if (ball_x > 240)
        {
            s2 = (byte)(s2 + 1);
            if (s2 == 0x3A)
                s2 = 0x30;
            ball_x = 124;
            ball_y = 120;
            ball_right = 1;
            ball_down = 1;
            sbuf[0] = s2;
            vrambuf_put(NTADR_A(18, 2), sbuf, 1);
        }
    }

    // player 1 scores (ball passed right edge)
    if (ball_right != 0)
    {
        if (ball_x < 8)
        {
            s1 = (byte)(s1 + 1);
            if (s1 == 0x3A)
                s1 = 0x30;
            ball_x = 124;
            ball_y = 120;
            ball_right = 0;
            ball_down = 1;
            sbuf[0] = s1;
            vrambuf_put(NTADR_A(12, 2), sbuf, 1);
        }
    }

    // draw sprites
    using (var frame = oam_begin())
    {
        // paddle 1 (3 sprites at x=16)
        oam_off = oam_spr(16, p1_y, 0x01, 0, oam_off);
        oam_off = oam_spr(16, (byte)(p1_y + 8), 0x01, 0, oam_off);
        oam_off = oam_spr(16, (byte)(p1_y + 16), 0x01, 0, oam_off);
        // paddle 2 (3 sprites at x=232)
        oam_off = oam_spr(232, p2_y, 0x01, 1, oam_off);
        oam_off = oam_spr(232, (byte)(p2_y + 8), 0x01, 1, oam_off);
        oam_off = oam_spr(232, (byte)(p2_y + 16), 0x01, 1, oam_off);
        // ball
        oam_off = oam_spr(ball_x, ball_y, 0x01, 2, oam_off);
    }
}
