using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RoslynTests_PokePeek : RoslynTests
{
    public RoslynTests_PokePeek(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void PokeConstant()
    {
        // poke(0x4015, 0x0F) should emit LDA #$0F, STA $4015
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90F", hex);    // LDA #$0F
        Assert.Contains("8D1540", hex);  // STA $4015
    }

    [Fact]
    public void PokeConsecutiveSameValue()
    {
        // Two consecutive pokes with the same value: LDA emitted only once
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            poke(0x4016, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90F", hex);    // LDA #$0F (once)
        Assert.Contains("8D1540", hex);  // STA $4015
        Assert.Contains("8D1640", hex);  // STA $4016

        // LDA #$0F should appear only once (optimization)
        int firstLda = hex.IndexOf("A90F");
        int secondLda = hex.IndexOf("A90F", firstLda + 4);
        Assert.Equal(-1, secondLda);
    }

    [Fact]
    public void PokeThenCallThenPoke()
    {
        // poke, then a function call, then poke with same value:
        // The second poke MUST re-emit LDA because the call clobbered A
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            pal_col(0, 0x30);
            poke(0x4016, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("8D1540", hex);  // STA $4015
        Assert.Contains("8D1640", hex);  // STA $4016

        // LDA #$0F must appear TWICE (the call between clobbers A)
        int firstLda = hex.IndexOf("A90F");
        Assert.NotEqual(-1, firstLda);
        int secondLda = hex.IndexOf("A90F", firstLda + 4);
        Assert.NotEqual(-1, secondLda);
    }

    [Fact]
    public void PeekConstant()
    {
        // peek(0x2002) should emit LDA $2002 (absolute)
        var bytes = GetProgramBytes(
            """
            byte status = peek(0x2002);
            pal_col(0, status);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("AD0220", hex);  // LDA $2002 (absolute)
    }

    [Fact]
    public void PeekSmallConstant()
    {
        // peek(0x003C) — address 0x3C fits in a byte, so the transpiler emits
        // a single LDA via WriteLdc(byte). The peek handler must remove only 1
        // prior instruction instead of 2 for the ushort path (LDX + LDA).
        var bytes = GetProgramBytes(
            """
            byte value = peek(0x003C);
            pal_col(0, value);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("AD3C00", hex);  // LDA $003C (absolute)
    }

    [Fact]
    public void PokeSmallConstant()
    {
        // poke(0x003C, 0x07) — address 0x3C fits in a byte, so the transpiler
        // emits a single LDA via WriteLdc(byte). The poke handler must remove
        // only 3 prior instructions instead of 4 for the ushort path.
        var bytes = GetProgramBytes(
            """
            poke(0x003C, 0x07);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A907", hex);    // LDA #$07
        Assert.Contains("8D3C00", hex);  // STA $003C (absolute)
    }
}
