using System.Text;
using Xunit.Abstractions;

namespace dotnes.tests;

public class TranspilerTests
{
    readonly ILogger _logger;

    public TranspilerTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    [Theory]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    public Task ReadStaticVoidMain(string name, bool debug)
    {
        var suffix = debug ? "debug" : "release";
        var dll = Utilities.GetResource($"{name}.{suffix}.dll");
        var transpiler = new Transpiler(dll, Array.Empty<AssemblyReader>());
        var builder = new StringBuilder();
        foreach (var instruction in transpiler.ReadStaticVoidMain())
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(instruction.ToString());
        }

        var settings = new VerifySettings();
        settings.DisableRequireUniquePrefix();
        settings.UseFileName($"TranspilerTests.ReadStaticVoidMain.{name}");
        return Verify(builder, settings);
    }

    [Theory]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("onelocal", true)]
    [InlineData("onelocal", false)]
    [InlineData("onelocalbyte", true)]
    [InlineData("onelocalbyte", false)]
    [InlineData("staticsprite", true)]
    [InlineData("staticsprite", false)]
    public Task Write(string name, bool debug)
    {
        var configuration = debug ? "debug" : "release";
        var chr_generic = new StreamReader(Utilities.GetResource("chr_generic.s"));

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var il = new Transpiler(dll, [new AssemblyReader(chr_generic)], _logger);
        using var ms = new MemoryStream();
        il.Write(ms);

        var settings = new VerifySettings();
        settings.DisableRequireUniquePrefix();
        settings.UseFileName($"TranspilerTests.Write.{name}");
        return Verify(ms.ToArray(), settings);
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData("hello", false)]
    [InlineData("attributetable", true)]
    [InlineData("attributetable", false)]
    public void BuildProgram6502(string name, bool debug)
    {
        var configuration = debug ? "debug" : "release";

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var transpiler = new Transpiler(dll, [], _logger);
        
        // Build using single-pass transpilation
        var program = transpiler.BuildProgram6502(out ushort sizeOfMain, out byte locals);

        // Verify the program has blocks
        Assert.True(program.BlockCount > 0, "Program should have blocks");
        Assert.True(program.TotalSize > 0, "Program should have non-zero size");

        // Verify main block exists with correct size
        var mainBlock = program.GetBlock("main");
        Assert.NotNull(mainBlock);
        Assert.True(mainBlock.Count > 0, "Main block should have instructions");
        Assert.Equal(sizeOfMain, mainBlock.Size);

        // Verify built-in labels are defined
        var labels = program.GetLabels();
        Assert.True(labels.ContainsKey("popa"), "popa label should be defined");
        Assert.True(labels.ContainsKey("popax"), "popax label should be defined");
        Assert.True(labels.ContainsKey("pusha"), "pusha label should be defined");
        Assert.True(labels.ContainsKey("pushax"), "pushax label should be defined");
        Assert.True(labels.ContainsKey("main"), "main label should be defined");

        // Verify forward reference labels have non-zero addresses
        Assert.NotEqual(0, labels["popa"]);
        Assert.NotEqual(0, labels["popax"]);
        Assert.NotEqual(0, labels["pusha"]);
        Assert.NotEqual(0, labels["pushax"]);
    }

    [Fact]
    public void CompareWriteAndBuildProgram6502()
    {
        var chr_generic = new StreamReader(Utilities.GetResource("chr_generic.s"));

        // Get legacy output
        using var dll = Utilities.GetResource("hello.release.dll");
        using var legacyTranspiler = new Transpiler(dll, [new AssemblyReader(chr_generic)], _logger);
        using var legacyMs = new MemoryStream();
        legacyTranspiler.Write(legacyMs);
        var legacyBytes = legacyMs.ToArray();

        // Get new output (just the program part, not full ROM)
        dll.Position = 0;
        chr_generic.BaseStream.Position = 0;
        using var newTranspiler = new Transpiler(dll, [new AssemblyReader(chr_generic)], _logger);
        var program = newTranspiler.BuildProgram6502(out var sizeOfMain, out var locals);
        var programBytes = program.ToBytes();

        // Compare the PRG section (after 16-byte header) with program bytes
        var legacyPrg = legacyBytes.AsSpan(16, programBytes.Length);
        
        // Find differences
        var differences = new List<(int offset, byte legacy, byte program)>();
        for (int i = 0; i < Math.Min(legacyPrg.Length, programBytes.Length); i++)
        {
            if (legacyPrg[i] != programBytes[i])
            {
                differences.Add((i, legacyPrg[i], programBytes[i]));
            }
        }

        if (differences.Count > 0)
        {
            _logger.WriteLine($"Found {differences.Count} differences:");
            foreach (var (offset, legacy, prog) in differences.Take(20))
            {
                _logger.WriteLine($"  0x{offset:X4}: legacy=0x{legacy:X2}, program=0x{prog:X2}");
            }
        }

        Assert.Empty(differences);
    }
}
