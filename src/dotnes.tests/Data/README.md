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