/*
Sprite demo.
Animate 32 hardware sprites moving around with wrapping.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/sprites.c
(Reduced from 64 to 32 actors due to NES zero-page memory constraints.)
*/

#include "neslib.h"

//#link "chr_generic.s"

/*{pal:"nes",layout:"nes"}*/
const char PALETTE[32] = {
  0x03,
  0x11,0x30,0x27,0x0,
  0x1c,0x20,0x2c,0x0,
  0x00,0x10,0x20,0x0,
  0x06,0x16,0x26,0x0,
  0x16,0x35,0x24,0x0,
  0x00,0x37,0x25,0x0,
  0x0d,0x2d,0x3a,0x0,
  0x0d,0x27,0x2a
};

#define NUM_ACTORS 32

byte actor_x[NUM_ACTORS];
byte actor_y[NUM_ACTORS];
sbyte actor_dx[NUM_ACTORS];
sbyte actor_dy[NUM_ACTORS];

void main() {
  char i;
  char oam_id;

  for (i=0; i<NUM_ACTORS; i++) {
    actor_x[i] = rand();
    actor_y[i] = rand();
    actor_dx[i] = (rand() & 7) - 3;
    actor_dy[i] = (rand() & 7) - 3;
  }

  oam_clear();
  pal_all(PALETTE);
  ppu_on_all();

  while (1) {
    oam_id = 0;
    for (i=0; i<NUM_ACTORS; i++) {
      oam_id = oam_spr(actor_x[i], actor_y[i], i, i, oam_id);
      actor_x[i] += actor_dx[i];
      actor_y[i] += actor_dy[i];
    }
    if (oam_id!=0) oam_hide_rest(oam_id);
    ppu_wait_frame();
  }
}
