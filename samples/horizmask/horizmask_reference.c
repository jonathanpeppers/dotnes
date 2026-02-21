/*
 * Reference C version matching the dotnes horizmask C# sample exactly.
 * Build on 8bitworkshop.com to get a reference ROM for comparison.
 *
 * Differences from original 8bitworkshop horizmask.c:
 * - No attribute table updates (no nt2attraddr, no VRAMBUF_PUT)
 * - Single "HORIZMASK DEMO" header instead of multi-line text
 * - x is a byte (0-255) instead of word x_scroll
 * - sub-pixel counter instead of (x_scroll & 7) == 0
 * - col_counter tracks next column directly
 * - All state is local to scroll_demo()
 */

#include "neslib.h"
#include <string.h>

// 0 = horizontal mirroring
// 1 = vertical mirroring
#define NES_MIRRORING 1

// VRAM update buffer
#include "vrambuf.h"
//#link "vrambuf.c"

// link the pattern table into CHR ROM
//#link "chr_generic.s"

#define PLAYROWS 26

/*{pal:"nes",layout:"nes"}*/
const char PALETTE[32] = { 
  0x03,

  0x25,0x30,0x27,0x00,
  0x1C,0x20,0x2C,0x00,
  0x00,0x10,0x20,0x00,
  0x06,0x16,0x26,0x00,

  0x16,0x35,0x24,0x00,
  0x00,0x37,0x25,0x00,
  0x0D,0x2D,0x1A,0x00,
  0x0D,0x27,0x2A
};

void scroll_demo() {
  byte bldg_height;
  byte bldg_width;
  byte bldg_char;
  byte x;
  byte col_counter;
  byte sub;
  char buf[PLAYROWS];
  byte i, j, roofIdx;

  bldg_height = (rand8() & 7) + 2;
  bldg_width = (rand8() & 3) * 4 + 4;
  bldg_char = rand8() & 15;
  x = 0;
  col_counter = 32;
  sub = 0;

  while (1) {
    if (sub == 0) {
      // Clear buffer
      i = 0;
      while (i < PLAYROWS) {
        buf[i] = 0;
        i++;
      }

      // Draw a random star in the sky
      buf[rand8() & 15] = 46; // '.'

      // Draw building roof
      roofIdx = PLAYROWS - bldg_height - 1;
      buf[roofIdx] = bldg_char & 3;

      // Draw building body
      j = PLAYROWS - bldg_height;
      while (j < PLAYROWS) {
        buf[j] = bldg_char;
        j++;
      }

      // Write vertical column to offscreen nametable area
      if (col_counter < 32)
        vrambuf_put(NTADR_A(col_counter, 4) | VRAMBUF_VERT, buf, PLAYROWS);
      else
        vrambuf_put(NTADR_B(col_counter & 31, 4) | VRAMBUF_VERT, buf, PLAYROWS);

      // Advance column
      col_counter = (col_counter + 1) & 63;

      // Generate new building?
      --bldg_width;
      if (bldg_width == 0) {
        bldg_height = (rand8() & 7) + 2;
        bldg_width = (rand8() & 3) * 4 + 4;
        bldg_char = rand8() & 15;
      }
    }

    // Advance sub-pixel counter
    ++sub;
    if (sub == 8)
      sub = 0;

    ppu_wait_nmi();
    vrambuf_clear();
    split(x, 0);
    ++x;
  }
}

void main(void) {
  // Set palette
  pal_all(PALETTE);

  // Write header text
  vram_adr(NTADR_A(7, 0));
  vram_write("HORIZMASK DEMO", 14);
  vram_adr(NTADR_A(0, 3));
  vram_fill(5, 32);

  // Set sprite 0 for split
  oam_clear();
  oam_spr(1, 30, 0xa0, 0, 0);

  // Enable VRAM update buffer
  vrambuf_clear();
  set_vram_update(updbuf);

  // Turn on rendering
  ppu_on_all();

  // Start scrolling (never returns)
  scroll_demo();
}
