# MMC3 Bank Layout

`NESMmc3BankedLayout` is an opt-in deterministic ROM layout for mapper 4. It
changes ROM placement only; it does not emulate MMC3 hardware or insert runtime
bank-select writes.

## PRG layout

`NESPrgBanks` remains the iNES count in 16 KiB units. MMC3 physical PRG banks
are 8 KiB, so a project with `NESPrgBanks=4` has physical banks 0-7.

| Physical bank | CPU window | Contents |
|---|---|---|
| 0 through N-3 | `$8000` or `$A000` | Explicit `NESPrgBank` assets |
| N-2 | `$C000-$DFFF` | Start of the fixed transpiled C# program |
| N-1 | `$E000-$FFFF` | End of the fixed program and vectors |

The transpiled program must fit in `$C000-$FFF9`. NMI, RESET, and IRQ vectors
are always written to `$FFFA-$FFFF` in physical bank N-1, and RESET points to
`$C000`.

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
  <NESPrgBanks>4</NESPrgBanks>
  <NESChrBanks>2</NESChrBanks>
  <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
</PropertyGroup>

<ItemGroup>
  <NESPrgBank Include="level1.s" Bank="0" CpuAddress="0x8000" />
  <NESPrgBank Include="level2.bin" Bank="1" CpuAddress="0xA000" Offset="256" />
  <NESChrBank Include="level1-tiles.s" Bank="8" />
</ItemGroup>
```

All placements are sorted deterministically and zero-filled. The build fails
for missing files, invalid or reserved banks, invalid CPU windows, conflicting
windows for one physical bank, overlaps, out-of-range offsets, or bank
overflow.

## PRG assembly relocations

`.s` PRG assets use dotnes's ca65-compatible assembler. Their base address is
the declared `CpuAddress + Offset`. The linker resolves:

- absolute instruction operands such as `jsr`, `jmp`, `lda`, and `sta`
- low/high-byte immediates such as `#<label` and `#>label`
- `.word` and `.addr` data references
- references to labels in the fixed transpiled program or another bank asset

Runtime code must select the referenced switchable bank before using its CPU
address. Relative branches cannot cross `NESPrgBank` asset placements, even
when two items occupy the same physical bank; put branch-connected code in one
assembly item. dotnes rejects cross-item branches instead of producing a layout
whose behavior depends on mapper state. This first slice does not split
transpiled C# methods among switchable banks and does not synthesize bank-switch
trampolines.

## CHR layout

`NESChrBanks` remains the iNES count in 8 KiB units. MMC3 physical CHR banks
are 1 KiB, so `NESChrBanks=2` provides physical banks 0-15. `NESChrBank` uses
that zero-based physical bank index.

`.bin` assets are copied directly. `.s` assets contribute their non-empty
`CHARS` segments. Existing `NESAssembly` `CHARS` data still starts at physical
CHR bank 0 for compatibility; explicit placements may use any remaining
non-overlapping range.
