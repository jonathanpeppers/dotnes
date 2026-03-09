/*
Pong — 2-player game.
Player 1: controller 1 UP/DOWN.
Player 2: controller 2 UP/DOWN.
Ball bounces off walls and paddles. Score at top.
APIs: pad_poll, oam_spr, oam_hide_rest, vrambuf_put, ppu_wait_nmi.
*/

byte[] PALETTE = [
    0x0F,                    // screen color (black)
    0x00, 0x10, 0x30, 0x0,  // bg palette 0
    0x00, 0x10, 0x30, 0x0,  // bg palette 1
    0x00, 0x10, 0x30, 0x0,  // bg palette 2
    0x00, 0x10, 0x30, 0x0,  // bg palette 3
    0x0F, 0x30, 0x30, 0x0,  // spr palette 0 (white)
    0x0F, 0x30, 0x30, 0x0,  // spr palette 1
    0x0F, 0x30, 0x30, 0x0,  // spr palette 2
    0x0F, 0x30, 0x30         // spr palette 3
];

// paddle y positions
byte p1_y = 104;
byte p2_y = 104;

// ball position and direction
byte ball_x = 124;
byte ball_y = 120;
byte ball_dx = 2;    // 2=right, 254=left
byte ball_dy = 1;    // 1=down, 255=up

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

    // read controllers
    PAD pad1 = pad_poll(0);
    PAD pad2 = pad_poll(1);

    // paddle 1 movement
    if ((pad1 & PAD.UP) != 0)
    {
        if (p1_y > 16)
            p1_y = (byte)(p1_y - 2);
    }
    if ((pad1 & PAD.DOWN) != 0)
    {
        if (p1_y < 200)
            p1_y = (byte)(p1_y + 2);
    }

    // paddle 2 movement
    if ((pad2 & PAD.UP) != 0)
    {
        if (p2_y > 16)
            p2_y = (byte)(p2_y - 2);
    }
    if ((pad2 & PAD.DOWN) != 0)
    {
        if (p2_y < 200)
            p2_y = (byte)(p2_y + 2);
    }

    // move ball
    ball_x = (byte)(ball_x + ball_dx);
    ball_y = (byte)(ball_y + ball_dy);

    // top wall bounce
    if (ball_y < 16)
    {
        ball_y = 16;
        ball_dy = 1;
    }

    // bottom wall bounce
    if (ball_y > 216)
    {
        ball_y = 216;
        ball_dy = 255;
    }

    // paddle 1 collision (left, x=16)
    if (ball_x < 24)
    {
        if (ball_dx > 127)
        {
            if (ball_y >= p1_y)
            {
                byte p1_end = (byte)(p1_y + 24);
                if (ball_y < p1_end)
                {
                    ball_dx = 2;
                    ball_x = 24;
                }
            }
        }
    }

    // paddle 2 collision (right, x=232)
    if (ball_x > 224)
    {
        if (ball_dx < 128)
        {
            if (ball_y >= p2_y)
            {
                byte p2_end = (byte)(p2_y + 24);
                if (ball_y < p2_end)
                {
                    ball_dx = 254;
                    ball_x = 224;
                }
            }
        }
    }

    // player 2 scores (ball passed left edge)
    if (ball_dx > 127)
    {
        if (ball_x > 240)
        {
            s2 = (byte)(s2 + 1);
            if (s2 == 0x3A)
                s2 = 0x30;
            ball_x = 124;
            ball_y = 120;
            ball_dx = 254;
            ball_dy = 1;
            sbuf[0] = s2;
            vrambuf_put(NTADR_A(18, 2), sbuf, 1);
        }
    }

    // player 1 scores (ball passed right edge)
    if (ball_dx < 128)
    {
        if (ball_x < 8)
        {
            s1 = (byte)(s1 + 1);
            if (s1 == 0x3A)
                s1 = 0x30;
            ball_x = 124;
            ball_y = 120;
            ball_dx = 2;
            ball_dy = 1;
            sbuf[0] = s1;
            vrambuf_put(NTADR_A(12, 2), sbuf, 1);
        }
    }

    // draw sprites
    byte oam_id = 0;
    // paddle 1 (3 sprites at x=16)
    oam_id = oam_spr(16, p1_y, 0x01, 0, oam_id);
    oam_id = oam_spr(16, (byte)(p1_y + 8), 0x01, 0, oam_id);
    oam_id = oam_spr(16, (byte)(p1_y + 16), 0x01, 0, oam_id);
    // paddle 2 (3 sprites at x=232)
    oam_id = oam_spr(232, p2_y, 0x01, 1, oam_id);
    oam_id = oam_spr(232, (byte)(p2_y + 8), 0x01, 1, oam_id);
    oam_id = oam_spr(232, (byte)(p2_y + 16), 0x01, 1, oam_id);
    // ball
    oam_id = oam_spr(ball_x, ball_y, 0x01, 2, oam_id);
    oam_hide_rest(oam_id);
}
