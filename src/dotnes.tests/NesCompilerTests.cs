using System.Runtime.Loader;
using dotnes.ObjectModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NesCompilerTests(ITestOutputHelper output) : RoslynTests(output)
{
    [Fact]
    public void NoRecognizedEntryPointDoesNotRequireSyntheticLocalMetadata()
    {
        var compilation = CSharpCompilation.Create(
            "AlternateLanguageEntry",
            [CSharpSyntaxTree.ParseText("public static class Entry { public static void AlternateMain() { NES.NESLib.ppu_off(); } }")],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "netstandard.dll")),
                MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        assembly.Position = 0;
        using var transpiler = new Transpiler(assembly, Array.Empty<AssemblyReader>());
        Assert.Empty(transpiler.ReadStaticVoidMain());
        Assert.False(transpiler.NumericTypes.ContainsKey("main"));
        var program = transpiler.BuildProgram6502(out _, out _);
        Assert.NotEmpty(program.ToBytes());
    }

    const string NativeCaller = """
        static extern void native_write();
        native_write();
        while (true) ;
        """;

    const string NativeSource = """
        .segment "CODE"
        _native_write:
            lda #$42
            sta $6000
            rts
        .segment "CHARS"
        .byte $12,$34
        """;

    [Fact]
    public void ExternalConsumerCanCompileAgainstPublicSurface()
    {
        // This assembly is not dotnes.tests and has no InternalsVisibleTo access.
        var source = """
            using System.IO;
            using dotnes;
            using dotnes.ObjectModel;

            public static class Consumer
            {
                public static Program6502 Compile(Stream assembly, TextReader source, ILogger logger)
                {
                    using var native = new AssemblyReader(source);
                    var options = new CompilationOptions { Mapper = 4, Mmc3BankedLayout = true };
                    var program = NesCompiler.Compile(assembly, options, new[] { native }, logger);
                    program.DefineExternalLabel("_device_write", 0x6000);
                    program.ToBytes();
                    return program;
                }
            }
            """;
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(framework, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(framework, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(NesCompiler).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "ExternalCompilationConsumer",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var outputAssembly = new MemoryStream();
        var result = compilation.Emit(outputAssembly);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        outputAssembly.Position = 0;
        var context = new AssemblyLoadContext("ExternalCompilationConsumer", isCollectible: true);
        try
        {
            var consumer = context.LoadFromStream(outputAssembly);
            // Reflection loads this test-only consumer, never the compiler implementation.
            var compile = consumer.GetType("Consumer")!.GetMethod("Compile")!
                .CreateDelegate<Func<Stream, TextReader, ILogger, Program6502>>();
            using var assembly = CompileAssembly(NativeCaller);
            var program = compile(assembly, new StringReader(NativeSource), _logger);
            Assert.Equal(0xC000, program.BaseAddress);
            Assert.Equal(0x6000, program.GetLabels()["_device_write"]);
            Assert.NotEmpty(program.GetMainBlock("_native_write"));
            Assert.True(assembly.CanRead);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void CompilesWithoutGraphicsOrReflection()
    {
        Program6502 program;
        using (var assembly = CompileAssembly("poke(0x6000, 0x42); while (true) ;"))
            program = NesCompiler.Compile(assembly, logger: _logger);

        Assert.Equal(0x8000, program.BaseAddress);
        Assert.Contains("A9428D0060", Convert.ToHexString(program.GetMainBlock()));
        Assert.NotEmpty(program.ToBytes());
    }

    [Fact]
    public void ReadsPEFromCallerPositionWithoutRewinding()
    {
        using var assembly = CompileAssembly("while (true) ;");
        using var prefixed = new MemoryStream();
        prefixed.Write(new byte[13]);
        assembly.CopyTo(prefixed);
        prefixed.Position = 13;
        var program = NesCompiler.Compile(prefixed);
        Assert.NotEmpty(program.ToBytes());
        Assert.True(prefixed.CanRead);
    }

    [Theory]
    [InlineData(0, false, 0x8000)]
    [InlineData(4, false, 0x8000)]
    [InlineData(4, true, 0xC000)]
    public void NativeTextInputMatchesProductionProgram(int mapper, bool banked, int baseAddress)
    {
        using var assembly = CompileAssembly(NativeCaller);
        using var text = new StringReader(NativeSource);
        using var native = new AssemblyReader(text);
        var program = NesCompiler.Compile(assembly,
            new CompilationOptions { Mapper = mapper, Mmc3BankedLayout = banked },
            [native], _logger);
        byte[] bytes = program.ToBytes();
        Assert.Equal(baseAddress, program.BaseAddress);
        Assert.Equal(new byte[] { 0xA9, 0x42, 0x8D, 0x00, 0x60, 0x60 }, program.GetMainBlock("_native_write"));
        ushort address = program.GetLabels()["_native_write"];
        Assert.Equal(new byte[] { 0x20, (byte)address, (byte)(address >> 8) }, program.GetMainBlock()[..3]);
        Assert.Equal(new byte[] { 0x12, 0x34 }, Assert.Single(native.GetSegments()).Bytes);
        Assert.Equal(-1, text.Peek()); // Borrowed reader is still open, positioned at EOF.

        assembly.Position = 0;
        using var rom = new MemoryStream();
        using (var transpiler = new Transpiler(assembly, [native], _logger,
            mapper: mapper, mmc3BankedLayout: banked))
            transpiler.Write(rom);

        int offset = 16 + baseAddress - 0x8000;
        Assert.Equal(bytes, rom.ToArray().AsSpan(offset, bytes.Length).ToArray());
        Assert.Equal(new byte[] { 0x12, 0x34 }, rom.ToArray().AsSpan(16 + 32768, 2).ToArray());
        Assert.True(assembly.CanRead);
        Assert.Throws<ObjectDisposedException>(() => text.Peek()); // Production still owns its readers.
    }

    [Fact]
    public void PathAndTextNativeInputsAreEquivalent()
    {
        using var assembly = CompileAssembly(NativeCaller);
        using var native = new AssemblyReader(new StringReader(NativeSource));
        var expected = NesCompiler.Compile(assembly, assemblyFiles: [native]).ToBytes();
        string path = Path.Combine(Path.GetTempPath(), $"dotnes-native-{Guid.NewGuid():N}.s");
        try
        {
            File.WriteAllText(path, NativeSource);
            assembly.Position = 0;
            using var fileNative = new AssemblyReader(path);
            var actual = NesCompiler.Compile(assembly, assemblyFiles: [fileNative]).ToBytes();
            Assert.Equal(expected, actual);
            Assert.Equal(new byte[] { 0x12, 0x34 }, Assert.Single(fileNative.GetSegments()).Bytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExternalBindingSurvivesAddressResolutionAndInputDisposal()
    {
        Program6502 program;
        using (var assembly = CompileAssembly(NativeCaller))
            program = NesCompiler.Compile(assembly);

        var exception = Assert.Throws<UnresolvedLabelException>(() => program.ToBytes());
        Assert.Equal("_native_write", exception.Label);
        program.DefineExternalLabel("_native_write", 0x6000);
        program.ResolveAddresses();
        Assert.NotEmpty(program.ToBytes());
        Assert.Equal(new byte[] { 0x20, 0x00, 0x60 }, program.GetMainBlock()[..3]);
    }

    [Fact]
    public void NativeDataReferencesCanBeBoundAfterCompilation()
    {
        Program6502 program;
        using (var assembly = CompileAssembly(NativeCaller))
        using (var native = new AssemblyReader(new StringReader("""
            .segment "CODE"
            _native_write:
                rts
            .segment "RODATA"
            _native_table:
                .word _host_data
            """)))
            program = NesCompiler.Compile(assembly, assemblyFiles: [native]);

        program.DefineExternalLabel("_host_data", 0x6000);
        program.ResolveAddresses();
        byte[] bytes = program.ToBytes();
        int tableOffset = program.GetLabels()["_native_table"] - program.BaseAddress;
        Assert.Equal(new byte[] { 0x00, 0x60 }, bytes.AsSpan(tableOffset, 2).ToArray());
    }

    [Theory]
    [InlineData(-1, false, "NESMapper")]
    [InlineData(256, false, "NESMapper")]
    [InlineData(0, true, "NESMapper=4")]
    public void InvalidOptionsLeaveInputsOpen(int mapper, bool banked, string message)
    {
        using var assembly = CompileAssembly("while (true) ;");
        using var text = new StringReader(NativeSource);
        using var native = new AssemblyReader(text);
        var exception = Assert.Throws<InvalidOperationException>(() => NesCompiler.Compile(
            assembly, new CompilationOptions { Mapper = mapper, Mmc3BankedLayout = banked }, [native]));
        Assert.Contains(message, exception.Message);
        Assert.True(assembly.CanRead);
        Assert.Equal('.', (char)text.Peek());
    }

    [Fact]
    public void UnsupportedILPropagatesTypedExceptionAndLeavesInputsOpen()
    {
        using var assembly = CompileAssembly("""
            try { ppu_on_all(); }
            catch { ppu_off(); }
            while (true) ;
            """);
        using var text = new StringReader(NativeSource);
        using var native = new AssemblyReader(text);
        var exception = Assert.Throws<TranspileException>(() => NesCompiler.Compile(assembly, assemblyFiles: [native]));
        Assert.Contains("try/catch", exception.Message);
        Assert.True(assembly.CanRead);
        Assert.Equal('.', (char)text.Peek());
    }

    [Fact]
    public void InvalidPELeavesInputsOpen()
    {
        using var assembly = new MemoryStream(new byte[128]);
        using var text = new StringReader(NativeSource);
        using var native = new AssemblyReader(text);
        var exception = Assert.Throws<InvalidOperationException>(() => NesCompiler.Compile(assembly, assemblyFiles: [native]));
        Assert.Contains("metadata", exception.Message);
        Assert.True(assembly.CanRead);
        Assert.Equal('.', (char)text.Peek());
    }

    [Fact]
    public void NativeReadFailurePropagatesAndLeavesInputsOpen()
    {
        using var assembly = CompileAssembly(NativeCaller);
        using var text = new FailingTextReader();
        using var native = new AssemblyReader(text);
        var exception = Assert.Throws<IOException>(() => NesCompiler.Compile(assembly, assemblyFiles: [native]));
        Assert.Same(text.Error, exception);
        Assert.True(assembly.CanRead);
        Assert.Equal('.', (char)text.Peek());
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentNullException>(() => NesCompiler.Compile(null!));
        using var closed = new MemoryStream();
        closed.Dispose();
        Assert.Throws<ArgumentException>(() => NesCompiler.Compile(closed));
        using var assembly = CompileAssembly("while (true) ;");
        Assert.Throws<ArgumentException>(() => NesCompiler.Compile(assembly, assemblyFiles: [null!]));
        using var nonSeekable = new NonSeekableStream();
        var exception = Assert.Throws<ArgumentException>(() => NesCompiler.Compile(nonSeekable));
        Assert.Contains("seekable", exception.Message);
    }

    sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    sealed class FailingTextReader : StringReader
    {
        public IOException Error { get; } = new("Native source read failed.");

        public FailingTextReader() : base(NativeSource) { }

        public override string ReadToEnd() => throw Error;
    }
}
