# 8bitworkshop NES Samples — dotnes Compatibility Analysis

> Source: https://github.com/sehugg/8bitworkshop/tree/master/presets/nes
>
> Analysis based on dotnes transpiler capabilities and the `NESLib.cs` API surface.
>
> Existing dotnes samples: `hello`, `hellofs`, `staticsprite`, `movingsprite`, `attributetable`, `flicker`, `metasprites`, `music`, `lols`, `tint`, `scroll`, `rletitle`, `tileset1`, `sprites`, `metacursor`, `metatrigger`, `statusbar`, `vrambuffer`, `horizscroll`, `horizmask`

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
- **Missing Features:** None — uses `ppu_off`, `pal_bg`, `pal_bright`, `vram_adr`, `vram_unrle`, `ppu_on_all`, `ppu_wait_frame`. User-defined functions inlined.

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

### horizscroll.c
- **Description:** Horizontal scrolling with vrambuf-based offscreen nametable updates, metatiles, and split-screen status bar.
- **Status:** ✅ Already Implemented (simplified)
- **dotnes sample:** `horizscroll`
- **Notes:** Simplified port demonstrating horizontal scrolling with `split()`, `vrambuf`, and vertical mirroring. Uses byte scroll counter (0-255). Full column generation with metatiles would require additional features (16-bit locals, shift opcodes, byte array vrambuf_put).
- **New Features Added:**
  - `ldarg` opcodes for user functions with byte parameters
  - `pushax` support for 16-bit argument passing
  - `incsp1`/`addysp` stack cleanup subroutines
  - `_ushortInAX` tracking for proper ushort argument pushing

### horizmask.c
- **Description:** Horizontal scrolling with random building generation using vertical VRAM writes to populate offscreen columns.
- **Status:** ✅ Already Implemented (simplified)
- **dotnes sample:** `horizmask`
- **Notes:** Port with simplified building generation (no attribute table updates or `nt2attraddr`). Uses `vrambuf_put` byte array overload for vertical column writes, runtime `NTADR_A`/`NTADR_B` with runtime first argument, and `Bge_s`/`Brtrue`/`Brfalse` (long-form) branch opcodes.
- **New Features Added:**
  - `vrambuf_put(ushort, byte[], byte)` overload for vertical sequential VRAM writes
  - Runtime division by power-of-2 (emits LSR A instructions)
  - `NTADR_A`/`B` with runtime first argument (x) support
  - `Bge_s` (branch if >=), `Brtrue`/`Brfalse` long-form with JMP trampoline
  - `updbuf` and `VRAMBUF_VERT` constants in NESLib

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
- **Note:** Uses `apu.c` for APU initialization — already covered by dotnes's built-in `apu_init()` subroutine. `vrambuf` module, `pad_trigger()`/`pad_state()`, and `for` loops are now available.
- **Missing Features:**
  - Direct APU register macros (`APU_PULSE_DECAY`, `APU_PULSE_SWEEP`, `APU_TRIANGLE_LENGTH`, `APU_NOISE_DECAY`, `APU_ENABLE`)
  - `APU.status` — direct hardware register reading
  - `typedef struct` — no struct support
  - `sprintf()` — no string formatting
  - Global arrays of structs, `const` struct arrays

### ppuhello.c
- **Description:** Directly programs PPU registers (`PPU.control`, `PPU.mask`, `PPU.vram.address`, `PPU.vram.data`) to display text — no neslib used.
- **Status:** 🔴 Complex
- **Missing Features:**
  - Direct PPU register access (`PPU.control`, `PPU.mask`, `PPU.vram.address`, `PPU.vram.data`, `PPU.scroll`)
  - `#include <nes.h>` — CC65 NES hardware header
  - `waitvsync()` — CC65-specific function
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
- **Note:** `split()`, `oam_size()`, `pad_trigger()`, `bank_bg()`/`bank_spr()`, and `for` loops are now available.
- **Missing Features:**
  - UxROM mapper (`NES_MAPPER 2`) with CHR RAM (`NES_CHR_BANKS 0`)
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
- **Note:** `vrambuf` module, `delay()`, `rand8()`, `OAM_FLIP_H`, `for` loops, `ushort` locals, `enum` types, and basic struct field access are now available.
- **Missing Features:**
  - FamiTone2 library (`famitone_init`, `sfx_init`, `sfx_play`, `music_play`, `music_stop`)
  - `nmi_set_callback()` with `famitone_update`
  - BCD arithmetic module (`bcd.h` / `bcd.c`)
  - `typedef struct` with bitfields (`Floor`, `Actor`)
  - Pointer arithmetic and pointer-to-struct operations
  - `memset()`, `memcpy()`
  - Arrays of structs, arrays of pointers
  - `bool` type, `static` variables
  - 20+ user-defined functions with complex control flow
  - `switch/case` with fallthrough

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

### shoot2.c
- **Description:** A shoot-em-up game with CHR RAM, sprite shifting, formation AI, and custom sound effects.
- **Status:** 🔴 Complex
- **Note:** Uses `apu.c` for APU initialization — already covered by dotnes's built-in `apu_init()` subroutine. `vrambuf` module, `oam_size()`, and `for` loops are now available.
- **Missing Features:**
  - UxROM mapper (`NES_MAPPER 2`) with CHR RAM
  - Direct APU register macros (`APU_PULSE_DECAY`, `APU_PULSE_SUSTAIN`, `APU_NOISE_DECAY`, `APU_TRIANGLE_SUSTAIN`, `APU_ENABLE`)
  - BCD module (`bcd_add`)
  - `typedef struct` (multiple: `FormationEnemy`, `AttackingEnemy`, `Missile`, `Sprite`)
  - Inline assembly (`asm()` statements for star animation)
  - `nesclock()` — declared but not transpiler-supported
  - `signed char` type
  - Extremely large tileset data (2048 bytes)
  - 30+ user-defined functions

### siegegame.c
- **Description:** A two-player surround/Tron-style game with AI, nametable collision detection, and attract mode.
- **Status:** 🔴 Complex
- **Note:** `vrambuf` module, `delay()`, and `for` loops are now available.
- **Missing Features:**
  - CC65 joystick library (`joystick.h`): `joy_install`, `joy_read`, `JOY_1`, `JOY_START_MASK`, etc.
  - `vram_read()` — for nametable collision detection
  - `typedef struct` with bitfields (`Player`)
  - `strlen()` standard library
  - `#include <nes.h>`, `#include <joystick.h>` — CC65 headers
  - 20+ user-defined functions
  - Complex game state management, AI logic

---

## Summary

| Status | Count | Samples |
|--------|-------|---------|
| ✅ Already Implemented | 16 | hello, attributes, flicker, metasprites, music, tint, scroll, rletitle, tileset1, sprites, metacursor, metatrigger, statusbar, vrambuffer, horizscroll, horizmask |
| 🟠 Moderate | 1 | bcd |
| 🔴 Complex | 12 | aputest, ppuhello, fami, bankswitch, monobitmap, conio, crypto, climber, transtable, irq, shoot2, siegegame |

> **Note:** `apu.c` and `vrambuf.c` are library files (not demos). `apu.c` is covered by dotnes's built-in `apu_init()` subroutine. `vrambuf.c` is covered by built-in `vrambuf_clear()`, `vrambuf_put()`, `vrambuf_end()`, `vrambuf_flush()`, and `set_vram_update()` subroutines. Neither is counted separately.

### Key Blockers (by frequency)

| Missing Feature | Samples Affected |
|-----------------|-----------------|
| User-defined functions with parameters | 25+ samples (byte params now supported; ushort/string params pending) |
| Global/static arrays | 15+ samples |
| `typedef struct` / struct support | 8 samples (basic field access now supported; arrays of structs, pointers to structs pending) |
| Direct APU/PPU register access | 5 samples (apu.c already covered by built-in `apu_init()`) |
| FamiTone2 library | 3 samples |
| `delay()` | 3 samples (now implemented) |
| Mapper support (MMC3, UxROM) | 3 samples |
| `signed byte` (sbyte) type | 4 samples |
| CC65-specific libraries (conio, joystick) | 2 samples |

### Features Now Implemented

| Feature | Samples Unlocked |
|---------|-----------------|
| vrambuf module (`vrambuf_clear`, `vrambuf_put`, `vrambuf_end`, `vrambuf_flush`) | horizscroll, horizmask |
| `vrambuf_put(ushort, byte[], byte)` byte array overload (vertical VRAM writes) | horizmask |
| `split()` function | horizscroll, horizmask, statusbar |
| `pad_trigger()` / `pad_state()` | metatrigger, tint |
| Runtime `NTADR_A/B/C/D(x, y)` with runtime x or y | vrambuffer, horizmask |
| `set_vram_update(ushort)` overload | vrambuffer |
| `Beq_s` opcode (branch if equal) | vrambuffer |
| `Bge_s` opcode (branch if >=) | horizmask |
| `Brtrue`/`Brfalse` long-form branches (JMP trampoline) | horizmask |
| Runtime division by power-of-2 (LSR A) | horizmask |
| `updbuf` and `VRAMBUF_VERT` constants | horizmask |
| Vertical mirroring (`<NESVerticalMirroring>`) | statusbar, horizscroll, horizmask |
| Static local functions | statusbar (`scroll_demo`), horizscroll, horizmask |
| `ldarg` opcodes (function parameters) | horizscroll (infrastructure for user functions with byte params) |
| `ushort` argument passing to built-ins (`pushax`) | horizscroll (16-bit scroll values) |
| `incsp1`/`addysp` stack cleanup subroutines | horizscroll (parameter cleanup for user methods) |
| `for` loops | all samples (C# `for` compiles to `br_s`+`blt_s` IL pattern, fully supported) |
| `ushort` locals (16-bit zero page variables) | horizmask (smooth 16-bit scroll counter) |
| `enum` types (compile to plain integer IL, no transpiler changes needed) | climber, siegegame (enum values, switch on enum) |
| Struct support (`stfld`, `ldfld`, `ldloca.s`) — byte/ushort fields on zero page | aputest, climber, shoot2, siegegame (struct field access and arithmetic) |

### Roadmap to climber.c

Prioritized TODO list of features needed to port climber.c, ordered by dependency and impact:

- [ ] **User functions with return values** — climber.c has 20+ functions returning `byte`, `bool`, or `word`. We already detect `hasReturnValue` in metadata; need to handle `ret` leaving result in A (byte) or A:X (ushort). Unlocks: `rndint`, `is_in_gap`, `get_floor_yy`, `get_closest_ladder`, `mount_ladder`, `check_collision`, `iabs`.
- [ ] **switch/case** — may already work for small cases (compiler generates branch chains). Verify with a RoslynTest; larger switches may need the `switch` IL opcode (jump table). Used in `move_actor` and `draw_actor`.
- [ ] **BCD arithmetic** — implement as `BCD` struct with `operator+` in NESLib, backed by a 6502 subroutine. Used for score tracking: `score = bcd_add(score, 1)`.
- [ ] **Global/static variables** — `stsfld`/`ldsfld` for user-defined fields (extend existing `oam_off` handling). Climber needs: `scroll_pixel_yy`, `scroll_tile_y`, `player_screen_y`, `score`, `vbright`. Could also be top-level locals captured by local functions.
- [ ] **sbyte (signed char)** — `Actor.yvel`, `Actor.xvel` are signed. Needs `conv.i1` handling and signed comparison branches (`Blt_s` already works for unsigned; may need signed variants).
- [ ] **Arrays of structs** — `Floor floors[MAX_FLOORS]`, `Actor actors[MAX_ACTORS]`. Need indexed struct access: base address + (index × struct size) + field offset.
- [ ] **memset/memcpy** — used for clearing buffers and arrays. Map to 6502 fill loop or built-in subroutine.
- [ ] **FamiTone2 integration** — `famitone_init`, `sfx_init`, `sfx_play`, `music_play`, `music_stop`, `nmi_set_callback(famitone_update)`. Requires linking external `.s` assembly files.
