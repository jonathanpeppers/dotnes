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
}
