# NESLib API Gaps

Methods declared in `NESLib.cs` that do not yet have a transpiler implementation (no 6502 subroutine in `BuiltInSubroutines.cs` and no special handling in `IL2NESWriter.cs` or `Transpiler.cs`). These stubs compile but will fail at transpilation time.

## Audio / FamiTone

| Method | Description |
|--------|-------------|
| `music_play(byte)` | Start/resume music playback |
| `music_stop()` | Stop music playback |
| `music_pause(bool)` | Pause/unpause music |
| `famitone_init(byte[])` | Initialize FamiTone engine |
| `sfx_init(byte[])` | Initialize SFX engine |
| `sfx_play(byte, byte)` | Play a sound effect |
| `sample_play(byte)` | Play a DPCM sample |

## VRAM / Memory

| Method | Description |
|--------|-------------|
| `vram_write(string)` | Write string to VRAM (overload) |
| `vram_unlz4(byte[])` | Decompress LZ4 data to VRAM |
| `memfill(byte[], uint, byte)` | Fill memory range with a value |
| `oam_clear_fast()` | Fast OAM clear (no parameters) |
| `oam_meta_spr_clip(int, int, byte[])` | Meta-sprite with clipping |

## Utility

| Method | Description |
|--------|-------------|
| `pal_trigger()` | Trigger palette update |
| `poke(uint, byte)` | Write byte to memory address |
| `rand()` | 16-bit random number |
| `MSB(ushort)` | Get most significant byte |
| `LSB(ushort)` | Get least significant byte |

> **Note:** `rand8()` and `set_rand()` ARE implemented. `rand()` (16-bit variant) is not.

## Already Implemented (for reference)

These methods have full transpiler support — either a `BuiltInSubroutines` block or special handling in `IL2NESWriter`:

`pal_all`, `pal_bg`, `pal_spr`, `pal_col`, `pal_clear`, `pal_spr_bright`, `pal_bg_bright`, `pal_bright`, `ppu_off`, `ppu_on_all`, `ppu_on_bg`, `ppu_on_spr`, `ppu_mask`, `ppu_wait_nmi`, `ppu_wait_frame`, `ppu_system`, `get_ppu_ctrl_var`, `set_ppu_ctrl_var`, `oam_clear`, `oam_size`, `oam_hide_rest`, `oam_spr`, `oam_meta_spr`, `oam_meta_spr_pal`, `rand8`, `set_rand`, `bcd_add`, `scroll`, `split`, `vram_adr`, `vram_put`, `vram_fill`, `vram_read`, `vram_write(byte)`, `vram_unrle`, `vram_inc`, `set_vram_update`, `flush_vram_update`, `vrambuf_clear`, `vrambuf_put`, `vrambuf_end`, `vrambuf_flush`, `bank_spr`, `bank_bg`, `nesclock`, `delay`, `pad_poll`, `pad_trigger`, `pad_state`, `apu_init`, `start_music`, `play_music`, `set_music_pulse_table`, `set_music_triangle_table`, `NTADR_A`, `NTADR_B`, `NTADR_C`, `NTADR_D`
