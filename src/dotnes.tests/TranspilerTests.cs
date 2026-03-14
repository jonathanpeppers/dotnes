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
    [InlineData("multifile", true)]
    [InlineData("multifile", false)]
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
    [InlineData("lols", true)]
    [InlineData("lols", false)]
    [InlineData("movingsprite", true)]
    [InlineData("movingsprite", false)]
    [InlineData("onelocal", true)]
    [InlineData("onelocal", false)]
    [InlineData("onelocalbyte", true)]
    [InlineData("onelocalbyte", false)]
    [InlineData("staticsprite", true)]
    [InlineData("staticsprite", false)]
    [InlineData("music", true)]
    [InlineData("music", false)]
    [InlineData("metasprites", true)]
    [InlineData("metasprites", false)]
    [InlineData("flicker", true)]
    [InlineData("flicker", false)]
    [InlineData("tint", true)]
    [InlineData("tint", false)]
    [InlineData("scroll", true)]
    [InlineData("scroll", false)]
    [InlineData("rletitle", true)]
    [InlineData("rletitle", false)]
    [InlineData("tileset1", true)]
    [InlineData("tileset1", false)]
    [InlineData("sprites", true)]
    [InlineData("sprites", false)]
    [InlineData("metacursor", true)]
    [InlineData("metacursor", false)]
    [InlineData("metatrigger", true)]
    [InlineData("metatrigger", false)]
    [InlineData("statusbar", true, true)]
    [InlineData("statusbar", false, true)]
    [InlineData("vrambuffer", true)]
    [InlineData("vrambuffer", false)]
    [InlineData("horizscroll", true, true)]
    [InlineData("horizscroll", false, true)]
    [InlineData("horizmask", true, true)]
    [InlineData("horizmask", false, true)]
    [InlineData("animation", true)]
    [InlineData("animation", false)]
    [InlineData("multifile", true)]
    [InlineData("multifile", false)]
    [InlineData("peekpoke", true)]
    [InlineData("peekpoke", false)]
    [InlineData("fade", true)]
    [InlineData("fade", false)]
    [InlineData("ppuhello", true)]
    [InlineData("ppuhello", false)]
    [InlineData("scoreboard", true)]
    [InlineData("scoreboard", false)]
    [InlineData("bigsprites", true)]
    [InlineData("bigsprites", false)]
    [InlineData("aputest", true)]
    [InlineData("aputest", false)]
    [InlineData("bankswitch", true, false, 4, 4, 8)]
    [InlineData("bankswitch", false, false, 4, 4, 8)]
    [InlineData("climber", true)]
    [InlineData("climber", false)]
    [InlineData("pong", true)]
    [InlineData("pong", false)]
    [InlineData("transtable", true, false, 0, 2, 0)]
    [InlineData("transtable", false, false, 0, 2, 0)]
    public Task Write(string name, bool debug, bool verticalMirroring = false, int mapper = 0, int prgBanks = 2, int chrBanks = 1)
    {
        var configuration = debug ? "debug" : "release";

        var assemblyReaders = new List<AssemblyReader>();

        // CHR RAM samples (chrBanks=0) don't need a CHR assembly file
        if (chrBanks > 0)
        {
            var chrName = $"chr_{name}.s";
            var chrStream = typeof(Utilities).Assembly.GetManifestResourceStream(chrName);
            var chr_generic = new StreamReader(chrStream ?? Utilities.GetResource("chr_generic.s"));
            assemblyReaders.Add(new AssemblyReader(chr_generic));
        }

        // Include fami assembly files (famitone2.s, demosounds.s, etc.) only for samples that use extern methods
        if (name is "climber" or "fami")
        {
            var famiDir = Path.Combine(AppContext.BaseDirectory, "Data", "fami");
            if (Directory.Exists(famiDir))
            {
                foreach (var sFile in Directory.GetFiles(famiDir, "*.s").OrderBy(f => f))
                    assemblyReaders.Add(new AssemblyReader(sFile));
            }
        }

        using var dll = Utilities.GetResource($"{name}.{configuration}.dll");
        using var il = new Transpiler(dll, assemblyReaders, _logger, verticalMirroring, mapper, prgBanks, chrBanks);
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
        var program = transpiler.BuildProgram6502(out ushort sizeOfMain, out ushort locals);

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
}
