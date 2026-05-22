using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RandomTests : RoslynTests
{
    public RandomTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Rand16_Returns16Bit()
    {
        // rand() returns ushort (16-bit) in A:X
        var bytes = GetProgramBytes(
            """
            ushort r = rand16();
            pal_col(0, (byte)r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Rand16_Returns16Bit hex: {hex}");

        // Should contain JSR to rand subroutine and store 16-bit result
        // The rand label resolves to a JSR target; verify the full JSR + STA pattern
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to some address
        // After rand() returns, the 16-bit result (A:X) should be stored to a local
        // STA $0325 (low byte) + STX $0326 (high byte) for a ushort local
        Assert.Contains("8D2503", hex); // STA $0325
        Assert.Contains("8E2603", hex); // STX $0326
    }

    [Fact]
    public void Rand16_ByteTruncation()
    {
        // (byte)rand16() should truncate 16-bit result to 8-bit (just use A, discard X)
        var bytes = GetProgramBytes(
            """
            byte r = (byte)rand16();
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Rand16_ByteTruncation hex: {hex}");

        // Should contain JSR to rand and STA for storing byte local (A only, X discarded)
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to rand
        Assert.Contains("8D2503", hex); // STA $0325 (byte local)
    }

    [Fact]
    public void SRand_AcceptsUshort()
    {
        // srand(ushort seed) should accept a 16-bit seed value
        var bytes = GetProgramBytes(
            """
            srand(42);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SRand_AcceptsUshort hex: {hex}");

        // Should contain LDA #42 (0x2A) and JSR to srand
        Assert.Contains("A92A", hex); // LDA #$2A (42, low byte of seed)
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to srand
    }
}
