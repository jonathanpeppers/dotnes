/*
Based on: https://8bitworkshop.com/v3.10.0/?platform=nes&file=sprites.c

Animate hardware sprite.
*/

// select a sprite id
byte sprite_id = 1;
byte oam_id = 0;

// initialize actors with random values
byte actor_x = 0;
byte actor_y = 0;
byte actor_dx = 5;
byte actor_dy = 3;

// screen color
pal_col(0, 0x04);

// turn on PPU
ppu_on_all();
  
// loop forever
while (true)
{
    // draw and move all actors
    oam_id = oam_spr(actor_x, actor_y, sprite_id, sprite_id, oam_id);
    actor_x += actor_dx;
    actor_y += actor_dy;

    // wait for next frame
    ppu_wait_frame();
    oam_clear();
}
