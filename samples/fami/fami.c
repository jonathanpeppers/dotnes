
/*
Demonstrates the Famitone2 library for sound and music.
Press controller buttons to hear sound effects.
*/

#include "neslib.h"

// link the pattern table into CHR ROM
//#link "chr_generic.s"

// setup Famitone library

//#link "famitone2.s"
void __fastcall__ famitone_update(void);
//#link "music_aftertherain.s"
extern char after_the_rain_music_data[];
//#link "music_dangerstreets.s"
extern char danger_streets_music_data[];
//#link "demosounds.s"
extern char demo_sounds[];

void main(void)
{
  pal_col(1,0x04);
  pal_col(2,0x20);
  pal_col(3,0x30);
  vram_adr(NTADR_A(2,2));
  vram_write("FAMITONE2 DEMO", 14);
  // initialize music system
  //famitone_init(after_the_rain_music_data);
  famitone_init(danger_streets_music_data);
  sfx_init(demo_sounds);
  // set music callback function for NMI
  nmi_set_callback(famitone_update);
  // play music
  music_play(0);
  //enable rendering
  ppu_on_all();
  // repeat forever
  while(1) {
    // poll controller 0
    char pad = pad_poll(0);
    // play sounds when buttons pushed
    if (pad & PAD_A) {
      sfx_play(0,0);
    }
    if (pad & PAD_B) {
      sfx_play(1,1);
    }
    if (pad & PAD_LEFT) {
      sfx_play(2,2);
    }
    if (pad & PAD_RIGHT) {
      sfx_play(3,3);
    }
  }
}
