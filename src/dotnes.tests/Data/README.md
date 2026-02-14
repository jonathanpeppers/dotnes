# What these files are

* `*.debug.dll`: .NET assembly built in `Debug`
* `*.release.dll`: .NET assembly built in `Release`
* `*.nes`: generally the accompanying ROM downloaded from https://8bitworkshop.com
* `CHR_ROM.nes`: binary blob from [8bitworkshop][hello]
* `chr_generic.s`: sample assembly from [8bitworkshop][hello]

[hello]: https://8bitworkshop.com/v3.10.0/?platform=nes&file=hello.c

# Specific samples

* `attributetable`: `samples/attributetable` in repo
* `hello`: `samples/hello` in repo
* `flicker`: `samples/flicker` in repo — flicker demo with 24 actors and sprite cycling
* `metasprites`: `samples/metasprites` in repo — metasprite rendering demo
* `music`: `samples/music` in repo — APU music playback demo
* `tint`: `samples/tint` in repo — PPU tint/monochrome demo with controller input

## `onelocal`

Based on `attributetable`, but with the change:

```diff
++// For testing locals
++uint fill = 32*30;
++
// set background palette colors
pal_bg(PALETTE);

// fill nametable with diamonds
vram_adr(NAMETABLE_A);  // start address ($2000)
--vram_fill(0x16, 32 * 30);   // fill nametable (960 bytes)
++vram_fill(0x16, fill);   // fill nametable (960 bytes)
```

## `onelocalbyte`

Based on `attributetable`, but with the change:

```diff
++// For testing locals
++byte nametable = 0x16;
++
// set background palette colors
pal_bg(PALETTE);

// fill nametable with diamonds
vram_adr(NAMETABLE_A);  // start address ($2000)
--vram_fill(0x16, 32 * 30);   // fill nametable (960 bytes)
++vram_fill(nametable, 32 * 30);   // fill nametable (960 bytes)
```
