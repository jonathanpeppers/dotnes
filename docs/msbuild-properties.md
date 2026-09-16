# MSBuild Properties

.NES exposes several MSBuild properties that you can set in your `.csproj` file to
configure how your NES ROM is built. All properties are optional and have sensible
defaults for simple projects.

## Properties

### `IsTestProject`

The standard MSBuild test-project property selects desktop test integration
instead of ROM compilation.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | Unset; standard test SDKs set it to `true` |

Ordinary `Microsoft.NET.Test.Sdk` projects are detected automatically. If a test
framework does not set the property in package props, declare it in
`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>
```

Detection occurs after all NuGet package props, before the .NET SDK derives
its compiler/output settings and before the project body. Test projects retain their normal framework
references, compiler settings, test discovery, and runtime output. They receive
the existing compiler APIs and their runtime assets, without NES-only analyzers
or ROM transpilation. This also applies during design-time builds. Standard
test-SDK projects need no additional configuration. Without such a test SDK,
setting `IsTestProject` only in the project body is too late. Changing test
classification in either direction after package props produces a clear build
error. Keeping ROM defaults before the project body preserves
existing consumer overrides such as `Optimize`, `DebugSymbols`, and `DebugType`.

This is not inferred from names or `OutputType=Library`, and is not a general
non-test tooling mode. See the [desktop test project example](compilation-api.md#reference-from-a-desktop-test-project).
The ROM properties and targets below apply to non-test projects.

### `NESDiagnosticLogging`

Enable verbose diagnostic output during transpilation.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | `false` |

When set to `true`, the transpiler logs detailed information about the IL it
reads and the 6502 instructions it emits. Useful for debugging transpilation
issues.

```xml
<PropertyGroup>
  <NESDiagnosticLogging>true</NESDiagnosticLogging>
</PropertyGroup>
```

### `NESOptimizeByteHelpers`

Use private RAM parameter homes for proven non-reentrant byte helpers instead of
pushing their incoming argument onto the cc65 software stack.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | `false` |

```xml
<PropertyGroup>
  <NESOptimizeByteHelpers>true</NESOptimizeByteHelpers>
</PropertyGroup>
```

The initial optimization admits private static helpers and static local functions
reachable from main with
exactly one by-value `byte` parameter, a `byte` or `void` return, at most four byte
locals, and at most 64 IL instructions. Bodies may contain scalar byte operations,
branches and calls to other eligible acyclic helpers. Each helper gets its own
one-byte home, above the final static/local/synthetic-local high-water mark,
populated on **every** call. Argument
evaluation, local initialization and the A-register call/return ABI are unchanged.

Other signatures, mutable/address-taken parameters, memory access, exception
regions, user attributes, built-in/unknown calls and recursive call chains are not
optimized. Methods reachable from non-private entry points retain standard storage.
Any declared extern, function pointer, indirect call, linked assembly code or PRG
bank payload disables this optimization for the compilation, since external entry
points and assembly/callback effects are unproven. CHR graphics assets do not
trigger the native-code barrier.
Existing recursion and unsupported-IL diagnostics still apply. This does not
replace general multi-argument calling conventions or all consumer-side rewrites.

Helpers whose results feed a direct boolean branch also retain standard storage:
the existing branch lowering relies on callee flags rather than testing the return
value. Removing stack cleanup must not conceal that separate correctness issue.

The default preserves existing ROM bytes. Enabling it trades one RAM byte per
eligible method for fewer software-stack accesses; diagnostic logging reports the
allocated homes.

Before changing any instructions, the compiler bounds the software stack from
the emitted control-flow graph, including pending arguments and nested calls.
Homes must fit below its lowest reachable address (the production stack starts
at `$0800`). Inconsistent stack depths at joins/loops, recursion, unknown calls
or stack-pointer effects, indexed/dynamic RAM access, accesses to the internal-RAM
mirrors at `$0800-$1FFF`, and insufficient capacity
leave the original IR unchanged. This intentionally excludes some otherwise
eligible helpers in programs whose complete storage safety is not yet proven.

### `NESMirroring`

Controls the nametable mirroring mode stored in the iNES header (Flags6, bit 0).

| | |
|---|---|
| **Type** | `string` |
| **Default** | `Horizontal` |

- `Horizontal` — horizontal mirroring, used for vertical scrolling games (the default).
- `Vertical` — vertical mirroring, used for horizontal scrolling games.

```xml
<PropertyGroup>
  <NESMirroring>Vertical</NESMirroring>
</PropertyGroup>
```

See the `samples/statusbar` and `samples/horizscroll` projects for examples.

### `NESMapper`

Specifies the iNES mapper number for the cartridge hardware.

| | |
|---|---|
| **Type** | `int` |
| **Default** | `0` (NROM) |

Common mapper values:

| Mapper | Name | Description |
|--------|------|-------------|
| 0 | NROM | No bank switching (32 KB PRG, 8 KB CHR) |
| 1 | MMC1 | 16 KB switchable PRG banks, 4 KB switchable CHR banks, software mirroring control |
| 2 | UxROM | 16 KB switchable PRG banks, fixed CHR |
| 3 | CNROM | Fixed PRG, switchable 8 KB CHR banks |
| 4 | MMC3 | Switchable PRG and CHR banks with IRQ counter |

The mapper number is encoded in bits 4–7 of iNES Flags6 and Flags7.

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
</PropertyGroup>
```

See the `samples/bankswitch` project for an MMC3 example.

### `NESPrgBanks`

Number of 16 KB PRG ROM banks.

| | |
|---|---|
| **Type** | `int` |
| **Default** | `2` (32 KB) |

This value is written to byte 4 of the iNES header. Increase it for larger games
that use bank switching.

```xml
<PropertyGroup>
  <NESPrgBanks>4</NESPrgBanks>
</PropertyGroup>
```

### `NESChrBanks`

Number of 8 KB CHR ROM banks.

| | |
|---|---|
| **Type** | `int` |
| **Default** | `1` (8 KB) |

This value is written to byte 5 of the iNES header. Set to `0` if your game uses
CHR RAM (pattern data uploaded at runtime instead of stored on the cartridge).

```xml
<PropertyGroup>
  <NESChrBanks>8</NESChrBanks>
</PropertyGroup>
```

### `NESMmc3BankedLayout`

Enable deterministic mapper-4 physical bank placement. This mode requires
`NESMapper=4` and at least two 16 KiB `NESPrgBanks`.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | `false` |

When enabled, dotnes links the fixed C# program at `$C000` across the
final two physical 8 KiB PRG banks. The second-last bank is mapped at `$C000`
only in PRG mode 0; the last bank is always fixed at `$E000`. A reset stub in
the last bank selects PRG mode 0 before jumping to `$C000`, and the NMI/RESET/IRQ
vectors are written at `$FFFA-$FFFF`. Runtime code must keep MMC3 bank-select
bit 6 clear so it does not unmap the program at `$C000`. Other PRG assets can
be assigned to the switchable `$8000` or `$A000` windows with `NESPrgBank`
items. Banked layout supports at most 32 `NESPrgBanks` and 32 `NESChrBanks`.
Named `NESManagedCodeBank` items additionally move annotated managed methods
to R6 at `$8000` and generate fixed bank-switch gates. Without these items and
annotations, banked layout alone does not generate runtime mapper writes.

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
  <NESPrgBanks>4</NESPrgBanks>
  <NESChrBanks>2</NESChrBanks>
  <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
</PropertyGroup>
```

See `samples/bankswitch` and [MMC3 bank layout](mmc3-bank-layout.md).

### `NESMmc3ManagedHomeBank`

Physical 8 KiB MMC3 R6 bank mapped at startup and restored after every public
managed bank-switch gate.

| | |
|---|---|
| **Type** | `int` (decimal, `0x`-prefixed hex, or `$`-prefixed hex) |
| **Default** | *(empty; unconfigured)* |

Required when `NESManagedCodeBank` regions are present. Requires mapper 4 and
`NESMmc3BankedLayout=true`; the bank must be a valid switchable physical PRG
bank, not one of the final two fixed banks. This is not a 16 KiB iNES bank
number and does not authorize arbitrary foreground R6 changes.

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
  <NESPrgBanks>6</NESPrgBanks>
  <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
  <NESMmc3ManagedHomeBank>0</NESMmc3ManagedHomeBank>
</PropertyGroup>
```

### `NESMmc3ManagedInterruptContract`

Declare the native interrupt callback scheduling contract for managed banking.

| | |
|---|---|
| **Type** | `string` (`None` or `NonNestingChrCallbacks`, case-sensitive) |
| **Default** | *(empty; equivalent to `None`)* |

`None` allows no user interrupt callbacks with managed banking.
`NonNestingChrCallbacks` explicitly permits native CHR-only callbacks returning
through the stock NMI/IRQ dispatchers. Callbacks must not nest, change PRG
mappings, or enter banked managed code. NMI must not interrupt IRQ mapper-write
sequences. The contract is the caller's scheduling obligation, **not proof**
of interrupt timing or nonoverlap; unsupported effects remain diagnostics.
Numeric enum values and unknown names are rejected.

```xml
<PropertyGroup>
  <NESMmc3ManagedInterruptContract>NonNestingChrCallbacks</NESMmc3ManagedInterruptContract>
</PropertyGroup>
```

The compiler preserves the full foreground selector with a shared-RAM shadow.
Each affected stock dispatcher restores it after the callback JSR, adding
8 cycles and 6 bytes to its epilogue, without pre-callback instrumentation
or changes inside callback mapper-write sequences. See the
[interrupt and selector contract](mmc3-bank-layout.md#interrupt-and-selector-contract)
for the required CHR mode and scheduling constraints.

### `NESBattery`

Indicates that the cartridge has battery-backed SRAM at $6000-$7FFF.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | `false` |

When set to `true`, bit 1 of iNES Flags6 is set, telling emulators to persist
the 8 KB SRAM region across power cycles. Use `peek()` and `poke()` with
addresses in the $6000-$7FFF range (constants `SRAM_START` and `SRAM_END`) to
read and write save data.

```xml
<PropertyGroup>
  <NESBattery>true</NESBattery>
</PropertyGroup>
```

### `MesenTestRunner`

The `dotnes.mesen` 2.2.1 package downloads
[Mesen Community Edition (MesenCE)](https://github.com/nesdev-org/MesenCE/releases/tag/2.2.1)
from `nesdev-org/MesenCE`, replacing the archived `SourMesen/Mesen2` release.
Downloads are SHA256-pinned for Windows, Linux x64/ARM64, and macOS Intel/Apple
Silicon. The integration remains MIT-licensed; the separate emulator process is
GPLv3-licensed, with its license downloaded alongside the executable. Emulator
binaries are not included in the NuGet package.

First-run settings use the platform's Documents folder on Windows or application
data folder on Linux/macOS, under `MesenCE`. Existing `Mesen2/settings.json` is
reused when no MesenCE settings exist, matching the emulator's legacy fallback.
Existing settings are never overwritten.

Run Mesen in headless test-runner mode (`--testrunner --doNotSaveSettings`).
Useful for CI smoke tests.

| | |
|---|---|
| **Type** | `bool` |
| **Default** | `false` |

```xml
<PropertyGroup>
  <MesenTestRunner>true</MesenTestRunner>
</PropertyGroup>
```

### `MesenTimeout`

Auto-exit Mesen after this many seconds. Only meaningful in test-runner mode.

| | |
|---|---|
| **Type** | `int` (seconds) |
| **Default** | *(empty — uses Mesen default of 100)* |

```xml
<PropertyGroup>
  <MesenTimeout>10</MesenTimeout>
</PropertyGroup>
```

### `MesenLuaScript`

Path to a Lua script to load when running Mesen.

| | |
|---|---|
| **Type** | `string` (file path) |
| **Default** | *(empty)* |

```xml
<PropertyGroup>
  <MesenLuaScript>scripts/smoke-test.lua</MesenLuaScript>
</PropertyGroup>
```

Example using `dotnet run` on the command line:

```bash
dotnet run -p:MesenTestRunner=true -p:MesenTimeout=10 -p:MesenLuaScript=smoke-test.lua
```

## Item Groups

### `NESPrgBank`

Place a `.bin` or ca65-compatible `.s` asset in one physical 8 KiB MMC3 PRG
bank. `Bank` is the zero-based physical bank number, `CpuAddress` is the CPU
window used to link the asset (`0x8000` or `0xA000`), and `Offset` is an
optional byte offset within the bank.

```xml
<ItemGroup>
  <NESPrgBank Include="level1.s"
              Bank="0"
              CpuAddress="0x8000"
              Offset="0" />
</ItemGroup>
```

The final two physical PRG banks are reserved for the fixed transpiled program.
Assembly assets support the existing label relocations for absolute
instructions, low/high-byte immediates, and `.word`/`.addr` data. A bank must
be selected at runtime before code accesses it; this asset item does not insert
mapper writes automatically.

### `NESManagedCodeBank`

Reserve a named region for methods annotated with `NES.NESCodeBankAttribute`.
Requires `NESMapper=4`, `NESMmc3BankedLayout=true`, and an explicit
`NESMmc3ManagedHomeBank`.

| Metadata | Type | Default | Description |
|---|---|---|---|
| `Include` | `string` | Required | Nonempty, case-sensitive logical region name, not a path |
| `Bank` | `int` | Required | Zero-based physical 8 KiB PRG bank index |
| `CpuAddress` | 16-bit integer | Required | CPU window base; only `0x8000` (R6) is supported |
| `Offset` | `int` | `0` | Byte offset within the physical bank |
| `Size` | `int` | Required | Positive size of the entire reserved region in bytes |

Numeric metadata accepts decimal, `0x`-prefixed hex, and `$`-prefixed hex.

```xml
<ItemGroup>
  <NESManagedCodeBank Include="audio"
                      Bank="2"
                      CpuAddress="0x8000"
                      Offset="0"
                      Size="0x1000" />
</ItemGroup>
```

Annotate a static class or static method/local function with
`[NESCodeBank("audio")]`. A method annotation overrides its class's region.
Static fields remain in shared RAM; use explicit initialization methods rather
than unsupported static constructors. Cross-region calls, recursion, and bank
reentry are rejected. R7 managed banking is not supported.

The final two physical PRG banks remain reserved for fixed code and gates.
The full `Size`, not just emitted bytes, participates in overlap checks.
`NESPrgBank` assets may occupy compatible, non-overlapping space in the same
physical bank. Legacy `NESAssembly` `CHARS` and explicit CHR assets are preserved.
This logical item is not a file input or assembly asset; its name and all
metadata are tracked in the properties stamp. See
[managed code regions](mmc3-bank-layout.md#managed-code-regions) for the ABI,
placement, and interrupt restrictions.

### `NESNativeRamCode`

Declare one foreground native PRG-RAM entry under an explicit caller behavior
contract. This is **not compiler proof** of self-modifying code, an automatic
inference, or a general unchecked-effects fallback.

| Metadata | Type | Default | Description |
|---|---|---|---|
| `Include` | `string` | Required | Nonempty logical declaration name, not a path |
| `Address` | 16-bit integer | Required | Exact callable entry address in PRG RAM `$6000-$7FFF` |
| `Size` | `int` | Required | Positive byte size; the whole region must fit in PRG RAM and not overlap another declaration |
| `Contract` | `string` | Required; no implicit contract | Exact case-sensitive name `ForegroundRtsPreservesMapperContext` |

`Address` and `Size` accept decimal, `0x`-prefixed hex, or `$`-prefixed hex.

```xml
<ItemGroup>
  <NESNativeRamCode Include="io-write"
                    Address="0x7420"
                    Size="4"
                    Contract="ForegroundRtsPreservesMapperContext" />
</ItemGroup>
```

The contract permits only foreground entry at the exact declared address,
by a normal call or tail `JMP`, returning through `RTS` with balanced hardware
stack. The software stack, mapper registers, full selector, and compiler
selector shadow must remain unchanged. Callback and banked-code paths are
never permitted. The consumer owns initialization, every mutation, and runtime
verification of those guarantees. The declaration does not initialize RAM.

Missing/unknown contracts, `None`, numeric contract values, and undeclared
RAM calls remain errors. All names and metadata participate in the properties
stamp, but these logical declarations are not file inputs. See
[explicit foreground native RAM contracts](mmc3-bank-layout.md#explicit-foreground-native-ram-contracts).

### `NESChrBank`

Place a `.bin` or `.s` asset in one physical 1 KiB MMC3 CHR bank. `Bank` is
the zero-based physical bank number and `Offset` is an optional byte offset
within that bank. Assembly assets must contain a `CHARS` segment.

```xml
<ItemGroup>
  <NESChrBank Include="background-2.s" Bank="8" Offset="0" />
</ItemGroup>
```

Legacy `CHARS` segments from `NESAssembly` continue to populate the start of
CHR ROM. Explicit `NESChrBank` items cannot overlap those bytes.

### `NESAssembly`

Include pattern for 6502 assembly (`.s`) files that provide CHR ROM data or
external subroutines.

| | |
|---|---|
| **Default** | `*.s` (all `.s` files in the project directory) |

By default, every `.s` file in your project directory is included. The most
common use is a `chr_*.s` file that contains your game's tile/sprite graphics.

```xml
<!-- Include only a specific assembly file -->
<ItemGroup>
  <NESAssembly Include="my_chr.s" />
</ItemGroup>
```

## Full Example

A project using bank switching with MMC3, vertical mirroring, and diagnostic
logging:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <NESMapper>4</NESMapper>
    <NESPrgBanks>4</NESPrgBanks>
    <NESChrBanks>8</NESChrBanks>
    <NESMirroring>Vertical</NESMirroring>
    <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
    <NESDiagnosticLogging>true</NESDiagnosticLogging>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="dotnes" Version="*" />
    <NESPrgBank Include="level1.s" Bank="0" CpuAddress="0x8000" />
    <NESChrBank Include="level1-tiles.bin" Bank="8" />
  </ItemGroup>
</Project>
```

## Output

The transpiler produces a `.nes` ROM file at:

```
$(OutputPath)$(TargetName).nes
```

For example, building a project called `hello` in Debug configuration produces
`bin/Debug/net10.0/hello.nes`.

## Incremental Builds

The `Transpile` target uses MSBuild incremental build support (`Inputs`/`Outputs`)
to avoid re-transpiling when nothing has changed. The inputs include:

- `$(TargetPath)` — the compiled `.dll`
- `@(NESAssembly)` — the `.s` assembly files
- `@(NESPrgBank)` and `@(NESChrBank)` — physical bank asset files
- A **properties stamp file** — tracks changes to `NESMirroring`, `NESMapper`,
  `NESPrgBanks`, `NESChrBanks`, `NESBattery`, `NESMmc3BankedLayout`,
  `NESOptimizeByteHelpers`, `NESMmc3ManagedHomeBank`,
  `NESMmc3ManagedInterruptContract`, physical asset metadata, and every
  `NESManagedCodeBank` name, `Bank`, `CpuAddress`, `Offset`, and `Size`
  and every `NESNativeRamCode` name, `Address`, `Size`, and `Contract`

`NESManagedCodeBank` and `NESNativeRamCode` items are logical names, not file `Inputs`. Changing only
a reservation or contract therefore retriggers transpilation without looking
for a file named after the region. Desktop test projects remain exempt from
ROM targets and this stamp.

A `_WriteNESPropertiesStamp` target automatically runs before each transpilation
and writes the current property values to
`$(IntermediateOutputPath)dotnes.properties.stamp`. The file is only rewritten
when a value actually changes (`WriteOnlyWhenDifferent`), so toggling a property
like `NESBattery` from `false` to `true` will correctly retrigger transpilation
on the next build without causing unnecessary rebuilds.

### `TranspileDependsOn`

A semicolon-separated list of targets that the `Transpile` target depends on.
By default this includes `_WriteNESPropertiesStamp` (the stamp file target
described above). You can append your own targets to run custom logic before
transpilation:

```xml
<PropertyGroup>
  <TranspileDependsOn>$(TranspileDependsOn);MyCustomPreTranspileTarget</TranspileDependsOn>
</PropertyGroup>
<Target Name="MyCustomPreTranspileTarget">
  <!-- Custom logic that runs before transpilation -->
</Target>
```
