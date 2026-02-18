# 8bitworkshop NES Samples — dotnes Compatibility Analysis

> Source: https://github.com/sehugg/8bitworkshop/tree/master/presets/nes
>
> Analysis based on dotnes transpiler capabilities and the `NESLib.cs` API surface.
>
> Existing dotnes samples: `hello`, `hellofs`, `staticsprite`, `movingsprite`, `attributetable`, `flicker`, `metasprites`, `music`, `lols`, `tint`, `scroll`, `rletitle`, `tileset1`, `sprites`, `metacursor`, `metatrigger`, `statusbar`, `vrambuffer`

---

## ✅ Already Implemented

### hello.c
- **Description:** Sets palette colors, writes "HELLO, WORLD!" to the nametable, and enables PPU rendering.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `hello`, `hellofs`
- **Missing Features:** None — uses `pal_col`, `vram_adr`, `vram_write`, `ppu_on_all`, `while(true)`.

### attributes.c
- **Description:** Fills the nametable with a tile pattern and copies an attribute table to VRAM to demonstrate palette selection per 16×16 region.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `attributetable`
- **Missing Features:** None — uses `pal_bg`, `vram_adr`, `vram_fill`, `vram_write`, `ppu_on_all`.

### flicker.c
- **Description:** Demonstrates sprite flickering by cycling through more metasprites than the 64-sprite hardware limit, using `oam_meta_spr_pal` and `oam_off`.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `flicker`
- **Missing Features:** None — uses `pal_all`, `oam_clear`, `oam_meta_spr_pal`, `oam_hide_rest`, `oam_off`, `ppu_wait_nmi`, `ppu_on_all`, `rand()`. All APIs are implemented.

### metasprites.c
- **Description:** Displays 16 bouncing 2×2 metasprites using `oam_meta_spr`.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `metasprites`
- **Missing Features:** None — all required APIs (`oam_meta_spr`, `oam_hide_rest`, `pal_all`, `oam_clear`, `ppu_on_all`, `ppu_wait_frame`, `rand`) are available.

### music.c
- **Description:** A custom music player that directly programs APU registers using `apu.h` macros to play "The Easy Winners" by Scott Joplin.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `music` (uses dotnes's own music engine with `start_music`, `play_music`, `set_music_pulse_table`, `set_music_triangle_table`)
- **Missing Features:** The 8bitworkshop version uses direct APU register macros (`APU_PULSE_DECAY`, `APU_TRIANGLE_LENGTH`, etc.) which dotnes does not support, but dotnes has its own equivalent music system.

### tint.c
- **Description:** Demonstrates PPU tint and monochrome bits via controller input and `ppu_mask()`.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `tint`
- **Missing Features:** None — uses `pal_all`, `oam_clear`, `vram_adr`, `vram_write`, `vram_fill`, `ppu_on_all`, `pad_poll`, `ppu_mask`, and `MASK.*` constants.

### scroll.c
- **Description:** Demonstrates vertical scrolling by writing text to two nametables and smoothly scrolling between them.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `scroll`
- **Missing Features:** None — uses `pal_col`, `vram_adr`, `vram_write`, `NTADR_A`, `NTADR_C`, `ppu_on_all`, `ppu_wait_nmi`, `scroll`. The C# version uses byte arithmetic to bounce within 0-239 range.

### rletitle.c
- **Description:** Unpacks RLE-compressed nametable data and fades in using `pal_bright`.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `rletitle`
- **Missing Features:** None — uses `ppu_off`, `pal_bg`, `pal_bright`, `vram_adr`, `vram_unrle`, `ppu_on_all`, `ppu_wait_frame`. User-defined functions inlined, `for` loop rewritten as `while`.

### tileset1.c
- **Description:** Loads a custom CHR tileset into CHR RAM and displays text using custom tile mapping.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `tileset1`
- **Missing Features:** None — uses `pal_bg`, `vram_adr`, `vram_write`, `ppu_on_all`. The CHR RAM approach is replaced with CHR ROM containing the same tileset data, padded so ASCII codes map directly to tile indices.

### sprites.c
- **Description:** Animates 32 hardware sprites moving around the screen with random velocities, wrapping at screen edges.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `sprites`
- **Missing Features:** None — uses `pal_all`, `oam_clear`, `oam_spr`, `oam_hide_rest`, `ppu_on_all`, `ppu_wait_frame`, `rand8()`. Reduced from 64 to 32 actors due to NES zero-page memory constraints.

### metacursor.c
- **Description:** Reads controller input to move metasprites, demonstrating `pad_poll` for two players.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `metacursor`
- **Simplifications:**
  - Reduced from 16 to 8 actors (zero-page memory limits)
  - Uses single metasprite for all actors (no animation frame cycling, which requires array-of-pointers)
  - Removed boundary checks (`actor_x[i] > 0`, `actor_x[i] < 240`) — actors wrap around screen edges
  - Inlined `setup_graphics()` into main body
  - Used `while` loops instead of `for`
  - Expanded C macros to byte array literals
  - Used `0x40` constant instead of `OAM_FLIP_H`

### metatrigger.c
- **Description:** Similar to metacursor but uses `pad_trigger()` and `pad_state()` for input, plus `pal_bright()` for brightness control.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `metatrigger`
- **Simplifications:**
  - Reduced from 16 to 8 actors (zero-page memory limits)
  - Uses single metasprite for all actors (no animation frame cycling)
  - Removed OAM buffer attribute manipulation
  - Inlined all setup code into main body
  - Used `while` loops instead of `for`

### statusbar.c
- **Description:** Demonstrates a split-screen status bar by using sprite 0 hit detection and the `split()` function for horizontal scrolling.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `statusbar`
- **Simplifications:**
  - `put_str()` inlined (user methods don't support parameters yet); `scroll_demo()` extracted as local function
  - `strlen()` replaced by `string.Length` (implicit via `vram_write(string)`)
  - Scroll range reduced from 0–479 to 0–255 (byte range, single nametable width)
  - Vertical mirroring configured via `<NESVerticalMirroring>true</NESVerticalMirroring>` MSBuild property
- **New Features Added:**
  - `split()` transpiler support (6502 subroutine + IL handler)
  - `<NESVerticalMirroring>` MSBuild property for vertical mirroring (iNES Flags6 bit 0)

### vrambuffer.c
- **Description:** Demonstrates the VRAM update buffer system for writing to VRAM during rendering, with scrolling and `sprintf`.
- **Status:** ✅ Already Implemented
- **dotnes sample:** `vrambuffer`
- **Simplifications:**
  - User-defined function (`scroll_demo`) inlined into main body
  - `sprintf()`/`memset()` replaced with fixed string literal
  - Uses `vrambuf_put(NTADR_A(x,y), "string")` instead of `vrambuf_put(addr, str, len)`
  - Simplified scroll demo: writes text lines vertically and stops (no bi-directional scrolling)
- **New Features Added:**
  - `vrambuf_clear()` and `vrambuf_put()` 6502 subroutines (VRAM update buffer at $0100)
  - `set_vram_update(ushort)` overload for raw address parameter
  - `NT_UPD_HORZ`, `NT_UPD_VERT`, `NT_UPD_EOF` constants in NESLib

---

## 🟠 Moderate (Significant Work Needed)

### bcd.c
- **Description:** Binary-Coded Decimal addition utility function.
- **Status:** 🟠 Moderate
- **Used by:** `climber.c`, `shoot2.c`
- **Missing Features:**
  - User-defined functions with return values (`bcd_add`)
  - `word` (16-bit) arithmetic with bitwise NOT (`~`), shift, XOR
  - `register` keyword (optimization hint, can be ignored)
  - This is a utility; dotnes would need a built-in BCD helper or function support

---

## 🔴 Complex (Major Features Needed)

### aputest.c
- **Description:** Generates random APU sounds and prints parameters to screen, showing channel status with vrambuf.
- **Status:** 🔴 Complex
- **Note:** Uses `apu.c` for APU initialization — already covered by dotnes's built-in `apu_init()` subroutine.
- **Missing Features:**
  - Direct APU register macros (`APU_PULSE_DECAY`, `APU_PULSE_SWEEP`, `APU_TRIANGLE_LENGTH`, `APU_NOISE_DECAY`, `APU_ENABLE`)
  - `APU.status` — direct hardware register reading
  - `typedef struct` — no struct support
  - `sprintf()` — no string formatting
  - `vrambuf_clear()`, `vrambuf_put()`, `set_vram_update()` — vrambuf module
  - `pad_trigger()`, `pad_state()` — not transpiler-supported
  - Global arrays of structs, `const` struct arrays
  - `PAD_START` constant for pad_state bitmask

### ppuhello.c
- **Description:** Directly programs PPU registers (`PPU.control`, `PPU.mask`, `PPU.vram.address`, `PPU.vram.data`) to display text — no neslib used.
- **Status:** 🔴 Complex
- **Missing Features:**
  - Direct PPU register access (`PPU.control`, `PPU.mask`, `PPU.vram.address`, `PPU.vram.data`, `PPU.scroll`)
  - `#include <nes.h>` — CC65 NES hardware header
  - `waitvsync()` — CC65-specific function
  - `for` loops
  - No neslib functions used at all — entirely hardware-register driven

### fami.c
- **Description:** Demonstrates the FamiTone2 library for music and sound effects, with controller-triggered SFX.
- **Status:** 🔴 Complex
- **Missing Features:**
  - `famitone_init()` — FamiTone2 library initialization
  - `sfx_init()` — sound effect initialization
  - `sfx_play()` — declared in NESLib but not transpiler-supported
  - `music_play()` — declared but the FamiTone2 variant needs linked assembly
  - `nmi_set_callback()` — implemented but needs FamiTone2's `famitone_update` function
  - External linked assembly files (`famitone2.s`, `music_aftertherain.s`, `demosounds.s`)
  - `extern char[]` declarations for linked data
  - `__fastcall__` calling convention

### horizscroll.c
- **Description:** Horizontal scrolling with vrambuf-based offscreen nametable updates, metatiles, and split-screen status bar.
- **Status:** 🔴 Complex
- **Missing Features:**
  - `vrambuf_clear()`, `vrambuf_put()`, `vrambuf_end()`, `vrambuf_flush()` — entire vrambuf module
  - `set_vram_update()` — not transpiler-supported
  - `split()` — not transpiler-supported
  - `VRAMBUF_PUT` macro, `VRAMBUF_VERT` constant, `updbuf` global
  - `memset()`, `memcpy()` — standard library
  - `register` keyword, `word` type
  - Multiple user-defined functions with parameters and return values
  - Global/static variables, arrays
  - Vertical mirroring configuration (`NES_MIRRORING 1`)

### horizmask.c
- **Description:** Similar to horizscroll but with building generation and attribute table updates during horizontal scrolling.
- **Status:** 🔴 Complex
- **Missing Features:**
  - Same as horizscroll.c: full vrambuf module, `split()`, `set_vram_update()`
  - `memset()`, `memcpy()` standard library
  - `VRAMBUF_PUT` macro, `VRAMBUF_VERT` constant
  - Multiple user-defined functions
  - `register` keyword, `word` type
  - Global variables and arrays

### bankswitch.c
- **Description:** Demonstrates MMC3 mapper bank switching for PRG and CHR ROM banks using `POKE` to mapper registers.
- **Status:** 🔴 Complex
- **Missing Features:**
  - MMC3 mapper support (`NES_MAPPER 4`)
  - `POKE()` to mapper registers ($8000, $8001, $A000) — dotnes `poke()` exists but mapper registers are untested
  - `#pragma rodata-name` / `#pragma code-name` — CC65 segment directives for placing code/data in specific banks
  - Multiple PRG/CHR bank configuration (`NES_PRG_BANKS 4`, `NES_CHR_BANKS 8`)
  - User-defined functions in specific code banks
  - `strlen()` standard library
  - `#include <peekpoke.h>` CC65 header

### monobitmap.c
- **Description:** Creates a monochrome framebuffer using CHR RAM with UxROM mapper, pixel-level drawing, and split-screen bank switching.
- **Status:** 🔴 Complex
- **Missing Features:**
  - UxROM mapper (`NES_MAPPER 2`) with CHR RAM (`NES_CHR_BANKS 0`)
  - `bank_bg()`, `bank_spr()` — declared but not transpiler-supported
  - `split()` — not transpiler-supported
  - `oam_size()` — declared but not transpiler-supported
  - `vram_read()` — declared but not transpiler-supported
  - `pad_trigger()` — not transpiler-supported
  - Inline assembly (`__asm__`) for cycle-accurate delay loops
  - Direct PPU register manipulation (`PPU.control`)
  - `abs()` standard library function
  - Multiple user-defined functions with complex logic
  - `bool` type, `static` local variables

### conio.c
- **Description:** Uses CC65's conio (console I/O) library to draw borders and text — completely CC65-specific.
- **Status:** 🔴 Complex
- **Missing Features:**
  - Entire CC65 conio library (`conio.h`): `bgcolor`, `clrscr`, `screensize`, `cputc`, `chline`, `cvlinexy`, `gotoxy`, `cprintf`
  - CC65 joystick library (`joystick.h`): `joy_install`, `joy_read`, `joy_uninstall`
  - `<stdlib.h>` and `<string.h>` functions
  - `EXIT_SUCCESS` return from `main()`
  - No neslib functions used — entirely CC65-framework driven

### crypto.c
- **Description:** A complex cryptographic/puzzle game with extensive game logic, AI, and state management.
- **Status:** 🔴 Complex
- **Missing Features:**
  - Extremely large codebase (100+ KB) with dozens of functions
  - `typedef struct` and struct instances — no struct support
  - Extensive use of pointers, arrays of structs, bitfields
  - `static` variables, `const` arrays
  - Multiple user-defined functions with complex control flow
  - `switch/case` statements
  - `sfx_play()` — not transpiler-supported
  - Far exceeds dotnes's current single-top-level-statement model

### climber.c
- **Description:** A full platform game with random level generation, enemy AI, scrolling, FamiTone2 music, and collision detection.
- **Status:** 🔴 Complex
- **Missing Features:**
  - FamiTone2 library (`famitone_init`, `sfx_init`, `sfx_play`, `music_play`, `music_stop`)
  - `nmi_set_callback()` with `famitone_update`
  - BCD arithmetic module (`bcd.h` / `bcd.c`)
  - vrambuf module (`vrambuf_clear`, `vrambuf_put`, `vrambuf_flush`, `set_vram_update`)
  - `typedef struct` with bitfields (`Floor`, `Actor`)
  - `typedef enum` (multiple enums)
  - Pointer arithmetic and pointer-to-struct operations
  - `memset()`, `memcpy()`, `rand()` / `rand8()`
  - Arrays of structs, arrays of pointers
  - `bool` type, `static` variables
  - `delay()` — declared but not transpiler-supported
  - 20+ user-defined functions with complex control flow
  - `switch/case` with fallthrough
  - `OAM_FLIP_H` constant and metasprite macros

### transtable.c
- **Description:** Custom CHR tileset loaded into CHR RAM with `#pragma charmap` translation tables for text display.
- **Status:** 🔴 Complex
- **Missing Features:**
  - CHR RAM support (`NES_CHR_BANKS 0`) — dotnes only supports CHR ROM
  - `#pragma charmap` / character translation tables — no equivalent in C#
  - `#pragma data-name` — CC65 segment directives
  - Large `const byte[]` tileset data (768 bytes) loaded via `vram_write`
  - `strlen()` standard library function
  - `sizeof()` operator
  - User-defined function (`put_str`)

### irq.c
- **Description:** Multiple screen splits using MMC3 mapper IRQs to change X scroll at different scanlines, creating a wavy effect.
- **Status:** 🔴 Complex
- **Missing Features:**
  - MMC3 mapper support (`NES_MAPPER 4`)
  - `__asm__` inline assembly (`cli` instruction, mapper strobe macros)
  - `__A__` — access to 6502 A register from C
  - `__fastcall__` calling convention for callback
  - `PPU.scroll` — direct PPU register struct access
  - `POKE()` to mapper registers ($A000, $A001, $C000, $C001, $E000, $E001)
  - `word` (16-bit) arrays with 128 elements
  - `set_ppu_ctrl_var()` / `get_ppu_ctrl_var()` — declared but not transpiler-supported
  - `ppu_wait_frame()` — declared but may lack transpiler support
  - User-defined functions with `__fastcall__` attribute
  - `for` loops → must use `while`

### shoot2.c
- **Description:** A shoot-em-up game with CHR RAM, sprite shifting, formation AI, and custom sound effects.
- **Status:** 🔴 Complex
- **Note:** Uses `apu.c` for APU initialization — already covered by dotnes's built-in `apu_init()` subroutine.
- **Missing Features:**
  - UxROM mapper (`NES_MAPPER 2`) with CHR RAM
  - Direct APU register macros (`APU_PULSE_DECAY`, `APU_PULSE_SUSTAIN`, `APU_NOISE_DECAY`, `APU_TRIANGLE_SUSTAIN`, `APU_ENABLE`)
  - BCD module (`bcd_add`)
  - vrambuf module (`vrambuf_clear`, `vrambuf_put`, `vrambuf_flush`, `set_vram_update`)
  - `typedef struct` (multiple: `FormationEnemy`, `AttackingEnemy`, `Missile`, `Sprite`)
  - Inline assembly (`asm()` statements for star animation)
  - `#pragma codesize` compiler directive
  - `oam_size()` — 8×16 sprite mode
  - `nesclock()` — declared but not transpiler-supported
  - `signed char` type, `register` keyword
  - Extremely large tileset data (2048 bytes)
  - Lookup tables with complex indexing
  - 30+ user-defined functions

### siegegame.c
- **Description:** A two-player surround/Tron-style game with AI, nametable collision detection, and attract mode.
- **Status:** 🔴 Complex
- **Missing Features:**
  - CC65 joystick library (`joystick.h`): `joy_install`, `joy_read`, `JOY_1`, `JOY_START_MASK`, etc.
  - vrambuf module (`vrambuf_clear`, `vrambuf_put`, `vrambuf_flush`, `set_vram_update`)
  - `vram_read()` — declared but not transpiler-supported (used for collision detection)
  - `delay()` — declared but not transpiler-supported
  - `typedef struct` with bitfields (`Player`)
  - `typedef enum` (`dir_t`)
  - `strlen()` standard library
  - `#include <nes.h>`, `#include <joystick.h>` — CC65 headers
  - 20+ user-defined functions
  - Complex game state management, AI logic

---

## Summary

| Status | Count | Samples |
|--------|-------|---------|
| ✅ Already Implemented | 15 | hello, attributes, flicker, metasprites, music, tint, scroll, rletitle, tileset1, sprites, metacursor, metatrigger, statusbar, vrambuffer |
| 🟡 Feasible | 0 | |
| 🟠 Moderate | 1 | bcd |
| 🔴 Complex | 14 | aputest, ppuhello, fami, horizscroll, horizmask, bankswitch, monobitmap, conio, crypto, climber, transtable, irq, shoot2, siegegame |

> **Note:** `apu.c` and `vrambuf.c` are library files (not demos). `apu.c` is covered by dotnes's built-in `apu_init()` subroutine. `vrambuf.c` is covered by built-in `vrambuf_clear()`, `vrambuf_put()`, and `set_vram_update()` subroutines. Neither is counted separately.

### Key Blockers (by frequency)

| Missing Feature | Samples Affected |
|-----------------|-----------------|
| User-defined functions | 25+ samples |
| `for` loops (must use `while`) | 20+ samples |
| Global/static arrays | 15+ samples |
| vrambuf module (`vrambuf_clear`, `vrambuf_put`, etc.) | 9 samples (core now implemented) |
| `typedef struct` / struct support | 8 samples |
| `split()` | 4 samples (`split()` now implemented) |
| FamiTone2 library | 3 samples |
| Direct APU/PPU register access | 5 samples (apu.c already covered by built-in `apu_init()`) |
| `pad_trigger()` / `pad_state()` | 2 samples (aputest, monobitmap) |
| `vram_unrle()` | 1 sample (rletitle now implemented) |
| `delay()` | 3 samples |
| `signed byte` (sbyte) type | 4 samples |
| Mapper support (MMC3, UxROM) | 3 samples |
| `bank_bg()` / `bank_spr()` | 2 samples |
| CC65-specific libraries (conio, joystick) | 2 samples |
