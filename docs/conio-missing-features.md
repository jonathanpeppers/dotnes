# conio.c — Missing CC65 Features

This document lists CC65 features used by the original `conio.c` sample from
[8bitworkshop](https://8bitworkshop.com/) that are **not available** in dotnes.
The `samples/conio/` port uses equivalent NES VRAM functions instead.

## CC65 conio library (`<conio.h>`)

| CC65 Function | Description | dotnes Equivalent |
|---------------|-------------|-------------------|
| `bgcolor(color)` | Set background color | `pal_col(0, color)` |
| `clrscr()` | Clear screen | `vram_adr(NTADR_A(0,0)); vram_fill(0x00, 960)` |
| `screensize(&w, &h)` | Query screen dimensions | Hard-coded `32×30` (NES nametable) |
| `gotoxy(x, y)` | Move cursor to position | `vram_adr(NTADR_A(x, y))` |
| `cputc(ch)` | Write single character | `vram_put(ch)` |
| `cprintf(fmt, ...)` | Formatted console print | `vram_write("literal")` (no format strings) |
| `chline(len)` | Draw horizontal line | `vram_fill(0x2D, len)` |
| `cvlinexy(x, y, len)` | Draw vertical line at position | `vram_adr(NTADR_A(x, y)); vram_inc(1); vram_fill(tile, len); vram_inc(0)` |

### What cannot be ported

- **Format strings** — `cprintf("%s", var)` requires runtime string formatting,
  which the 6502 transpiler does not support. Use string literals instead.
- **Cursor tracking** — CC65 conio maintains an internal cursor position that
  auto-advances after each character. dotnes requires explicit `vram_adr()` calls.
- **Box-drawing characters** — `CH_ULCORNER`, `CH_URCORNER`, `CH_LLCORNER`,
  `CH_LRCORNER` are CC65-specific tile indices for the NES font. The port uses
  standard ASCII characters (`+`, `-`, `|`) which may render differently
  depending on the CHR ROM tileset.

## CC65 joystick library (`<joystick.h>`)

| CC65 Function | Description | dotnes Equivalent |
|---------------|-------------|-------------------|
| `joy_install(drv)` | Install joystick driver | Not needed (built into NES hardware) |
| `joy_read(port)` | Read joystick state | `pad_poll(port)` |
| `joy_uninstall()` | Remove joystick driver | Not needed |

## C standard library

| CC65 Function | Description | dotnes Equivalent |
|---------------|-------------|-------------------|
| `strlen(s)` | String length | Not available at runtime; use constants |
| `EXIT_SUCCESS` | Return code | N/A — NES programs never exit |
| `return` from `main()` | Exit program | N/A — use `while (true) ;` instead |

## Summary

The dotnes port achieves the same visual result (blue screen, border, centered
text, wait for input, clear screen) using lower-level VRAM functions. The main
gap is the lack of a cursor-tracking console abstraction and runtime string
formatting. These are CC65-framework features that do not map naturally to the
NES hardware model used by dotnes.
