/*
Demonstrates pad_trigger() and pad_state() for different input modes.
pad_trigger() detects newly pressed buttons (edge detection).
pad_state() reads currently held buttons.
Press A/B to decrease/increase virtual brightness.
D-pad moves the cursor using held-button state.
Original: https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/metatrigger.c
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

// define a 2x2 metasprite
#define DEF_METASPRITE_2x2(name,code,pal)\
const unsigned char name[]={\
        0,      0,      (code)+0,   pal, \
        0,      8,      (code)+1,   pal, \
        8,      0,      (code)+2,   pal, \
        8,      8,      (code)+3,   pal, \
        128};

DEF_METASPRITE_2x2(playerRStand, 0xd8, 0);

DEF_METASPRITE_2x2(personToSave, 0xba, 1);

/*{pal:"nes",layout:"nes"}*/
const char PALETTE[32] = { 
  0x03,			// background color

  0x25,0x30,0x27,0x00,	// ladders and pickups
  0x1C,0x20,0x2C,0x00,	// floor blocks
  0x00,0x10,0x20,0x00,
  0x06,0x16,0x26,0x00,

  0x16,0x35,0x24,0x00,	// enemy sprites
  0x00,0x37,0x25,0x00,	// rescue person
  0x0D,0x2D,0x1A,0x00,
  0x0D,0x27,0x2A	// player sprites
};

// number of actors (4 h/w sprites each)
#define NUM_ACTORS 8

// actor x/y positions
byte actor_x[NUM_ACTORS];
byte actor_y[NUM_ACTORS];
// actor x/y deltas per frame (signed)
sbyte actor_dx[NUM_ACTORS];
sbyte actor_dy[NUM_ACTORS];

// main program
void main() {
  char i;
  char oam_id;
  char trig;    // trigger flags (newly pressed)
  char state;   // state flags (currently held)
  char vbright = 4;
  
  // print instructions
  vram_adr(NTADR_A(2,2));
  vram_write("PRESS A/B DEC/INC BRIGHT", 24);
  vram_adr(NTADR_A(2,4));
  vram_write("D-PAD USES PAD_STATE", 20);
  // setup graphics
  oam_hide_rest(0);
  pal_all(PALETTE);
  ppu_on_all();
  // initialize actors
  for (i=0; i<NUM_ACTORS; i++) {
    actor_x[i] = i*32;
    actor_y[i] = i*16+64;
    actor_dx[i] = 0;
    actor_dy[i] = 0;
  }
  // loop forever
  while (1) {
    oam_id = 0;
    // pad_trigger detects newly pressed buttons (edge detection)
    trig = pad_trigger(0);
    if (trig&PAD_LEFT) actor_dx[0]=-2;
    else if (trig&PAD_RIGHT) actor_dx[0]=2;
    else actor_dx[0]=0;
    // A/B buttons change brightness (trigger = one press per tap)
    if (trig&PAD_A) vbright--;
    if (trig&PAD_B) vbright++;
    // pad_state reads currently held buttons (continuous)
    state = pad_state(0);
    if (state&PAD_UP) actor_dy[0]=-2;
    else if (state&PAD_DOWN) actor_dy[0]=2;
    else actor_dy[0]=0;
    // draw and move all actors
    for (i=0; i<NUM_ACTORS; i++) {
      oam_id = oam_meta_spr(actor_x[i], actor_y[i], oam_id, playerRStand);
      actor_x[i] += actor_dx[i];
      actor_y[i] += actor_dy[i];
    }
    if (oam_id!=0) oam_hide_rest(oam_id);
    pal_bright(vbright);
    ppu_wait_frame();
  }
}
