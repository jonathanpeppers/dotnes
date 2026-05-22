using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ClosuresTests : RoslynTests
{
    public ClosuresTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CapturingByteArray()
    {
        // When a non-static local function captures an outer byte[] variable,
        // the compiler generates a closure struct. The transpiler should handle
        // this by mapping closure byte[] fields to ROM data labels and scalar
        // fields to zero-page addresses.
        var (program, transpiler) = BuildProgram(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            apply_palette();
            ppu_on_all();
            while (true) ;

            void apply_palette()
            {
                pal_bg(palette);
            }
            """);

        // The program should compile without errors
        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        // Verify the closure method was detected
        Assert.Contains("apply_palette", transpiler.UserMethods.Keys);

        // The full ROM should contain the byte array data (0x0F, 0x10, 0x20, 0x30)
        var fullBytes = program.ToBytes();
        var fullHex = Convert.ToHexString(fullBytes);
        _logger.WriteLine($"CapturingByteArray fullHex: {fullHex}");
        Assert.Contains("0F102030", fullHex); // byte array ROM data
        transpiler.Dispose();
    }

    [Fact]
    public void CapturingByteArrayAndScalar()
    {
        // Test: closure capturing both a byte[] (ROM data) and a scalar byte variable.
        // The byte[] field should use ROM labels, the scalar should use a zero-page address.
        var bytes = GetProgramBytes(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            byte color = 0x15;
            apply_palette();
            ppu_on_all();
            while (true) ;

            void apply_palette()
            {
                pal_bg(palette);
                pal_col(0, color);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CapturingByteArrayAndScalar hex: {hex}");

        // Verify the scalar closure field (color = 0x15) is stored at its address.
        // The main should emit LDA #$15 (A915) followed by STA $addr (8D xx xx).
        int ldaIdx = hex.IndexOf("A915");
        Assert.True(ldaIdx >= 0, $"LDA #$15 not found. Hex: {hex}");
        // Verify STA follows the LDA (8D = STA absolute)
        Assert.Equal("8D", hex.Substring(ldaIdx + 4, 2));
    }

    [Fact]
    public void MethodWithRealParams()
    {
        // Test: closure method that has real parameters in addition to
        // the implicit closure struct ref. Roslyn places the closure ref
        // as the LAST parameter, not the first.
        var (program, transpiler) = BuildProgram(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            byte color = 0x15;
            apply_at(3, color);
            ppu_on_all();
            while (true) ;

            void apply_at(byte index, byte c)
            {
                pal_col(index, c);
                pal_bg(palette);
            }
            """);
        transpiler.Dispose();

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var fullBytes = program.ToBytes();
        var fullHex = Convert.ToHexString(fullBytes);
        _logger.WriteLine($"MethodWithRealParams hex: {fullHex}");

        // Verify palette data is in the full ROM
        Assert.Contains("0F102030", fullHex);
    }
}
