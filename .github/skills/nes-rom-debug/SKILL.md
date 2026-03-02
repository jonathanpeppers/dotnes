---
name: nes-rom-debug
description: >-
  Disassemble and debug NES ROM files (.nes) produced by the dotnes transpiler.
  Use this skill whenever the user wants to disassemble a .nes ROM, inspect 6502 machine code,
  compare two NES ROMs side-by-side, debug transpiler output, investigate byte differences
  between a cc65 reference ROM and a dotnes ROM, or understand what 6502 instructions the
  transpiler emitted. Also use when the user mentions disasm, disassembly, PRG ROM, ROM bytes,
  NES addresses, or wants to look at the hex/binary output of a build. Even if the user just
  says something like "the ROM looks wrong" or "what did the transpiler emit", this skill applies.
---

# NES ROM Debug

Tools and knowledge for disassembling NES ROMs and debugging dotnes transpiler output.

## Available Scripts

### `disasm.py` — 6502 Disassembler

Disassembles the PRG ROM section of an iNES (.nes) file into human-readable 6502 assembly.

```bash
python scripts/disasm.py <file.nes> [start_hex] [end_hex]
```

- **`file.nes`** — Path to the NES ROM file
- **`start_hex`** — (Optional) Start NES address in hex, default `8000`
- **`end_hex`** — (Optional) End NES address in hex, default end of PRG

**Examples:**

> **Note:** `.nes` files are build outputs — run `dotnet build` in the sample directory first.

```bash
# Disassemble the entire PRG ROM
python scripts/disasm.py samples/hello/hello.nes

# Disassemble only the main() region
python scripts/disasm.py samples/hello/hello.nes 8500 8600

# Disassemble the interrupt vectors area
python scripts/disasm.py samples/hello/hello.nes FF00 FFFF
```

**Output format:**
```
$8500: A9 02     LDA #$02
$8502: 20 61 85  JSR $8561
$8505: 85 17     STA $17
$8507: D0 F7     BNE $8500
```

Each line shows: `$ADDR: HEX_BYTES  MNEMONIC OPERAND`

### `compare_rom.py` — Side-by-Side ROM Comparison

Compares two NES ROMs with byte-level and instruction-level analysis. Use this to verify dotnes output against a cc65 reference ROM.

```bash
python scripts/compare_rom.py <reference.nes> <dotnes.nes>
```

**Output includes:**
1. File sizes
2. Total byte differences (header / PRG / CHR breakdown)
3. Contiguous diff groups with NES addresses
4. Side-by-side disassembly of the largest diff region with match/mismatch markers
5. Instruction mismatch count (ignoring absolute addresses, since the two compilers may place subroutines at different offsets)

### `ildump.cs` — IL Opcode Dumper

Dumps the .NET IL opcodes from a compiled DLL — useful for understanding what IL the transpiler will process.

```bash
dotnet run scripts/ildump.cs -- <path-to-dll>
```

## NES ROM Format (iNES)

Understanding the binary layout helps interpret disassembly output:

```
Offset    Size     Content
0x00      16       iNES header (starts with "NES\x1A")
0x10      32768    PRG ROM (mapped to NES $8000-$FFFF)
0x8010    8192     CHR ROM (pattern tables for tiles/sprites)
```

**Key NES addresses:**
- `$8000-$85AD` — neslib runtime and built-in subroutines (palette, PPU, NMI handler, stack ops)
- `$85AE+` — `main()` and user code (exact layout varies per sample)
- `$FFFA` — NMI vector (2 bytes, little-endian)
- `$FFFC` — RESET vector (2 bytes, little-endian — entry point)
- `$FFFE` — IRQ vector (2 bytes, little-endian)

**File offset → NES address:** `nes_addr = 0x8000 + (file_offset - 16)`
**NES address → file offset:** `file_offset = (nes_addr - 0x8000) + 16`

## Common Debugging Workflows

### "The ROM doesn't match the reference"

1. Run `compare_rom.py` to identify where bytes differ
2. Use `disasm.py` on both ROMs targeting the diff region to see the instructions
3. Check if differences are just address relocations (compare_rom normalizes these) or actual logic differences

```bash
python scripts/compare_rom.py reference.nes output.nes
# If diff at $8600-$8620:
python scripts/disasm.py reference.nes 85F0 8630
python scripts/disasm.py output.nes 85F0 8630
```

### "What did the transpiler emit for my code?"

1. Build the sample: `cd samples/hello && dotnet build`
2. Dump the IL to see what C# compiled to: `dotnet run scripts/ildump.cs -- samples/hello/bin/Debug/net10.0/hello.dll`
3. Disassemble the ROM to see the 6502 output: `python scripts/disasm.py samples/hello/bin/Debug/net10.0/hello.nes 8500`

### "Where is main() in the ROM?"

The RESET vector at `$FFFC` points to the startup code, which eventually jumps to main. To find it:

```bash
# Look at the last few bytes for the vectors
python scripts/disasm.py myrom.nes FFF0 FFFF

# The RESET vector value tells you where startup is
# Then disassemble from there to find the JMP to main
```

### "Comparing test output against verified snapshot"

When `TranspilerTests.Write` fails, the test produces a `.received.bin` alongside the `.verified.bin`. Compare them:

```bash
# The test DLLs are in src/dotnes.tests/Data/
# Build the test project to get the .received.bin
python scripts/compare_rom.py path/to/verified.bin path/to/received.bin
```

## 6502 Quick Reference

The most common instructions you'll see in dotnes output:

| Opcode | Mnemonic | Meaning |
|--------|----------|---------|
| `A9` | `LDA #imm` | Load accumulator with immediate value |
| `A5` | `LDA zpg` | Load accumulator from zero page |
| `AD` | `LDA abs` | Load accumulator from absolute address |
| `85` | `STA zpg` | Store accumulator to zero page |
| `8D` | `STA abs` | Store accumulator to absolute address |
| `20` | `JSR abs` | Jump to subroutine |
| `60` | `RTS` | Return from subroutine |
| `4C` | `JMP abs` | Jump to address |
| `D0` | `BNE rel` | Branch if not equal (Z=0) |
| `F0` | `BEQ rel` | Branch if equal (Z=1) |
| `C9` | `CMP #imm` | Compare accumulator with immediate |
| `E6` | `INC zpg` | Increment zero page location |
| `C6` | `DEC zpg` | Decrement zero page location |

**Zero page addresses used by dotnes:**
- `$17` — TEMP
- `$22-$23` — sp (cc65 software stack pointer)
- `$0325+` — Local variables
