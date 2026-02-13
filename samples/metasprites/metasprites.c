/*
Metasprites demo for 8bitworkshop.com (NES platform).
Paste into https://8bitworkshop.com to compile.
Based on https://github.com/sehugg/8bitworkshop/blob/master/presets/nes/metasprites.c
*/

#include "neslib.h"

//#link "chr_generic.s"

#define TILE 0xd8
#define ATTR 0

const unsigned char metasprite[]={
    0,   0,   TILE+0, ATTR,
    0,   8,   TILE+1, ATTR,
    8,   0,   TILE+2, ATTR,
    8,   8,   TILE+3, ATTR,
    128
};

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

#define NUM_ACTORS 16

byte actor_x[NUM_ACTORS];
byte actor_y[NUM_ACTORS];
byte actor_dx[NUM_ACTORS];
byte actor_dy[NUM_ACTORS];

void main() {
    byte i;
    byte oam_id;

    oam_clear();
    pal_all(PALETTE);
    ppu_on_all();

    i = 0;
    while (i < NUM_ACTORS) {
        actor_x[i] = rand8();
        actor_y[i] = rand8();
        actor_dx[i] = (rand8() & 7) - 3;
        actor_dy[i] = (rand8() & 7) - 3;
        i++;
    }

    while (1) {
        oam_id = 0;
        i = 0;
        while (i < NUM_ACTORS) {
            oam_id = oam_meta_spr(actor_x[i], actor_y[i], oam_id, metasprite);
            actor_x[i] += actor_dx[i];
            actor_y[i] += actor_dy[i];
            i++;
        }
        if (oam_id != 0) oam_hide_rest(oam_id);
        ppu_wait_frame();
    }
}
