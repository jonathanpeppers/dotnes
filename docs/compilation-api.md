# In-memory compilation

`dotnes.NesCompiler.Compile` is the supported entry point for tools that need a
`dotnes.ObjectModel.Program6502` from an already compiled .NET assembly. It uses
the same transpiler and target placement as the MSBuild/CLI ROM pipeline; no
reflection over the internal `Transpiler` is necessary.

The API lives alongside `Program6502` in `dotnes.tasks.dll` and is also included
in the `dotnes.compiler` tool's `dotnes.dll`. Reference one compiler assembly,
not both: they contain the same types. Keep its accompanying dependencies,
including `neslib`, available to the host application.

## Compile a program

Given a supported NES program compiled to a PE assembly (for example, C# ending
with `while (true) ;`):

```csharp
using dotnes;
using dotnes.ObjectModel;

using var assembly = File.OpenRead("game.dll");
Program6502 program = NesCompiler.Compile(assembly);
byte[] code = program.ToBytes();
```

`Compile` assigns block addresses but deliberately leaves final binding and
branch relaxation until `ToBytes()` (or `WriteTo(stream)`). This allows external
symbols to be supplied before byte emission. The result owns no input resources
and remains usable after all inputs have been disposed.

These are **program bytes, not an iNES ROM**. CHR input is not required. The
returned model includes the stock runtime and transpiled/native code, but not
the iNES header, bank padding, graphics image, MMC3 reset stub, or interrupt
vector table. Use the existing MSBuild/CLI ROM build for a runnable `.nes`.

### Target options

```csharp
var options = new CompilationOptions
{
    Mapper = 4,
    Mmc3BankedLayout = true,
};
Program6502 program = NesCompiler.Compile(assembly, options);
```

| Option | Default | Meaning |
|---|---|---|
| `Mapper` | `0` | iNES mapper number, in `0..255`. The number alone does not change placement or generate bank-switching code. |
| `Mmc3BankedLayout` | `false` | Place the program at `$C000` instead of `$8000`; requires mapper 4. |

Only options that select program construction are exposed. Mirroring, battery
flags, PRG/CHR bank counts, and bank asset packaging belong to the ROM image
pipeline, not this model-only operation. The existing
[MSBuild properties](msbuild-properties.md) and
[MMC3 layout requirements](mmc3-bank-layout.md) still apply when packaging a
ROM, including fixed-bank capacity and keeping MMC3 PRG mode 0 active.

## Native assembly and external symbols

Pass existing `AssemblyReader` inputs, backed either by paths or `TextReader`s.
Native code uses the existing ca65-compatible assembler; this API does not
introduce another assembler or change the extern calling convention.

For a C# program containing a top-level native declaration:

```csharp
static extern void native_write();
native_write();
while (true) ;
```

The host can supply its implementation entirely in memory:

```csharp
using var native = new AssemblyReader(new StringReader("""
    .segment "CODE"
    _native_write:
        lda #$42
        sta $6000
        rts
    """));
Program6502 program = NesCompiler.Compile(assembly, assemblyFiles: [native]);
byte[] code = program.ToBytes();
```

As in ROM compilation, native source inputs are assembled when the input PE
declares extern methods. No extern declarations means those sources do not add
native code. Native interop is still subject to the compiler's supported C#
signatures and runtime ABI.

Alternatively, omit the source and bind a native entry point already provided
by the host:

```csharp
Program6502 program = NesCompiler.Compile(assembly);
program.DefineExternalLabel("_native_write", 0x6000);
byte[] code = program.ToBytes();
```

The host must actually provide executable code at that CPU address.
`DefineExternalLabel` bindings survive subsequent address resolution. The
existing `Program6502` APIs also allow adding blocks and changing `BaseAddress`;
call `ResolveAddresses()` after changing `BaseAddress`, then `ToBytes()` to
finalize branch relaxation. Missing symbols fail at emission with
`UnresolvedLabelException`, not with fabricated addresses.

## Ownership and diagnostics

The PE stream must be readable and seekable, positioned at the start of the
assembly image. The compiler does not rewind it or buffer non-seekable streams.
An image embedded at a nonzero stream offset is read from that offset.
Positions may advance; explicitly reset `Position` before compiling the same
assembly again. Copy a non-seekable source into a caller-owned `MemoryStream`
first if needed.

All caller inputs are borrowed on success **and** failure: `Compile` disposes
its internal compiler/metadata resources, but never disposes the supplied PE
stream, `AssemblyReader`s, or logger. Dispose your readers yourself.
`AssemblyReader` owns its underlying `TextReader` and disposes it when the
reader is disposed.

An `AssemblyReader` lazily caches text from its initial reader position to EOF.
Native assembly and `GetSegments()` replay that cached text independently, so
CHR extraction and native compilation work in either order. Reuse the same
reader to compile the same source; construct a new reader to observe file
changes. Readers are not safe for concurrent use.

Pass the existing `ILogger` for compiler messages. Errors are ordinary
exceptions: invalid arguments use `ArgumentException`/`ArgumentNullException`,
invalid target combinations use `InvalidOperationException`, malformed PE
input can use `BadImageFormatException` or `InvalidOperationException` (for an
image without metadata), and unsupported IL can use
`TranspileException`. Assembler and I/O exceptions propagate unchanged.
There is no `TargetInvocationException` wrapper and no success-shaped fallback.

This additive facade is the supported boundary; the internal constructor and
other `Transpiler` internals remain implementation details. Existing MSBuild
and CLI entry points retain their input ownership and ROM output behavior.
