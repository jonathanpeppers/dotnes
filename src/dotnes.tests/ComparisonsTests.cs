using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ComparisonsTests : RoslynTests
{
    public ComparisonsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void SbyteComparison()
    {
        // sbyte comparison: if (vel < 0) should branch correctly
        var bytes = GetProgramBytes(
            """
            sbyte vel = -2;
            if (vel < 0) pal_col(0, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FE", hex); // LDA #$FE (-2 in two's complement)
    }

    [Fact]
    public void UshortComparison()
    {
        // 16-bit comparison: ushort > constant
        var bytes = GetProgramBytes(
            """
            ushort yy = 200;
            if (yy > 100) pal_col(0, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Should compile without error (constant-folded)
        var hex = Convert.ToHexString(bytes);
        Assert.NotEmpty(hex);
    }

    [Fact]
    public void RuntimeComparison()
    {
        // Compare two runtime variables: for (byte i = 0; i < limit; i++)
        var bytes = GetProgramBytes(
            """
            byte limit = (byte)(rand8() % 4);
            for (byte i = 0; i < limit; i++)
            {
                pal_col(i, i);
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain CMP $addr (absolute mode, opcode CD) for runtime comparison
        Assert.Contains("CD", hex);
    }

    [Fact]
    public void BranchCompareWithIndexedArrayElement()
    {
        // Pattern from climber: if (dy < floor_height[f]) — compares local with array[index]
        // ldelem.u1 emits LDA array,X (AbsoluteX). EmitBranchCompare must convert to CMP array,X.
        var bytes = GetProgramBytes(
            """
            byte[] heights = new byte[20];
            heights[0] = 5;
            heights[1] = 3;
            byte dy = 2;
            for (byte f = 0; f < 20; f++)
            {
                if (dy < heights[f])
                {
                    oam_clear();
                    break;
                }
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"BranchCompareIndexed hex: {hex}");
        // Must contain CMP absolute,X (opcode DD) for comparing dy with heights[f]
        Assert.Contains("DD", hex);
        // Must NOT contain CMP #$00 (C900) which would mean constant comparison
        Assert.DoesNotContain("C900", hex);
    }

    [Fact]
    public void SubThenCompare_RuntimeSubNotStripped()
    {
        // Regression: EmitBranchCompare's default path blindly removed the last
        // instruction, assuming it was LDA #imm from WriteLdc. But when
        // _runtimeValueInA is true, WriteLdc returns without emitting LDA, so
        // the last instruction is the SBC from HandleAddSub. The fix checks
        // that the instruction being removed is actually LDA Immediate.
        //
        // Pattern: (byte)(arr[idx] - localVar) < 16
        // IL: ldelem.u1; ldloc localVar; sub; conv.u1; ldc.i4.s 16; bge.s
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte[] lad = new byte[4];
            for (byte i = 0; i < 4; i++)
            {
                arr[i] = rand8();
                lad[i] = rand8();
            }
            byte idx = 0;
            byte ladx = (byte)(lad[idx] * 16);
            byte check = (byte)(arr[idx] - ladx);
            if (check < 16)
            {
                pal_col(0, 0x30);
            }
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SubThenCompare hex: {hex}");

        // The SEC+SBC sequence (38 E5 18) must exist:
        // SEC=38, SBC ZeroPage=E5, TEMP+1=18
        // This ensures the subtraction wasn't stripped by EmitBranchCompare.
        Assert.Contains("38E518", hex);

        // After SBC, there should be CMP #$10 (C9 10) for the < 16 check
        Assert.Contains("C910", hex);

        // The full sequence: SEC; SBC $18; CMP #$10 (38 E5 18 C9 10)
        Assert.Contains("38E518C910", hex);
    }

    [Fact]
    public void ByteComparison_LessThanOrEqual255()
    {
        // Regression: if (x <= 0xFF) caused OverflowException because
        // Ble emits CMP #(value+1), and 255+1 overflows a byte.
        // The transpiler should emit an unconditional JMP (always true for bytes).
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            if (x <= 0xFF)
            {
                pal_col(0, x);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"ByteComparison_LessThanOrEqual255 hex: {hex}");

        // Should contain JMP (4C) for the always-true branch, not CMP #$00 (overflow)
        Assert.Contains("4C", hex);
    }

    [Fact]
    public void UshortLessThanConstant16Bit()
    {
        // 16-bit comparison: ushort local < 300 (0x012C)
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y < 300)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Ushort16BitLT hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 300 (hi=0x01)
        Assert.Contains("E001", hex);
        // Must contain CMP #$2C (C92C) for lo byte comparison against 300 (lo=0x2C)
        Assert.Contains("C92C", hex);
        // Must contain BCC (90) after CPX #$01 for the "less than" hi byte check
        Assert.Contains("E00190", hex);
    }

    [Fact]
    public void UshortEqualConstant16Bit()
    {
        // 16-bit equality: ushort local == 500 (0x01F4)
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y == 500)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Ushort16BitEQ hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 500 (hi=0x01)
        Assert.Contains("E001", hex);
        // Must contain CMP #$F4 (C9F4) for lo byte comparison against 500 (lo=0xF4)
        Assert.Contains("C9F4", hex);
    }

    [Fact]
    public void UshortNotEqualConstant16Bit()
    {
        // 16-bit inequality: ushort local != 1000 (0x03E8)
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y != 1000)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Ushort16BitNE hex: {hex}");

        // Must contain CPX #$03 (E003) for hi byte comparison against 1000 (hi=0x03)
        Assert.Contains("E003", hex);
        // Must contain CMP #$E8 (C9E8) for lo byte comparison against 1000 (lo=0xE8)
        Assert.Contains("C9E8", hex);
    }

    [Fact]
    public void UshortGreaterOrEqualConstant16Bit()
    {
        // 16-bit comparison: ushort local >= 256 (0x0100)
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y >= 256)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Ushort16BitGE hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 256 (hi=0x01)
        Assert.Contains("E001", hex);
        // Must contain CMP #$00 (C900) for lo byte comparison against 256 (lo=0x00)
        Assert.Contains("C900", hex);
    }

    [Fact]
    public void UshortLessThanSmallConstant()
    {
        // 16-bit local compared with small constant (fits in byte):
        // ushort local < 5 — must still emit 16-bit comparison because local is 16-bit.
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y < 5)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Ushort8BitLT hex: {hex}");

        // Must contain CPX #$00 (E000) for hi byte comparison (hi of 5 is 0x00)
        Assert.Contains("E000", hex);
        // Must contain CMP #$05 (C905) for lo byte comparison (lo of 5 is 0x05)
        Assert.Contains("C905", hex);
    }

    [Fact]
    public void UshortLessThan256_16BitComparison()
    {
        // Regression test: ushort local < 256 should emit 16-bit comparison
        // (256 exceeds byte range, so WriteLdc(ushort 256) is called).
        // Previously this threw "Branch comparison value 256 exceeds byte range."
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y < 256)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortLT256 hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 256 (hi=0x01)
        Assert.Contains("E001", hex);
        // Must contain CMP #$00 (C900) for lo byte comparison against 256 (lo=0x00)
        Assert.Contains("C900", hex);
    }

    [Fact]
    public void UshortLessThan256_WithIncrement()
    {
        // Regression test: ushort local incremented in a loop, then compared < 256.
        // This is closer to the scroll_yy pattern in the climber sample.
        var bytes = GetProgramBytes(
            """
            ushort scroll_yy = 0;
            pal_col(0, 0x30);
            ppu_on_all();
            while (true)
            {
                scroll_yy = (ushort)(scroll_yy + 1);
                if (scroll_yy < 256)
                {
                    pal_col(0, 0x20);
                }
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortLT256_Inc hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 256 (hi=0x01)
        Assert.Contains("E001", hex);
    }

    [Fact]
    public void UshortLessThan256_AfterArithmetic()
    {
        // Regression test: ushort local used in arithmetic, then compared < 256.
        // Tests that _ushortInAX survives through add + conv.u2 + stloc + ldloc.
        var bytes = GetProgramBytes(
            """
            ushort x = rand16();
            ushort y = (ushort)(x + 100);
            if (y < 256)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortLT256_Arith hex: {hex}");

        // Must contain CPX #$01 (E001) for hi byte comparison against 256 (hi=0x01)
        Assert.Contains("E001", hex);
    }

    [Fact]
    public void UshortLessThan512_16BitComparison()
    {
        // Test with a different boundary value (512 = 0x0200)
        var bytes = GetProgramBytes(
            """
            ushort y = rand16();
            if (y < 512)
            {
                pal_col(0, 0x30);
            }
            pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortLT512 hex: {hex}");

        // Must contain CPX #$02 (E002) for hi byte comparison against 512 (hi=0x02)
        Assert.Contains("E002", hex);
    }
}
