# NESLib API Gaps

Methods declared in `NESLib.cs` that do not yet have a transpiler implementation (no 6502 subroutine in `BuiltInSubroutines.cs` and no special handling in `IL2NESWriter.cs` or `Transpiler.cs`). These stubs compile but will fail at transpilation time.

## Audio / FamiTone

| Method | Description |
|--------|-------------|
| `music_play(byte)` | Start/resume music playback |
| `music_stop()` | Stop music playback |
| `music_pause(bool)` | Pause/unpause music |
| `sample_play(byte)` | Play a DPCM sample |

## VRAM / Memory

| Method | Description |
|--------|-------------|
| `vram_unlz4(byte[], byte[], uint)` | Decompress LZ4 data to VRAM |
| `memfill(ushort, byte, uint)` | Fill memory range with a value |
| `oam_clear_fast()` | Fast OAM clear (no parameters) |
| `oam_meta_spr_clip(int, byte, byte[])` | Meta-sprite with clipping |

## Utility

| Method | Description |
|--------|-------------|
| `rand()` | Random byte 0–255 (alias of `rand8`, but no transpiler mapping yet) |
| `MSB(ushort)` | Get most significant byte (compile-time only, no 6502 codegen) |
| `LSB(ushort)` | Get least significant byte (compile-time only, no 6502 codegen) |

> **Note:** `rand8()`, `set_rand()`, `rand16()`, and `srand()` ARE implemented. `rand()` (byte alias) has no transpiler dispatch yet.

## Already Implemented (for reference)

These methods have full transpiler support — either a `BuiltInSubroutines` block or special handling in `IL2NESWriter`:

`pal_all`, `pal_bg`, `pal_spr`, `pal_col`, `pal_clear`, `pal_spr_bright`, `pal_bg_bright`, `pal_bright`, `ppu_off`, `ppu_on_all`, `ppu_on_bg`, `ppu_on_spr`, `ppu_mask`, `ppu_wait_nmi`, `ppu_wait_frame`, `ppu_system`, `get_ppu_ctrl_var`, `set_ppu_ctrl_var`, `oam_clear`, `oam_size`, `oam_hide_rest`, `oam_spr`, `oam_meta_spr`, `oam_meta_spr_pal`, `rand8`, `rand16`, `set_rand`, `srand`, `bcd_add`, `scroll`, `split`, `vram_adr`, `vram_put`, `vram_fill`, `vram_read`, `vram_write(byte[])`, `vram_write(string)`, `vram_unrle`, `vram_inc`, `set_vram_update`, `flush_vram_update`, `vrambuf_clear`, `vrambuf_put`, `vrambuf_put_vert`, `vrambuf_end`, `vrambuf_flush`, `bank_spr`, `bank_bg`, `nesclock`, `delay`, `pad_poll`, `pad_trigger`, `pad_state`, `poke`, `peek`, `cli`, `sei`, `waitvsync`, `apu_init`, `start_music`, `music_tick`, `set_music_pulse_table`, `set_music_triangle_table`, `famitone_init`, `sfx_init`, `sfx_play`, `nmi_set_callback`, `irq_set_callback`, `cnrom_set_chr_bank`, `mmc1_write`, `mmc1_set_prg_bank`, `mmc1_set_chr_bank`, `mmc1_set_mirroring`, `mmc3_set_chr_bank`, `NTADR_A`, `NTADR_B`, `NTADR_C`, `NTADR_D`, `oam_off`
