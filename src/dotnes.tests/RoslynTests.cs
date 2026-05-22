using dotnes.ObjectModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace dotnes.tests;

/// <summary>
/// A set of tests to assert a short C# program -> 6502 assembly result
/// </summary>
public abstract class RoslynTests
{
    readonly MemoryStream _stream = new();
    private protected readonly ILogger _logger;

    protected RoslynTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    protected void AssertProgram(string csharpSource, string expectedAssembly)
    {
        _stream.SetLength(0);

        // Implicit global using
        csharpSource = $"using NES;using static NES.NESLib;{Environment.NewLine}{csharpSource}";

        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
        var systemPrivateCoreLib = typeof(object).Assembly.Location;
        var frameworkDir = Path.GetDirectoryName(systemPrivateCoreLib);
        Assert.NotNull(frameworkDir);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(systemPrivateCoreLib),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };

        var compilation = CSharpCompilation
            .Create(
                "hello.dll",
                [syntaxTree],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));

        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
        {
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        }

        _stream.Seek(0, SeekOrigin.Begin);

        using var transpiler = new Transpiler(_stream, [new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")))], _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        var mainBlock = program.GetMainBlock();
        AssertEx.Equal(Utilities.ToByteArray(expectedAssembly), mainBlock);
    }

    protected byte[] GetProgramBytes(string csharpSource)
        => GetProgramBytes(csharpSource, additionalAssemblyFiles: null);

    protected byte[] GetProgramBytes(string csharpSource, IList<AssemblyReader>? additionalAssemblyFiles, bool allowUnsafe = false)
    {
        var (program, transpiler) = BuildProgram(csharpSource, additionalAssemblyFiles, allowUnsafe);
        transpiler.Dispose();
        return program.GetMainBlock();
    }

    private protected (Program6502 program, Transpiler transpiler) BuildProgram(string csharpSource, IList<AssemblyReader>? additionalAssemblyFiles = null, bool allowUnsafe = false)
    {
        _stream.SetLength(0);
        csharpSource = $"using NES;using static NES.NESLib;{Environment.NewLine}{csharpSource}";
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
        var systemPrivateCoreLib = typeof(object).Assembly.Location;
        var frameworkDir = Path.GetDirectoryName(systemPrivateCoreLib)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(systemPrivateCoreLib),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };
        var compilation = CSharpCompilation
            .Create("test.dll", [syntaxTree], references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true,
                    allowUnsafe: allowUnsafe));
        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        _stream.Seek(0, SeekOrigin.Begin);
        var assemblyFiles = new List<AssemblyReader> { new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s"))) };
        if (additionalAssemblyFiles != null)
            assemblyFiles.AddRange(additionalAssemblyFiles);
        var transpiler = new Transpiler(_stream, assemblyFiles, _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        return (program, transpiler);
    }

    private protected (Program6502 program, Transpiler transpiler) BuildProgramMultiFile(string[] csharpSources, IList<AssemblyReader>? additionalAssemblyFiles = null)
    {
        _stream.SetLength(0);
        var syntaxTrees = csharpSources.Select(source =>
        {
            source = $"using NES;using static NES.NESLib;{Environment.NewLine}{source}";
            return CSharpSyntaxTree.ParseText(source);
        }).ToArray();
        var systemPrivateCoreLib = typeof(object).Assembly.Location;
        var frameworkDir = Path.GetDirectoryName(systemPrivateCoreLib)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(systemPrivateCoreLib),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };
        var compilation = CSharpCompilation
            .Create("test.dll", syntaxTrees, references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true));
        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        _stream.Seek(0, SeekOrigin.Begin);
        var assemblyFiles = new List<AssemblyReader> { new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s"))) };
        if (additionalAssemblyFiles != null)
            assemblyFiles.AddRange(additionalAssemblyFiles);
        var transpiler = new Transpiler(_stream, assemblyFiles, _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        return (program, transpiler);
    }

}
