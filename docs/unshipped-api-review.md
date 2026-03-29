# Unshipped API Review

Review of new APIs in `src/neslib/PublicAPI.Unshipped.txt` before they ship as public API.

Compared against the original [neslib.h](https://github.com/clbr/neslib/blob/master/neslib.h) by Shiru and the existing shipped C# API in `src/neslib/PublicAPI.Shipped.txt`.

## What's Good

1. **`delegate*<void>` for callbacks** — `nmi_set_callback` and `irq_set_callback` use C# function pointers. Type-safe, zero overhead, maps directly to hardware interrupt vectors.

2. **Hardware register constants** (PPU, APU, MMC3, MMC1, SRAM) — well-organized, clearly named, useful with `poke()`/`peek()`.

3. **MMC1 helper methods** — great layering: low-level `mmc1_write()` plus convenience wrappers `mmc1_set_prg_bank`, `mmc1_set_chr_bank`, `mmc1_set_mirroring`.

4. **`vrambuf_*` family** — matches the Shiru extensions faithfully, well-organized.

5. **`bcd_add`, `peek`/`poke`, `cnrom_set_chr_bank`** — clean, appropriate.

6. **Extra MASK tint constants** (`TINT_RED`/`TINT_GREEN`/`TINT_BLUE`, `MONO`) — the original neslib.h only has `MASK_SPR`/`MASK_BG`/`MASK_EDGE_*`. These are good additions for the full PPU mask register.

## Issues

### 1. ~~`MASK` should be a `[Flags] enum`, not a static class~~ ✅ Resolved

`MASK` is now a `[Flags] enum` and `ppu_mask()` accepts `MASK` instead of raw `byte`.

### 2. ~~`set_chr_mode` should be `mmc3_set_chr_bank`~~ ✅ Resolved

Renamed to `mmc3_set_chr_bank` for naming consistency across mapper APIs.

### 3. ~~`memfill(object dst, ...)` uses wrong parameter type~~ ✅ Resolved

Changed to `memfill(ushort addr, byte value, uint len)` to match `poke`/`peek`.

### 4. ~~`music_play(byte)` vs `play_music()` naming confusion~~ ✅ Resolved

Resolved: `play_music()` has been renamed to `music_tick()`. The names are now clearly distinct:
- `music_play(byte song)` — FamiTone (from neslib.h)
- `music_tick()` — dotnes custom music engine (advances one frame)

### 5. ~~Missing `OAM_FLIP_V`, `OAM_FLIP_H`, `OAM_BEHIND` constants~~ ✅ Resolved

Added as `OAM.FLIP_V`, `OAM.FLIP_H`, `OAM.BEHIND` in `Enums.cs`.

## C#-ification Opportunities

These match the neslib.h "vibes" perfectly but could be more idiomatic C#. Lower priority — whether to fix these is a style judgment call.

| API | Current | More C# |
|---|---|---|
| ~~`oam_size(byte)`~~ | ~~"0 for 8x8, 1 for 8x16"~~ | ~~`SpriteSize` enum~~ ✅ Done |
| ~~`vram_inc(byte)`~~ | ~~"0 for +1, not 0 for +32"~~ | ~~`bool` or enum~~ ✅ Done — now `vram_inc(VramIncrement)` |
| ~~`ppu_system()` returns `byte`~~ | ~~"0 for PAL, not 0 for NTSC"~~ | ~~`VideoSystem` enum~~ ✅ Done |
| ~~`MMC1_MIRROR_*` constants~~ | ~~`const byte`~~ | ~~`MMC1Mirror` enum~~ ✅ Done |
| ~~`music_pause(byte)`~~ | ~~"0 unpause, 1 pause"~~ | ~~`bool`~~ ✅ Done |
| `oam_off` ~~field~~ | ~~public mutable field~~ | ~~property~~ ✅ Done |

## Recommendation

All five issues and all C#-ification items have been resolved. The unshipped API is ready to ship.
