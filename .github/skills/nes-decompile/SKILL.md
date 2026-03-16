---
name: nes-decompile
description: >-
  Decompile NES ROM files (.nes) into C# projects that can be rebuilt with dotnes.
  Use this skill whenever the user wants to decompile a ROM, reverse-engineer a .nes file,
  convert a ROM to C#, extract code from a NES ROM, create a project from an existing ROM,
  or do a round-trip test (transpile → decompile → retranspile). Also use when the user says
  things like "decompile this ROM", "turn this .nes into C#", "extract the code", "what does
  this ROM do", "reverse engineer", or "round-trip". This is the high-level decompilation tool
  — for low-level 6502 disassembly, use nes-rom-debug instead.
---

# NES ROM Decompiler

Decompile `.nes` ROM files into complete C# projects that can be rebuilt through dotnes.

## Decompiler Tool

The decompiler lives at `src/dotnes.decompiler/` and is a .NET CLI tool.

### Running the Decompiler

```bash
dotnet run --project src/dotnes.decompiler -- <input.nes> [output-directory]
```

- **`<input.nes>`** — Path to the NES ROM file (required)
- **`[output-directory]`** — Where to write the C# project (optional; defaults to the ROM filename without extension)

**Examples:**

```bash
# Decompile a sample ROM (build it first if needed)
cd samples/hello && dotnet build && cd ../..
dotnet run --project src/dotnes.decompiler -- samples/hello/bin/Debug/net10.0/hello.nes hello-decompiled

# Decompile any .nes ROM
dotnet run --project src/dotnes.decompiler -- path/to/game.nes game-decompiled

# Decompile with default output directory (uses ROM filename)
dotnet run --project src/dotnes.decompiler -- myrom.nes
```

### Output Files

The decompiler generates a complete, buildable C# project:

| File | Description |
|------|-------------|
| `Program.cs` | Decompiled C# source with recovered NESLib API calls |
| `{name}.csproj` | MSBuild project with NES configuration from ROM metadata |
| `chr_generic.s` | CHR ROM data as ca65 assembly (only if ROM has CHR banks) |

### What Gets Recovered

The decompiler recognizes these NESLib API calls:

| Category | Functions |
|----------|-----------|
| **Palette** | `pal_col(palette, color)` |
| **VRAM** | `vram_adr(address)`, `vram_write("string")` with NTADR_A reconstruction |
| **PPU control** | `ppu_on_all()`, `ppu_on_bg()`, `ppu_on_spr()`, `ppu_off()` |
| **Sprites/OAM** | `oam_clear()`, `oam_size()`, `oam_hide_rest()` |
| **CHR banking** | `bank_spr()`, `bank_bg()` |
| **Input** | `pad_poll()` |
| **Timing** | `delay(frames)`, `waitvsync()` |
| **Random** | `rand8()`, `set_rand()` |

String literals in `vram_write()` are recovered from ROM data when the bytes are printable ASCII.
VRAM addresses are decompiled back to `NTADR_A(x, y)` macro form when they match the pattern `0x2000 + y*32 + x`.

### What Is NOT Recovered

- Original variable names, control flow structures, or algorithm intent
- User-defined functions (inlined into main)
- Local variables and complex expressions
- Non-NESLib subroutine calls (appear as comments)
- Classes, objects, or BCL usage (NES doesn't support these)

## Common Workflows

### Decompile a dotnes sample ROM

Build the sample, then decompile its ROM output:

```bash
# Build the sample
cd samples/hello && dotnet build && cd ../..

# Decompile the ROM
dotnet run --project src/dotnes.decompiler -- samples/hello/bin/Debug/net10.0/hello.nes hello-decompiled

# Inspect the output
cat hello-decompiled/Program.cs
```

### Round-trip test (transpile → decompile → retranspile)

Verify the decompiler produces code that builds back to an equivalent ROM:

```bash
# 1. Build original sample
cd samples/hello && dotnet build && cd ../..

# 2. Decompile the ROM
dotnet run --project src/dotnes.decompiler -- samples/hello/bin/Debug/net10.0/hello.nes hello-roundtrip

# 3. Build the decompiled project
cd hello-roundtrip && dotnet build && cd ..

# 4. Compare the two ROMs
python scripts/compare_rom.py samples/hello/bin/Debug/net10.0/hello.nes hello-roundtrip/bin/Debug/net10.0/hello-roundtrip.nes
```

If the round-trip produces an identical ROM, the decompiler recovered all observable behavior.

### Decompile an external .nes ROM

Any iNES-format ROM can be parsed, but the decompiler only recognizes dotnes/neslib patterns:

```bash
dotnet run --project src/dotnes.decompiler -- external-game.nes game-output
cat game-output/Program.cs
```

For ROMs not built with dotnes, the output will contain fewer recognized API calls and more comments for unrecognized subroutines.

### Inspect ROM metadata without full decompilation

Use the decompiler's console output to quickly check ROM properties:

```bash
dotnet run --project src/dotnes.decompiler -- myrom.nes 2>&1 | head -6
```

This shows PRG/CHR bank counts, mapper number, mirroring mode, and interrupt vectors.

## How the Decompiler Works

The decompiler uses a three-phase algorithm:

### Phase 1: Build Symbol Table
- Assembles the known neslib built-in subroutines to determine their addresses
- Finds `main()` by scanning the `initlib` block for the first JSR to an address past the built-ins
- Pattern-matches final built-ins (pusha, pushax, popa, vram_write, etc.) using byte signatures

### Phase 2: Decompile Main
- Collects all 6502 instructions from main until the infinite loop (`JMP` to self)
- Uses look-ahead pattern matching to recognize multi-instruction sequences:
  - `LDA #imm / JSR pusha` → 8-bit argument push
  - `LDA #lo / LDX #hi / JSR pushax` → 16-bit pointer push
  - `LDX #hi / LDA #lo / JSR <sub>` → call with 16-bit register argument
  - `LDA #imm / JSR <sub>` → call with immediate argument

### Phase 3: Generate C#
- Converts the recognized patterns into NESLib API calls with recovered arguments
- Reconstructs string literals and NTADR_A macro addresses
- Wraps everything in the standard dotnes program structure

## MSBuild Properties in Generated .csproj

The decompiler reads ROM metadata and sets appropriate MSBuild properties:

| Property | Condition | Example |
|----------|-----------|---------|
| `NESMapper` | Mapper ≠ 0 | `<NESMapper>1</NESMapper>` |
| `NESPrgBanks` | PRG banks ≠ 2 | `<NESPrgBanks>4</NESPrgBanks>` |
| `NESChrBanks` | CHR banks ≠ 1 | `<NESChrBanks>2</NESChrBanks>` |
| `NESVerticalMirroring` | Mirroring is vertical | `<NESVerticalMirroring>true</NESVerticalMirroring>` |

## Related Skills

- **nes-rom-debug** — Low-level 6502 disassembly and byte-level ROM inspection
- **nes-emu-debug** — Run ROMs in Mesen2 emulator to verify runtime behavior
