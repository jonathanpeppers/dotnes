using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ArithmeticTests : RoslynTests
{
    public ArithmeticTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void BcdAddConstant()
    {
        // bcd_add with chained calls — optimizer keeps result on stack
        var bytes = GetProgramBytes(
            """
            ushort score = bcd_add(0, 1);
            score = bcd_add(score, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should have: pushax, bcd_add, pushax, bcd_add, ppu_on_all (5 JSRs + JMP)
        int jsrCount = hex.Split("20").Length - 1;
        Assert.True(jsrCount >= 4, $"Expected at least 4 JSR calls, got {jsrCount}");
    }

    [Fact]
    public void BcdAddToLocal()
    {
        // bcd_add result stored to ushort local, read back in loop
        // The loop forces the compiler to actually store score to a local
        var bytes = GetProgramBytes(
            """
            ushort score = 0;
            byte i = 0;
            while (i < 3) {
                score = bcd_add(score, 1);
                i = (byte)(i + 1);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // bcd_add called inside a loop — STA + STX for ushort local store
        Assert.Contains("8D", hex); // STA absolute (lo byte of ushort local)
        Assert.Contains("8E", hex); // STX absolute (hi byte of ushort local)
    }

    [Fact]
    public void ModuloPowerOf2()
    {
        // x % 8 should optimize to AND #7 (runtime value)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x % 8);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("2907", hex); // AND #$07 (x & 7 == x % 8)
    }

    [Fact]
    public void ModuloGeneral()
    {
        // x % 5 needs software division (runtime value)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x % 5);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain SEC + CMP #$05 + BCC + SBC pattern
        Assert.Contains("38", hex);   // SEC
        Assert.Contains("C905", hex); // CMP #$05
        Assert.Contains("E905", hex); // SBC #$05
    }

    [Fact]
    public void DivisionGeneral()
    {
        // x / 3 needs software division (runtime value, non-power-of-2)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x / 3);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain LDX #$FF, SBC #$03, and TXA opcodes
        Assert.Contains("A2FF", hex); // LDX #$FF
        Assert.Contains("E903", hex); // SBC #$03
        Assert.Contains("8A", hex);   // TXA
    }

    [Fact]
    public void SignedDivisionByPowerOf2()
    {
        // sbyte / 4 needs arithmetic shift right (preserving sign)
        var bytes = GetProgramBytes(
            """
            sbyte vel = -8;
            sbyte r = (sbyte)(vel / 4);
            pal_col(0, (byte)r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9F8", hex); // LDA #$F8 (-8 in two's complement)
    }

    [Fact]
    public void UshortAddSignedByte()
    {
        // actor.yy += actor.yvel/4 — mixing ushort and sbyte
        var bytes = GetProgramBytes(
            """
            ushort yy = 100;
            sbyte vel = -8;
            yy = (ushort)(yy + vel / 4);
            pal_col(0, (byte)yy);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9F8", hex); // LDA #$F8 (-8)
    }

    [Fact]
    public void ShiftRight()
    {
        // Shift right by constant — pyy >> 8 pattern for extracting high byte
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte hi = (byte)(x >> 2);
            pal_col(0, hi);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain two LSR A instructions (4A 4A)
        Assert.Contains("4A4A", hex);
    }

    [Fact]
    public void ShiftLeft()
    {
        // Shift left by constant — x << 3 pattern for multiply by 8
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x << 3);
            pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain three ASL A instructions (0A 0A 0A)
        Assert.Contains("0A0A0A", hex);
    }

    [Fact]
    public void Ushort16BitMultiply()
    {
        // byte * 8 → ushort (overflow to 16-bit)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort result = (ushort)(x * 8);
            pal_col(0, (byte)(result >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain ROL $17 (zero page, opcode 26 17) for 16-bit shift
        Assert.Contains("2617", hex);
        // Should contain TXA (8A) to extract high byte via >> 8
        Assert.Contains("8A", hex);
    }

    [Fact]
    public void LocalTimesConstant_PreservesLocalLoad()
    {
        // Regression test: ldloc;ldc;mul must keep the LDA $local instruction.
        // A previous bug had RemoveLastInstructions(2) which removed both
        // the LDA #constant AND the LDA $local, leaving ASL shifts with
        // a stale accumulator value.
        //
        // The pal_col call between gap's definition and usage forces the
        // compiler to store gap to a local and reload it, producing the
        // ldloc;ldc;mul IL pattern that triggered the bug.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte gap = (byte)(x & 7);
            pal_col(0, gap);
            ushort offset = (ushort)(gap * 16);
            pal_col(1, (byte)(offset >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);

        // 16-bit multiply by 16 uses LDX #0 (A200), STX $17 (8617), then ASL A + ROL $17
        string shiftSetup = "A20086170A2617";
        int setupIdx = hex.IndexOf(shiftSetup);
        Assert.True(setupIdx >= 6, "Could not find LDX #0 + STX $17 + ASL A + ROL $17 sequence");

        // The 3 bytes (6 hex chars) immediately before the shift setup must be
        // LDA Absolute (AD xx xx) — the local variable reload.
        // If RemoveLastInstructions removes too many, it would be a JSR (20 xx xx) instead.
        string instrBefore = hex.Substring(setupIdx - 6, 2);
        Assert.Equal("AD", instrBefore);
    }

    [Fact]
    public void Ushort16BitAdd()
    {
        // ushort + byte constant with carry propagation
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort result = (ushort)(x * 8 + 16);
            pal_col(0, (byte)(result >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain CLC (18) + ADC #10 (69 10) for 16-bit add
        Assert.Contains("186910", hex);
        // Should contain BCC (90) for carry propagation
        Assert.Contains("90", hex);
    }

    [Fact]
    public void BytePlusUshortConstant()
    {
        // byte + ushort constant (> 255) produces 16-bit result
        var bytes = GetProgramBytes(
            """
            byte lo = rand8();
            ushort val = (ushort)(lo + 256);
            pal_col(0, (byte)val);
            pal_col(1, (byte)(val >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void BcdAddWithRuntimeFirstArg()
    {
        // Bug: bcd_add(score, 1) where score is ushort — the first argument
        // needs pushax (16-bit push), and the second argument needs X=0.
        // Without the fix, pusha (1-byte) is used or X is garbage.
        var bytes = GetProgramBytes(
            """
            ushort score = 0;
            score = bcd_add(score, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"BcdAddRuntime hex: {hex}");
        // Must contain LDX #$00 (A200) and LDA #$01 (A901) for the constant arg
        Assert.Contains("A200", hex); // LDX #$00 (clear X)
        Assert.Contains("A901", hex); // LDA #$01 (second arg)

        // Verify the sequence for second arg: LDX #$00 then LDA #$01 then JSR bcd_add
        // A2 00 A9 01 20 ?? ??
        bool patternFound = false;
        for (int i = 0; i + 6 < bytes!.Length; i++)
        {
            if (bytes[i] == 0xA2 && bytes[i + 1] == 0x00      // LDX #$00
                && bytes[i + 2] == 0xA9 && bytes[i + 3] == 0x01 // LDA #$01
                && bytes[i + 4] == 0x20)                        // JSR bcd_add
            {
                patternFound = true;
                break;
            }
        }
        Assert.True(patternFound, "Expected LDX #$00; LDA #$01; JSR bcd_add pattern not found.");
    }

    [Fact]
    public void Multiply_NonPowerOf2_3()
    {
        // Runtime val * 3 should use the general 8x8 multiply loop
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x * 3);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Multiply_NonPowerOf2_3 hex: {hex}");

        // General multiply loop: LDX #$08 (8 bits), LSR TEMP2, BCC, CLC, ADC TEMP
        Assert.Contains("A208", hex);     // LDX #$08
        Assert.Contains("4619", hex);     // LSR $19 (TEMP2)
    }

    [Fact]
    public void Multiply_NonPowerOf2_5()
    {
        // Runtime val * 5 should use the general 8x8 multiply loop
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x * 5);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Multiply_NonPowerOf2_5 hex: {hex}");

        // General multiply loop pattern: LDX #$08, LSR TEMP2
        Assert.Contains("A208", hex);     // LDX #$08
        Assert.Contains("4619", hex);     // LSR $19 (TEMP2)
    }

    [Fact]
    public void Division_RuntimeDividend()
    {
        // Runtime dividend / constant divisor should emit repeated subtraction
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x / 10);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Division_RuntimeDividend hex: {hex}");

        // Repeated subtraction: LDX #$FF, SEC, INX, SBC #$0A, BCS
        Assert.Contains("A2FF", hex);     // LDX #$FF
        Assert.Contains("38", hex);       // SEC
        Assert.Contains("E90A", hex);     // SBC #$0A (divisor 10)
    }
}
