# MSBuild Properties

.NES exposes several MSBuild properties that you can set in your `.csproj` file to
configure how your NES ROM is built. All properties are optional and have sensible
defaults for simple projects.

## Properties

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

When enabled, dotnes links the transpiled C# program at `$C000` across the
final two physical 8 KiB PRG banks. The second-last bank is mapped at `$C000`
only in PRG mode 0; the last bank is always fixed at `$E000`. A reset stub in
the last bank selects PRG mode 0 before jumping to `$C000`, and the NMI/RESET/IRQ
vectors are written at `$FFFA-$FFFF`. Runtime code must keep MMC3 bank-select
bit 6 clear so it does not unmap the program at `$C000`. Other PRG assets can
be assigned to the switchable `$8000` or `$A000` windows with `NESPrgBank`
items. Banked layout supports at most 32 `NESPrgBanks` and 32 `NESChrBanks`.

```xml
<PropertyGroup>
  <NESMapper>4</NESMapper>
  <NESPrgBanks>4</NESPrgBanks>
  <NESChrBanks>2</NESChrBanks>
  <NESMmc3BankedLayout>true</NESMmc3BankedLayout>
</PropertyGroup>
```

See `samples/bankswitch` and [MMC3 bank layout](mmc3-bank-layout.md).

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
be selected at runtime before code accesses it; dotnes does not insert mapper
writes automatically.

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
- A **properties stamp file** — tracks changes to `NESMirroring`, `NESMapper`,
  `NESPrgBanks`, `NESChrBanks`, `NESBattery`, `NESMmc3BankedLayout`, and bank
  item metadata

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
