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

## Item Groups

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
    <NESDiagnosticLogging>true</NESDiagnosticLogging>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="dotnes" Version="*" />
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
  `NESPrgBanks`, `NESChrBanks`, and `NESBattery`

The stamp file is written to `$(IntermediateOutputPath)dotnes.properties.stamp`
and is only updated when a property value changes, so toggling a property like
`NESBattery` from `false` to `true` will correctly retrigger transpilation on
the next build.
