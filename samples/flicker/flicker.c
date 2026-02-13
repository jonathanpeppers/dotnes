/*
Flicker demo - shows sprite cycling for more than 64 hardware sprites.
Based on: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/flicker.c
Uses oam_meta_spr_pal to set per-actor palette colors.
*/

#include <stdlib.h>
#include <string.h>

// include NESLIB header
#include "neslib.h"

// include CC65 NES Header (PPU)
#include <nes.h>

// link the pattern table into CHR ROM
//#link "chr_generic.s"

///// METASPRITES

#define TILE 0xd8
#define ATTR 0

// define a 2x2 metasprite
const unsigned char metasprite[]={
        0,      0,      TILE+0,   ATTR,
        0,      8,      TILE+1,   ATTR,
        8,      0,      TILE+2,   ATTR,
        8,      8,      TILE+3,   ATTR,
        128};

/*{pal:"nes",layout:"nes"}*/
const char PALETTE[32] = {
  0x03,			// screen color

  0x11,0x30,0x27,0x0,	// background palette 0
  0x1c,0x20,0x2c,0x0,	// background palette 1
  0x00,0x10,0x20,0x0,	// background palette 2
  0x06,0x16,0x26,0x0,	// background palette 3

  0x16,0x35,0x24,0x0,	// sprite palette 0
  0x00,0x37,0x25,0x0,	// sprite palette 1
  0x0d,0x2d,0x3a,0x0,	// sprite palette 2
  0x0d,0x27,0x2a	// sprite palette 3
};

// number of actors (4 h/w sprites each)
#define NUM_ACTORS 24

// actor x/y positions
byte actor_x[NUM_ACTORS];
byte actor_y[NUM_ACTORS];
// actor x/y deltas per frame (signed)
byte actor_dx[NUM_ACTORS];
byte actor_dy[NUM_ACTORS];

// main program
void main() {
  byte i;	// actor index
  byte count;
  byte pal;

  // setup
  oam_clear();
  pal_all(PALETTE);
  ppu_on_all();

  // initialize actors with random values
  i = 0;
  while (i < NUM_ACTORS) {
    actor_x[i] = rand8();
    actor_y[i] = rand8();
    actor_dx[i] = (rand8() & 7) - 3;
    actor_dy[i] = (rand8() & 7) - 3;
    i++;
  }

  // loop forever
  while (1) {
    oam_off = 0;
    count = 0;

    // draw up to 15 actors per frame (15 * 4 = 60 sprites, under the 64 limit)
    while (count < 15) {
      // palette color cycles with actor index (i & 3)
      pal = i & 3;
      // Note: 8bitworkshop's oam_meta_spr_pal has a bug where pal is ignored.
      // Our C# version correctly applies the palette via ORA into OAM attributes.
      oam_meta_spr_pal(actor_x[i], actor_y[i], pal, metasprite);

      // update position
      actor_x[i] = actor_x[i] + actor_dx[i];
      actor_y[i] = actor_y[i] + actor_dy[i];

      // advance and wrap around actor array
      i++;
      if (i >= NUM_ACTORS) {
        i -= NUM_ACTORS;
      }

      count++;
    }

    oam_hide_rest(oam_off);
    ppu_wait_nmi();
  }
}
