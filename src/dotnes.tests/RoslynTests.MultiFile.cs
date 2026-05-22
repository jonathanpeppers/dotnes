using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RoslynTests_MultiFile : RoslynTests
{
    public RoslynTests_MultiFile(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void MultiFile_StaticHelperClass()
    {
        // Verify that methods in a separate static class are correctly transpiled.
        // This tests the basic multi-file scenario where helper methods live in
        // a static class in a different file.
        var (program, _) = BuildProgramMultiFile([
            // File 1: Program.cs (top-level statements)
            """
            Palette.setup();
            ppu_on_all();
            while (true) ;
            """,
            // File 2: Palette.cs (static helper class)
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x14);
                    pal_col(2, 0x20);
                    pal_col(3, 0x30);
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        _logger.WriteLine($"MultiFile_StaticHelperClass main hex: {Convert.ToHexString(mainBlock)}");

        // First instruction in main should be JSR (0x20) to the setup method
        Assert.Equal(0x20, mainBlock[0]);
    }

    [Fact]
    public void MultiFile_MatchesSingleFile()
    {
        // Verify that a multi-file program produces the same main block bytes
        // as the equivalent single-file program with a local function.
        var singleFileBytes = GetProgramBytes(
            """
            setup();
            ppu_on_all();
            while (true) ;

            static void setup()
            {
                pal_col(0, 0x02);
                pal_col(1, 0x14);
                pal_col(2, 0x20);
                pal_col(3, 0x30);
            }
            """);

        var (multiFileProgram, _) = BuildProgramMultiFile([
            """
            Palette.setup();
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x14);
                    pal_col(2, 0x20);
                    pal_col(3, 0x30);
                }
            }
            """
        ]);

        var multiFileBytes = multiFileProgram.GetMainBlock();

        _logger.WriteLine($"Single-file main: {Convert.ToHexString(singleFileBytes)}");
        _logger.WriteLine($"Multi-file main:  {Convert.ToHexString(multiFileBytes)}");

        Assert.Equal(singleFileBytes, multiFileBytes);
    }

    [Fact]
    public void MultiFile_MethodWithParameters()
    {
        // Verify that methods with parameters in a separate class work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            Graphics.set_color(0, 0x30);
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Graphics
            {
                public static void set_color(byte index, byte color)
                {
                    pal_col(index, color);
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var hex = Convert.ToHexString(mainBlock);
        _logger.WriteLine($"MultiFile_MethodWithParameters main hex: {hex}");

        // Should contain LDA #$00 for index and LDA #$30 for color
        Assert.Contains("A900", hex);
        Assert.Contains("A930", hex);
    }

    [Fact]
    public void MultiFile_MethodWithReturnValue()
    {
        // Verify that methods with return values in a separate class work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            pal_col(0, Colors.white());
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Colors
            {
                public static byte white() => 0x30;
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var hex = Convert.ToHexString(mainBlock);
        _logger.WriteLine($"MultiFile_MethodWithReturnValue main hex: {hex}");

        // Should contain LDA #$00 for pal_col index arg
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void MultiFile_MultipleHelperClasses()
    {
        // Verify that methods across multiple static helper classes work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            Palette.setup();
            Display.enable();
            while (true) ;
            """,
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x30);
                }
            }
            """,
            """
            static class Display
            {
                public static void enable()
                {
                    ppu_on_all();
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        _logger.WriteLine($"MultiFile_MultipleHelperClasses main hex: {Convert.ToHexString(mainBlock)}");

        // Main should begin with two JSR (0x20) calls at instruction boundaries:
        // JSR setup (3 bytes) then JSR enable (3 bytes)
        Assert.True(mainBlock.Length >= 6, $"Expected at least 6 bytes, got {mainBlock.Length}");
        Assert.Equal(0x20, mainBlock[0]);  // JSR setup
        Assert.Equal(0x20, mainBlock[3]);  // JSR enable
    }
}
