# .NES (dotnes) AI Coding Instructions

## Overview
.NES is a transpiler that converts .NET IL code directly into 6502 assembly for NES game development. It allows C# programs to run on the NES console by translating MSIL to working machine code.

## Core Architecture

### Transpilation Pipeline
1. **Input**: .NET assembly (.dll) + Assembly files (*.s) 
2. **Processing**: `Transpiler` reads MSIL using `System.Reflection.Metadata`
3. **Output**: NES ROM (.nes file) via `IL2NESWriter` and `NESWriter`

Key files:
- `src/dotnes.tasks/Utilities/Transpiler.cs` - Main transpilation logic
- `src/dotnes.tasks/Utilities/IL2NESWriter.cs` - Converts IL to 6502 assembly  
- `src/dotnes.tasks/Utilities/NESWriter.cs` - Writes NES ROM format
- `src/dotnes.compiler/Program.cs` - CLI entry point

### Reference Assembly Pattern
`neslib` contains **only reference assemblies** - no actual implementations. Methods like `pal_col()`, `vram_write()` throw `null!` but provide compile-time APIs. The transpiler maps these to actual 6502 subroutines during compilation.

## Project Structure

### MSBuild Integration
- `dotnes.props` - Disables BCL, sets optimization, adds `using static NES.NESLib`
- `dotnes.targets` - Hooks `TranspileToNES` task after Build
- `TranspileToNES` task creates `.nes` files from `.dll` + `*.s` assembly files

### Package Structure
- `dotnes` - Core transpiler and MSBuild tasks
- `dotnes.templates` - `dotnet new nes` project template  
- `dotnes.anese` - Bundled NES emulator for testing
- `neslib` - Reference assembly with NES APIs

## Development Workflows

### Building Projects
```bash
dotnet build              # Builds .dll AND .nes via MSBuild hook
dotnet run               # Runs via emulator (through dotnes.anese)
```

### Testing Samples
```bash
cd samples/hello
dotnet build             # Creates hello.nes
```

### Running Tests
```bash
dotnet test src/dotnes.tests/  # Unit tests with verification snapshots
```

## Programming Model

### Program Structure
NES programs are single `Program.cs` files with:
- `using static NES.NESLib;` (automatic via props)
- Top-level statements only (no methods/classes initially)
- `chr_generic.s` file for sprite/character data

### Example Pattern
```csharp
// Set palette  
pal_col(0, 0x02);
// Write to video memory
vram_adr(NTADR_A(2, 2));
vram_write("Hello!");
// Enable rendering
ppu_on_all();
// Infinite loop required
while (true) ;
```

## Key Constraints

### Supported Features
- Local variables (stored in zero page)
- Byte arrays as data tables
- Basic control flow (while loops, conditionals)
- NESLib API calls

### Not Supported
- Methods/functions (beyond main)
- Objects, classes, structs  
- .NET BCL (disabled via `NoStdLib=true`)
- String manipulation (ASCII only)
- Garbage collection

## File Patterns

### Sample Projects
Look at `samples/` for working examples:
- `hello/` - Basic text output
- `staticsprite/` - Sprite rendering  
- `attributetable/` - Palette manipulation

### Testing
Tests use `Verify` snapshots comparing IL output and generated assembly. When adding features, add test cases to `TranspilerTests.cs`.

## Critical Development Notes

1. **Always build optimized IL** - `Optimize=true` required for proper transpilation
2. **Include chr_generic.s** - Required for NES character data  
3. **Programs must infinite loop** - NES has no "exit", use `while(true);`
4. **Local variables use zero page** - Limited to ~200 bytes total
5. **MSBuild drives everything** - The `Transpile` target handles .dll → .nes conversion

## Debugging
- Enable diagnostic logging: `<NESDiagnosticLogging>true</NESDiagnosticLogging>`
- Check generated assembly with hex editors
- Use ANESE emulator for testing ROMs
- Verify test output with snapshot testing
