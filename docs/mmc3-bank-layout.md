# MMC3 Bank Layout

`NESMmc3BankedLayout` is an opt-in deterministic ROM layout for mapper 4. It
does not emulate MMC3 hardware. On its own it changes ROM placement only;
named managed code regions additionally enable compiler-generated runtime
bank-switch gates. Projects without managed banking retain their existing output.

## PRG layout

`NESPrgBanks` remains the iNES count in 16 KiB units. MMC3 physical PRG banks
are 8 KiB, so a project with `NESPrgBanks=4` has physical banks 0-7.

| Physical bank | CPU window | Contents |
|---|---|---|
| 0 through N-3 | `$8000` or `$A000` | Explicit `NESPrgBank` assets; managed regions at `$8000` only |
| N-2 | `$C000-$DFFF` in PRG mode 0 | Start of the transpiled C# program |
| N-1 | `$E000-$FFFF` | Always-fixed end of the program, reset stub, and vectors |

The fixed program, including any managed bank-switch gates, must fit in
`$C000-$FFF1`. NMI, RESET, and IRQ vectors
are always written to `$FFFA-$FFFF` in physical bank N-1. RESET points to an
eight-byte stub at `$FFF2` that selects PRG mode 0 and jumps to `$C000`.

The program and interrupt handlers rely on physical bank N-2 remaining mapped
at `$C000`. Runtime code must therefore keep MMC3 bank-select bit 6 clear after
the reset stub initializes it. PRG mode 1 moves N-2 to `$8000` and makes
`$C000` switchable, which unmaps part of the executing program.

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
overflow. MMC3 hardware limits banked layouts to at most 32 `NESPrgBanks`
(64 physical 8 KiB banks) and 32 `NESChrBanks` (256 physical 1 KiB banks).

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
whose behavior depends on mapper state. PRG assets alone do not move managed
methods or generate bank-switch gates; use named managed regions below.

## Managed code regions

Annotate a static class or a static method/local function with
`NES.NESCodeBankAttribute` to place its managed method bodies in a named region:

```csharp
using NES;

[NESCodeBank("audio")]
static class Audio
{
    static byte state;

    public static void Reset() => state = 0;

    public static byte Tick(byte input)
    {
        state = input;
        return state;
    }
}
```

Each name must be nonempty and match an `NESManagedCodeBank` item exactly,
including case. A method annotation overrides its declaring class's region.
Attributes are not inherited or repeatable. A class annotation applies to
managed method bodies, not native extern declarations, other types, static
storage, or unrelated helpers. Annotated local functions must be static and
capture no state.

Configure the physical placement and an explicit home bank in the project:

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
  <NESPrgBanks>6</NESPrgBanks>
  <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
  <NESMmc3ManagedHomeBank>0</NESMmc3ManagedHomeBank>
  <NESMmc3ManagedInterruptContract>NonNestingChrCallbacks</NESMmc3ManagedInterruptContract>
</PropertyGroup>
<ItemGroup>
  <NESManagedCodeBank Include="audio"
                      Bank="2"
                      CpuAddress="0x8000"
                      Offset="0"
                      Size="0x1000" />
</ItemGroup>
```

`Include` is a logical name, not a file or an `NESAssembly` asset. `Bank`,
`CpuAddress`, and `Size` are required; `Offset` defaults to zero. Numeric
metadata accepts decimal, `0x`-prefixed hex, or `$`-prefixed hex. `Bank` counts
physical 8 KiB banks, unlike the 16 KiB `NESPrgBanks` count. The final two
physical banks remain reserved for fixed code.

This version supports foreground R6 banking at `$8000` in PRG mode 0 only,
not R7 banking at `$A000` or automatic method partitioning. `Size` reserves
the **entire** declared range, including unused bytes. Code must fit within
that size, and `Offset + Size` must fit within one 8 KiB bank. An `NESPrgBank`
asset can share a physical bank only outside every reserved range and with
a compatible CPU window. Duplicate names, unresolved annotations, invalid
placements, overlaps, and overflow are build errors.

Calls from fixed code enter compiler-generated gates in the fixed program.
Each gate selects the method's region and restores `NESMmc3ManagedHomeBank`
before returning, while preserving the supported argument/return ABI and
the caller's selector context. Startup initializes R6 to the home bank after
RAM initialization and before enabling NMI. The home bank must be explicitly
configured; it does not authorize arbitrary foreground R6 changes.
Same-region calls are direct and retain eligible pure-byte-expression inlining;
inlining never crosses a managed-bank boundary.

Gates share a flag/register-preserving return path. The compiler inlines their
entry bookkeeping when the whole set fits the unchanged fixed-bank capacity,
otherwise retaining compact entries. If effect analysis proves banked methods
and their reachable helpers never write the selector, return gates also omit
redundant R6 selection: interrupt epilogues preserve the published selector.
The inline/compact forms add 87/106 CPU cycles in that case, or 103/122 when
banked code can select CHR registers, excluding the unchanged callee body and
interrupts. These selections need no consumer build step or additional mapper
contract.

Banked and fixed code share the existing zero-initialized RAM allocation.
Use ordinary C# initialization methods such as `Reset`; unsupported implicit
static initializers and static constructors associated with banked types are
rejected. Methods cannot recurse, reenter a bank, call another managed region,
or call back into authored fixed managed methods. Compiler built-ins and
source-visible native helpers require supported, stable mapping/effects.
Cross-gate arguments initially support by-value `byte`, `sbyte`, and `bool`;
returns support `void`, `byte`, `sbyte`, `bool`, `short`, and `ushort`.
Byrefs, array/pointer gate arguments, generics, captured closures, indirect
calls, and delegate/function-pointer escapes are unsupported.

### Desktop compilation API

`NesCompiler.CompileBanked` exposes the fixed program and named managed regions
without writing an iNES image. `CompilationOptions.PrgBankAssets` accepts the
same native `.s` and binary PRG placements as stock `NESPrgBank` items:

```csharp
using dotnes;

using var assembly = File.OpenRead("Game.dll");
var options = new CompilationOptions
{
    Mapper = 4,
    PrgBanks = 6,
    Mmc3BankedLayout = true,
    Mmc3ManagedHomeBank = 0,
    ManagedCodeBanks =
    {
        new ManagedCodeBank { Name = "audio", Bank = 2, Size = 0x1000 },
    },
    PrgBankAssets =
    {
        new PrgBankAsset
        {
            Path = "native.s", Bank = 1, CpuAddress = 0xA000, Offset = 0x1000,
        },
    },
};
var compiled = NesCompiler.CompileBanked(assembly, options);
compiled.ResolveAndRelax();
byte[] fixedCode = compiled.FixedProgram.ToBytes();
byte[] audioCode = compiled.Regions.Single(region => region.Placement.Name == "audio").Program.ToBytes();
```

`compiled.PrgAssets` is an `IReadOnlyList<CompiledPrgAsset>`. Each result exposes
its captured `Placement` and either an editable assembly `Program` or raw binary
`Data`; the other is null. Native assembly models participate in the same
`ResolveAndRelax()` link as fixed code and managed regions, so their labels
and references reflect final placement. Resolve again after editing a model
before inspecting its emitted bytes.

`compiled.FixedNativeBlocks` exposes the prepared fixed native assembly blocks
(code and data) in emission order, retaining the exact instances in
`FixedProgram`. It includes inserted selector tracking and relaxed branches,
without including compiler startup, managed methods, gates, or stock interrupt
dispatchers. Inspection fixtures can select these blocks plus the dispatcher
blocks they need instead of reassembling an uninstrumented source or guessing
a label range. The collection covers all fixed native sources; it does not
provide per-file source identity.

This example links the native asset at `$B000` (`$A000 + $1000`) in physical
bank 1. Foreground startup must initialize a stable R7 mapping to that bank
before calling it; placement alone does not select the bank. Managed R6 gates
do not switch R7. Native bodies must remain source-visible to validate their
mapper effects. Use the ordinary package build targets to produce the complete
ROM, including legacy `CHARS` and explicit CHR assets.

### Explicit foreground native RAM contracts

Self-modifying native code cannot be proven from a ROM's assembly body.
`NESNativeRamCode` provides a narrow, explicit **caller guarantee, not a compiler
proof**, for foreground I/O thunks in PRG RAM. It is not inferred from call
addresses, an unchecked-effects fallback, or permission for arbitrary RAM calls.

```xml
<ItemGroup>
  <NESNativeRamCode Include="io-write"
                    Address="0x7420"
                    Size="4"
                    Contract="ForegroundRtsPreservesMapperContext" />
</ItemGroup>
```

The name, exact entry `Address`, positive `Size`, and `Contract` are required.
The entire region must fit in `$6000-$7FFF`; declarations cannot overlap.
Only entry at the exact declared address is authorized, by a normal foreground
call or tail `JMP`. This does not authorize calling arbitrary offsets inside
the region, interrupt callback paths, or banked managed paths.

`ForegroundRtsPreservesMapperContext` requires the code to return through `RTS`
with a balanced hardware stack, leaving the software stack, mapper registers,
full MMC3 selector, and compiler selector shadow unchanged. The consumer is
responsible for initializing the RAM code, every subsequent mutation, and
verifying its behavior through consumer runtime tests. Declaring a region
neither copies code into RAM nor verifies the mutated instructions.

No contract is selected implicitly. `NativeRamCodeContract.None` grants no
permission and is rejected for configured entries. Missing/unknown contracts,
undeclared RAM calls, and callback/banked access remain errors. Names are
case-sensitive and numeric contract values are not accepted.

The equivalent desktop option is:

```csharp
options.NativeRamCode.Add(new NativeRamCode
{
    Name = "io-write",
    Address = 0x7420,
    Size = 4,
    Contract = NativeRamCodeContract.ForegroundRtsPreservesMapperContext,
});
```

Declare only entries whose full behavior meets this contract; it is independent
of the non-nesting interrupt scheduling obligation.

### Shared RAM inspection labels

`compiled.FixedProgram.GetLabels()` exposes the actual shared RAM allocation:

| Label | Meaning |
|---|---|
| `__nesbank_selector` | Foreground MMC3 selector shadow byte |
| `__nesbank_saved_selector` | Saved caller selector byte; together these two context bytes follow static storage |
| `__nesbank_main_locals` | Initial main frame base, after statics, mapper context, and preallocated shared arrays |
| `__nesbank_main_first_array` | Lowest address of a retained main RAM array; absent when there are none |
| `__nesbank_main_array_<ILlocalIndex>` | Actual address of the retained main RAM array for that IL local |
| `__nesbank_main_array_<ILlocalIndex>_size` | Byte size of that array, not a RAM address |
| `__nesbank_static_<fieldName>` | Address of the named shared static field |

Use the array's own label when inspecting memory; do not derive its address
by adding sizes to `__nesbank_main_first_array` or the main frame base.
IL local indices describe the compiled assembly, not source declaration order.

```csharp
var labels = compiled.FixedProgram.GetLabels();
ushort selectorAddress = labels["__nesbank_selector"];
ushort stateAddress = labels["__nesbank_static_state"];
// For a retained array identified as IL local 3 in this compiled assembly:
if (labels.TryGetValue("__nesbank_main_array_3", out ushort arrayAddress))
{
    ushort arraySize = labels["__nesbank_main_array_3_size"];
    // Inspect arraySize bytes starting at arrayAddress in the emulator's RAM.
}
```

### Interrupt and selector contract

The default `NESMmc3ManagedInterruptContract` is `None`: managed banking
permits no user interrupt callbacks. Projects registering native callbacks
must explicitly choose `NonNestingChrCallbacks`. The names are case-sensitive;
numeric enum values and other names are rejected.

`NonNestingChrCallbacks` is a **caller scheduling obligation, not a compiler
proof** of hardware interrupt cadence. NMI must not interrupt IRQ mapper-write
sequences, and callbacks must not reenter themselves. Callbacks must use the
stock dispatchers and their established return ABI, select only CHR registers
0–5, keep PRG mappings unchanged, and never enter banked managed code.
Foreground code and callbacks must obey the same CHR inversion-mode contract.
Statically detectable violations and unresolved mapper effects are diagnostics;
the option does not make arbitrary nested interrupts safe. In particular,
`SEI` cannot mask NMI.

The compiler tracks the full foreground MMC3 selector in allocated shared RAM,
publishing the intended value before each supported foreground selector write.
This preserves pending CHR-select/data pairs even when interrupted between
publication and the hardware store. Foreground native absolute shadow stores
cost four cycles each; gates restore both home R6 and the caller's selector
without changing PRG mode or losing the agreed CHR inversion mode.

The stock NMI/IRQ epilogue restores the selector with
`LDA absolute-shadow; STA $8000` immediately after its existing callback JSR
and before register restoration/RTI. This adds exactly **8 cycles and 6 bytes**
per affected dispatcher, with **no pre-callback instrumentation** and no added
instructions inside callback mapper-write sequences. Validate actual callback
timing and nonoverlap in the consumer's emulator/hardware schedule.

Managed placement preserves the existing header, mirroring, battery flag,
PRG/CHR counts, reset stub, vectors, zero filling, and legacy `CHARS` data.
Failed validation leaves the previous output ROM unchanged. All region metadata
and managed properties participate in the incremental properties stamp.

## CHR layout

`NESChrBanks` remains the iNES count in 8 KiB units. MMC3 physical CHR banks
are 1 KiB, so `NESChrBanks=2` provides physical banks 0-15. `NESChrBank` uses
that zero-based physical bank index.

`.bin` assets are copied directly. `.s` assets contribute their non-empty
`CHARS` segments. Existing `NESAssembly` `CHARS` data still starts at physical
CHR bank 0 for compatibility; explicit placements may use any remaining
non-overlapping range.
