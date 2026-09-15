# In-memory compilation

`dotnes.NesCompiler.Compile` is the supported entry point for tools that need a
`dotnes.ObjectModel.Program6502` from an already compiled .NET assembly. It uses
the same transpiler and target placement as the MSBuild/CLI ROM pipeline; no
reflection over the internal `Transpiler` is necessary.

The API lives alongside `Program6502` in `dotnes.tasks.dll` and is also included
in the `dotnes.compiler` tool's `dotnes.dll`. Reference one compiler assembly,
not both: they contain the same types.

## Reference from a desktop test project

A standard .NET 10 test project can reference the existing `dotnes` package
directly. Set `DotnesVersion` to the package version you are testing:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="dotnes" Version="$(DotnesVersion)" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
</Project>
```

`Microsoft.NET.Test.Sdk` sets the standard MSBuild `IsTestProject` property.
Detection runs after all NuGet package props, before the SDK's compiler defaults
and the project body. Standard test projects need no custom imports,
package-path references, analyzer-removal targets, or extra dotnes flags.
Project names, directory names, and `OutputType=Library` are **not** used to
detect tests.

For a nonstandard test framework that does not set `IsTestProject` in package
props, set `<IsTestProject>true</IsTestProject>` in `Directory.Build.props`.
Setting it only in the project body is too late without a test SDK. Changing
the classification in either direction after package props produces an error
rather than mixing desktop and ROM settings.
This timing preserves existing ROM projects' ability to override compiler and
output settings in their project bodies.

In test projects, dotnes leaves the SDK's framework references, CLR output,
dependency/runtime configuration files, test discovery, optimization, debug,
unsafe-code, and other ordinary compiler settings alone. It does not transpile
the test assembly, write a `.nes`, or add NES-only analyzer diagnostics or
implicit NES usings. `dotnet test` runs normally.

NuGet supplies compile and runtime assets for `dotnes.tasks` and `neslib`;
the .NET 10 shared framework supplies the compiler's metadata and immutable
collection dependencies. Tests can call `NesCompiler.Compile`, use `Program6502`,
and read native sources with `AssemblyReader` without loading an MSBuild task
host. `NESLib` methods still describe NES operations, not a desktop emulator:
compile fixture programs that call them rather than executing those stubs.
If tests compile fixture C# at runtime, add their normal Roslyn dependency,
such as `Microsoft.CodeAnalysis.CSharp`, separately.

Compiler/runtime assets and test-only analyzer exclusion also work through
project references. ROM build settings and transpilation targets do not
propagate to those referencing projects.

This automatic integration is scoped to standard test projects. A non-test
library or tooling executable with a direct `dotnes` reference still gets ROM
build behavior; `IsTestProject` is not a general-purpose host-mode switch.
When hosting the API outside this test-project integration, keep the compiler's
accompanying dependencies, including `neslib`, available to the host.

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
| `OptimizeByteHelpers` | `false` | Use private RAM parameter homes for proven non-reentrant small byte helpers. Applies the same eligibility and native-code fallback as [`NESOptimizeByteHelpers`](msbuild-properties.md#nesoptimizebytehelpers). |

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
`DefineExternalLabel` bindings survive subsequent address resolution and also
resolve native data references such as `.word _native_write` during `ToBytes()`.
Missing instruction operands (for example, a `JSR` target) throw
`UnresolvedLabelException` at emission. **This is not a complete linker
validation:** the existing emitter leaves unresolved native data relocations
such as `.word`/`.addr` at their placeholder values (normally zero). Callers must
ensure those data symbols are defined before emission; successful `ToBytes()`
alone does not prove that every native data reference was bound.

`OptimizeByteHelpers` also preserves the standard calling convention for this
late-binding path: any C# extern declaration disables the optimization for the
compilation, even if no native source is supplied or the declaration is unused.
`DefineExternalLabel` only binds a symbol; it does not install code, add a call,
or register an interrupt handler. Binding an unreferenced name therefore creates
no new entry point. Native callers pass the byte argument in A; the managed
callee owns its parameter storage in both conventions.

The optimization proof covers the program as compiled. If a host injects extra
instructions, callbacks, or interrupt entry points after compilation, compile
with `OptimizeByteHelpers = false`; arbitrary model edits or out-of-band host
execution are not reanalyzed by `DefineExternalLabel` or `ToBytes()`.

Choose the complete program's placement through `CompilationOptions` before
compiling. Although `Program6502` exposes `BaseAddress` and block editing, those
operations are not a supported way to rebase or freely rearrange the complete
compiled runtime. Startup routines such as `copydata` and `donelib` embed
addresses calculated during compilation; changing `BaseAddress` and resolving
labels does not relocate those immediate values. Compile again with the desired
layout instead. Late external-symbol binding does not change the runtime's
placement and remains supported.

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
