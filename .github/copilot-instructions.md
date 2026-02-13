# .NES (dotnes) AI Coding Instructions

## Overview
.NES transpiles .NET IL code into 6502 assembly, producing NES ROMs. C# compiles to MSIL, then `Transpiler` reads it via `System.Reflection.Metadata` and emits machine code for the NES.

## Architecture

### Transpilation Pipeline
```
Program.cs → dotnet build → .dll (MSIL) → Transpiler → .nes ROM
                                ↑                ↓
                           chr_generic.s    IL2NESWriter → NESWriter
```

**Key files:**
- [src/dotnes.tasks/Utilities/Transpiler.cs](src/dotnes.tasks/Utilities/Transpiler.cs) - Single-pass MSIL reader and ROM assembler
- [src/dotnes.tasks/Utilities/IL2NESWriter.cs](src/dotnes.tasks/Utilities/IL2NESWriter.cs) - IL→6502 opcode mapping
- [src/dotnes.tasks/Utilities/NESWriter.cs](src/dotnes.tasks/Utilities/NESWriter.cs) - ROM binary format
- [src/dotnes.tasks/ObjectModel/BuiltInSubroutines.cs](src/dotnes.tasks/ObjectModel/BuiltInSubroutines.cs) - 6502 assembly for all built-in subroutines
- [src/dotnes.tasks/ObjectModel/Program6502.cs](src/dotnes.tasks/ObjectModel/Program6502.cs) - ROM code block ordering and address resolution
- [src/neslib/NESLib.cs](src/neslib/NESLib.cs) - Reference-only API (all methods `throw null!`)

### Reference Assembly Pattern
`neslib` has **no implementations**—methods provide compile-time API only. The transpiler looks up method names to emit corresponding 6502 subroutine calls. Adding new NES APIs requires:
1. Add method stub in `NESLib.cs` with `throw null!`
2. Implement 6502 equivalent in `BuiltInSubroutines.cs`

## Build & Test

```bash
dotnet build                    # Build entire solution
dotnet test                     # Run tests (ALWAYS rebuilds, NEVER use --no-build)
cd samples/hello && dotnet run  # Build + run in emulator
```

**⚠️ IMPORTANT:** Always run `dotnet test` without `--no-build`. The test project depends on build outputs that must be fresh.

**Diagnostic logging:** Add `<NESDiagnosticLogging>true</NESDiagnosticLogging>` to project.

### Diagnostic Scripts

- `dotnet run scripts/ildump.cs -- <path-to-dll>` — Dumps IL opcodes from a .NET DLL (useful for understanding what IL the transpiler will process)
- `python scripts/compare_rom.py <cc65.nes> <dotnes.nes>` — Side-by-side ROM comparison with 6502 disassembly (useful for debugging transpiler output vs cc65 reference)

## MSBuild Integration
- [bin/Debug/dotnes.props](bin/Debug/dotnes.props) - Disables BCL (`NoStdLib=true`), forces `Optimize=true`
- [bin/Debug/dotnes.targets](bin/Debug/dotnes.targets) - Runs `TranspileToNES` task after Build

The Transpile target automatically creates `.nes` from `.dll` + `*.s` files.

## Testing Patterns
Tests in [src/dotnes.tests/](src/dotnes.tests/) use **Verify snapshots**:
- Test data DLLs live in `Data/` folder (pre-compiled debug/release)
- `TranspilerTests.Write` verifies entire ROM output byte-for-byte
- `TranspilerTests.ReadStaticVoidMain` verifies IL parsing

**⚠️ CRITICAL: The `.verified.bin` files are the source of truth for existing samples. Any code change that causes `TranspilerTests.Write` to produce different bytes for an unchanged sample is WRONG — fix the code, not the verified file. When adding or modifying a sample (e.g., changing `Program.cs`), rebuild its test DLLs and update the verified.bin to match.**

**Adding new test cases:** Compile sample code, copy `.dll` to `Data/`, add `[InlineData("name", true/false)]`.

**Per-sample CHR ROM:** Tests look for `chr_{name}.s` in the Data folder first, falling back to `chr_generic.s`. The music sample uses an empty CHR (no graphics).

## NES Program Constraints

**Supported:** Top-level statements, local variables (zero page `$0324+`), byte arrays as ROM tables, ushort arrays as note tables, while loops, NESLib API calls, NES APU music playback

**Not supported:** Methods, classes, objects, BCL, string manipulation, GC

**Required:** Programs MUST end with `while (true) ;` (NES has no exit)

## Adding IL Opcode Support
1. Add case in `IL2NESWriter.Write(ILInstruction)` switch
2. Emit via `Write(NESInstruction.*, value)`
3. Test with new sample in `Data/`

## 6502 Assembly Basics

Common patterns in [IL2NESWriter](src/dotnes.tasks/Utilities/IL2NESWriter.cs):
```csharp
Emit(Opcode.LDA, AddressMode.Immediate, 0x02);     // A9 02 - Load immediate
EmitWithLabel(Opcode.JSR, AddressMode.Absolute, "pal_col"); // 20 xx xx - Jump subroutine
Emit(Opcode.STA, AddressMode.ZeroPage, TEMP);       // 85 17 - Store zero page
Emit(Opcode.BNE, AddressMode.Relative, offset);     // D0 xx - Branch if not equal
EmitWithLabel(Opcode.JMP, AddressMode.Absolute, "main");    // 4C xx xx - Jump absolute
```

**6502 registers:** A (accumulator), X/Y (index). Zero page ($00-$FF) is fast memory; stack at $0100-$01FF.

## Music Architecture

Music subroutines (`play_music`, `start_music`) are emitted BEFORE `main()` to match cc65's ROM layout. The transpiler handles these IL patterns:

- `set_music_pulse_table(ushort[])` / `set_music_triangle_table(ushort[])` — transpiler-only directives that store note frequency tables as interleaved lo/hi byte pairs
- `apu_init()` — emits `JSR apu_init` (built-in subroutine that initializes APU registers)
- `start_music(byte[])` — loads music data address into A/X, then `JSR start_music`
- `play_music()` — emits `JSR play_music` (called every NMI frame)

See [docs/music-sample.md](docs/music-sample.md) for ROM layout details and cc65 comparison.

**Resources:** [6502 Instruction Set](https://www.masswerk.at/6502/6502_instruction_set.html) | [NES Dev Wiki](https://wiki.nesdev.org/w/index.php/INES) | [8bitworkshop](https://8bitworkshop.com)

## Code Review Guidelines

**⚠️ DO NOT suggest these changes in code reviews:**

1. **Do not use .Where() for filtering when the loop body needs TryResolve out parameters**
   - BAD: `foreach (var kvp in dict.Where(kvp => TryResolve(kvp.Value, out _))) { TryResolve(kvp.Value, out var x); }`
   - GOOD: `foreach (var kvp in dict) { if (TryResolve(kvp.Value, out var x)) { } }`
   - Reason: The Where() clause calls TryResolve twice (slower) and doesn't capture the out parameter

2. **Do not rename parameters to avoid shadowing field names unless it causes actual bugs**
   - Example: Parameter `local` in `WriteStloc(Local local)` is fine even though there's a field `readonly ushort local = 0x325`
   - Reason: The types are different (Local vs ushort) and the context makes usage clear

3. **Do not change conditional logic that appears redundant without understanding the semantic relationship**
   - Example: `if (needsDecsp4 && usedMethods.Contains("pad_poll"))` correctly adds PadTrigger/PadState blocks
   - Reason: When decsp4 is needed, pad_trigger and pad_state are internal implementation dependencies of the pad_poll feature, not separate user-facing methods. They should be added together when pad_poll is used with needsDecsp4, not checked individually.