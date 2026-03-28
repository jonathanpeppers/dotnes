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

### 1. `MASK` should be a `[Flags] enum`, not a static class

`PAD` is already a `[Flags]` enum. `MASK` should follow the same pattern — these are bitflags meant to be OR'd together (`MASK.BG | MASK.SPR`). A `[Flags] enum` gives type safety and lets `ppu_mask()` accept `MASK` instead of raw `byte`.

```csharp
// Current (C-style) — requires an explicit cast because byte | byte promotes to int
ppu_mask((byte)(MASK.BG | MASK.SPR));

// Better (C#-style) — [Flags] enum lets | work naturally
[Flags] public enum MASK : byte { ... }
public static void ppu_mask(MASK mask);
ppu_mask(MASK.BG | MASK.SPR);  // compiles cleanly, type-safe
```

### 2. `set_chr_mode` should be `mmc3_set_chr_bank`

The method writes to MMC3-specific registers (`$8000`/`$8001`) but has a generic name. The `mmc1_*` family uses consistent `mmc1_` prefixes. This should be `mmc3_set_chr_bank` for naming consistency across mapper APIs.

### 3. `memfill(object dst, ...)` uses wrong parameter type

The C original is `void *dst`, but NES has no managed objects. `object` is semantically wrong for a hardware memory fill. This should be either:
- `memfill(ushort addr, byte value, uint len)` — address-based, matching `poke`/`peek`
- `memfill(byte[] dst, byte value, uint len)` — buffer-based

### 4. `music_play(byte)` vs `play_music()` naming confusion

These are dangerously similar names for different subsystems:
- `music_play(byte song)` — FamiTone (from neslib.h)
- `play_music()` — dotnes custom music engine

Users will confuse them. Consider renaming the custom engine method (e.g., `music_tick()`) or at minimum adding very prominent XML doc warnings.

### 5. Missing `OAM_FLIP_V`, `OAM_FLIP_H`, `OAM_BEHIND` constants

These are defined in the original neslib.h (`0x80`, `0x40`, `0x20`) and are commonly needed for sprite attribute manipulation. They are absent from both shipped and unshipped APIs.

## C#-ification Opportunities

These match the neslib.h "vibes" perfectly but could be more idiomatic C#. Lower priority — whether to fix these is a style judgment call.

| API | Current | More C# |
|---|---|---|
| `oam_size(byte)` | "0 for 8x8, 1 for 8x16" | `SpriteSize` enum |
| `vram_inc(byte)` | "0 for +1, not 0 for +32" | `bool` or enum |
| `MMC1_MIRROR_*` constants | `const byte` | `MMC1Mirror` enum |
| `music_pause(byte)` | "0 unpause, 1 pause" | `bool` |
| `oam_off` field | public mutable field | property |

## Recommendation

Fix the five issues above before shipping these APIs. The C#-ification table is more stylistic — keeping APIs C-like preserves the "NES programming in C#" vibe, but `MASK` as a static class (issue 1) is inconsistent with the existing `PAD` enum precedent and should definitely change.
