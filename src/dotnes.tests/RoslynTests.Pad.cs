using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RoslynTests_Pad : RoslynTests
{
    public RoslynTests_Pad(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void PadPollUpDown_UsesCorrectAndImmediateOperands()
    {
        // Regression: climber had PAD_UP=0x08 (START) and PAD_DOWN=0x04 (SELECT).
        // Correct values are PAD.UP=0x10 and PAD.DOWN=0x20.
        // Verify the AND immediate operands in the emitted 6502.
        var bytes = GetProgramBytes(
            """
            byte y = 100;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                if ((pad & PAD.UP) != 0) y--;
                if ((pad & PAD.DOWN) != 0) y++;
                oam_spr(40, y, 0xD8, 0, 0);
            }
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PadUpDown hex: {hex}");

        // AND #$10 for PAD.UP (29 10), NOT AND #$08
        Assert.Contains("2910", hex);
        Assert.DoesNotContain("2908", hex);
        // AND #$20 for PAD.DOWN (29 20), NOT AND #$04
        Assert.Contains("2920", hex);
        // 2904 could appear as part of addresses, so just verify 2910/2920 are present
    }

    [Fact]
    public void PadPollAndRand8_IndependentAndOperations()
    {
        // Regression: the AND handler had a _padPollResultAvailable flag that was never
        // cleared after non-pad_poll calls, so AND operations on rand8() results would
        // reload the pad_poll address instead of using the correct local. Enemy movement
        // read player's joy instead of its own random byte.
        //
        // This test verifies that after rand8() (which clears _padPollResultAvailable),
        // subsequent AND operations don't emit a pad reload address. The intervening
        // code between rand8() and its AND check forces Roslyn to use stloc/ldloc
        // instead of dup optimization.
        var bytes = GetProgramBytes(
            """
            byte y = 100;
            byte x = 50;
            byte ai = 0;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD joy = pad_poll(0);
                if ((joy & PAD.LEFT) != 0) x--;
                if ((joy & PAD.RIGHT) != 0) x++;
                byte ej = rand8();
                ai = (byte)(ej & 3);
                oam_spr(x, y, ai, 0, 0);
                if ((ej & (byte)PAD.LEFT) != 0) y--;
            }
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PadPollAndRand8 hex: {hex}");

        // The pad reload address is the first STA after pad_poll JSR.
        // Find LDA #$00; JSR xxxx; STA xxxx pattern (pad_poll(0)).
        int padStaIdx = -1;
        ushort padReloadAddr = 0;
        for (int i = 0; i < bytes.Length - 8; i++)
        {
            if (bytes[i] == 0xA9 && bytes[i + 1] == 0x00 && bytes[i + 2] == 0x20
                && bytes[i + 5] == 0x8D)
            {
                padStaIdx = i + 5;
                padReloadAddr = (ushort)(bytes[i + 6] | (bytes[i + 7] << 8));
                break;
            }
        }
        Assert.True(padStaIdx > 0, "Could not find pad_poll STA pattern");
        _logger.WriteLine($"Pad reload address: ${padReloadAddr:X4}");

        // Find all AND #$40 (PAD.LEFT = 0x40) instructions
        var andPositions = new List<int>();
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == 0x29 && bytes[i + 1] == 0x40)
                andPositions.Add(i);
        }
        // At least 2 AND #$40: one for pad_poll, one for rand8
        Assert.True(andPositions.Count >= 2, $"Expected at least 2 AND #$40, found {andPositions.Count}");

        // The LAST AND #$40 should be for the rand8 ej variable.
        // It should NOT have LDA $padReloadAddr immediately before it.
        int lastAndPos = andPositions[^1];
        if (lastAndPos >= 3 && bytes[lastAndPos - 3] == 0xAD)
        {
            ushort loadAddr = (ushort)(bytes[lastAndPos - 2] | (bytes[lastAndPos - 1] << 8));
            Assert.NotEqual(padReloadAddr, loadAddr);
            _logger.WriteLine($"Last AND #$40 loads from ${loadAddr:X4} (not pad address ${padReloadAddr:X4})");
        }
        // If no LDA abs before last AND, the value comes from a different source (also correct)
    }

    [Fact]
    public void PadPollAndArrayElement_NoStaleReload()
    {
        // Regression for the !_runtimeValueInA guard in the AND handler:
        // After pad_poll(), _padPollResultAvailable stays true until the next Call.
        // If the program accesses an array element (ldelem.u1 — sets _runtimeValueInA)
        // and immediately ANDs the result, the AND handler must NOT reload the
        // pad_poll address. Without the guard, it would clobber A with a stale pad value.
        var bytes = GetProgramBytes(
            """
            byte[] data = new byte[] { 0x41, 0x42, 0x43, 0x44 };
            byte y = 100;
            byte x = 50;
            byte i = 0;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD joy = pad_poll(0);
                if ((joy & PAD.LEFT) != 0) x--;
                if ((joy & PAD.RIGHT) != 0) x++;
                if ((data[i] & 3) != 0) y++;
                i++;
                oam_spr(x, y, 0, 0, 0);
            }
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PadPollAndArrayElement hex: {hex}");

        // Find pad_poll reload address: LDA #$00; JSR xxxx; STA xxxx
        ushort padReloadAddr = 0;
        for (int j = 0; j < bytes.Length - 8; j++)
        {
            if (bytes[j] == 0xA9 && bytes[j + 1] == 0x00 && bytes[j + 2] == 0x20
                && bytes[j + 5] == 0x8D)
            {
                padReloadAddr = (ushort)(bytes[j + 6] | (bytes[j + 7] << 8));
                break;
            }
        }
        Assert.NotEqual((ushort)0, padReloadAddr);
        _logger.WriteLine($"Pad reload address: ${padReloadAddr:X4}");

        // Find AND #$03 instruction (the array element mask)
        int andPos = -1;
        for (int j = 0; j < bytes.Length - 1; j++)
        {
            if (bytes[j] == 0x29 && bytes[j + 1] == 0x03) // AND #$03
            {
                andPos = j;
                break;
            }
        }
        Assert.True(andPos >= 0, "Could not find AND #$03 in output");

        // The AND #$03 should NOT be preceded by LDA $padReloadAddr.
        // Without the !_runtimeValueInA guard, the AND handler emits:
        //   LDA padReloadAddr; AND #$03  (clobbering the array element in A)
        // With the guard, A keeps the array element value loaded by ldelem.u1.
        if (andPos >= 3 && bytes[andPos - 3] == 0xAD) // LDA Absolute
        {
            ushort loadAddr = (ushort)(bytes[andPos - 2] | (bytes[andPos - 1] << 8));
            Assert.NotEqual(padReloadAddr, loadAddr);
            _logger.WriteLine($"AND #$03 preceded by LDA ${loadAddr:X4} (not stale pad address ${padReloadAddr:X4})");
        }
        else
        {
            _logger.WriteLine($"AND #$03 at offset {andPos}: no stale pad reload (correct)");
        }
    }

    [Fact]
    public void PadPressed_ProducesSameCodeAsManualAnd()
    {
        // pad_pressed(pad, PAD.LEFT) should produce identical 6502 code
        // to the manual (pad & PAD.LEFT) != 0 pattern.
        var manualBytes = GetProgramBytes(
            """
            byte x = 40;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                if ((pad & PAD.LEFT) != 0) x--;
                if ((pad & PAD.RIGHT) != 0) x++;
                oam_spr(x, 40, 0xD8, 0, 0);
            }
            """);
        Assert.NotNull(manualBytes);

        var helperBytes = GetProgramBytes(
            """
            byte x = 40;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                if (pad_pressed(pad, PAD.LEFT)) x--;
                if (pad_pressed(pad, PAD.RIGHT)) x++;
                oam_spr(x, 40, 0xD8, 0, 0);
            }
            """);
        Assert.NotNull(helperBytes);

        var manualHex = Convert.ToHexString(manualBytes);
        var helperHex = Convert.ToHexString(helperBytes);
        _logger.WriteLine($"Manual: {manualHex}");
        _logger.WriteLine($"Helper: {helperHex}");

        // Both should produce byte-identical 6502 output
        Assert.Equal(manualBytes, helperBytes);
    }

    [Fact]
    public void PadPressed_MultipleButtons()
    {
        // Verify pad_pressed emits correct AND immediate for multiple button checks
        var bytes = GetProgramBytes(
            """
            byte x = 100;
            byte y = 100;
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                if (pad_pressed(pad, PAD.UP)) y--;
                if (pad_pressed(pad, PAD.DOWN)) y++;
                if (pad_pressed(pad, PAD.LEFT)) x--;
                if (pad_pressed(pad, PAD.RIGHT)) x++;
                oam_spr(x, y, 0xD8, 0, 0);
            }
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PadPressed_MultipleButtons hex: {hex}");

        // AND #$10 for PAD.UP
        Assert.Contains("2910", hex);
        // AND #$20 for PAD.DOWN
        Assert.Contains("2920", hex);
        // AND #$40 for PAD.LEFT
        Assert.Contains("2940", hex);
        // AND #$80 for PAD.RIGHT
        Assert.Contains("2980", hex);
    }

    [Fact]
    public void PadDpadX()
    {
        // pad_dpad_x returns -1 (LEFT), +1 (RIGHT), or 0
        var bytes = GetProgramBytes(
            """
            byte x = 128;
            pal_col(0, 0);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                x = (byte)(x + pad_dpad_x(pad));
                pal_col(0, x);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain AND #$40 (PAD.LEFT mask)
        Assert.Contains("2940", hex);
        // Should contain AND #$80 (PAD.RIGHT mask)
        Assert.Contains("2980", hex);
        // Should contain LDA #$FF (-1)
        Assert.Contains("A9FF", hex);
        // Should contain LDA #$01 (+1)
        Assert.Contains("A901", hex);
    }

    [Fact]
    public void PadDpadY()
    {
        // pad_dpad_y returns -1 (UP), +1 (DOWN), or 0
        var bytes = GetProgramBytes(
            """
            byte y = 128;
            pal_col(0, 0);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                y = (byte)(y + pad_dpad_y(pad));
                pal_col(0, y);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain AND #$10 (PAD.UP mask)
        Assert.Contains("2910", hex);
        // Should contain AND #$20 (PAD.DOWN mask)
        Assert.Contains("2920", hex);
        // Should contain LDA #$FF (-1)
        Assert.Contains("A9FF", hex);
        // Should contain LDA #$01 (+1)
        Assert.Contains("A901", hex);
    }

    [Fact]
    public void PadDpadXAndY()
    {
        // Both pad_dpad_x and pad_dpad_y used together
        var bytes = GetProgramBytes(
            """
            byte x = 128;
            byte y = 128;
            pal_col(0, 0);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                x = (byte)(x + pad_dpad_x(pad));
                y = (byte)(y + pad_dpad_y(pad));
                oam_spr(x, y, 0xD8, 0, 0);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Both X direction masks
        Assert.Contains("2940", hex); // PAD.LEFT
        Assert.Contains("2980", hex); // PAD.RIGHT
        // Both Y direction masks
        Assert.Contains("2910", hex); // PAD.UP
        Assert.Contains("2920", hex); // PAD.DOWN
        // x + pad_dpad_x(pad) must use CLC; ADC TEMP ($17), not ADC #$00
        Assert.Contains("186517", hex); // CLC; ADC $17 (TEMP)
        Assert.DoesNotContain("186900", hex); // CLC; ADC #$00 would be wrong
    }

    [Fact]
    public void PadDpadX_WithPadState()
    {
        // pad_dpad_x works with pad_state (not just pad_poll).
        // The intrinsic saves A to its own reload slot, so it doesn't
        // depend on _padReloadAddress being set by pad_poll.
        var bytes = GetProgramBytes(
            """
            byte x = 128;
            pal_col(0, 0);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad = pad_poll(0);
                PAD state = pad_state(0);
                x = (byte)(x + pad_dpad_x(state));
                pal_col(0, x);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain both direction masks
        Assert.Contains("2940", hex); // PAD.LEFT
        Assert.Contains("2980", hex); // PAD.RIGHT
        // Should contain LDA #$FF (-1) and LDA #$01 (+1)
        Assert.Contains("A9FF", hex);
        Assert.Contains("A901", hex);
        // x + pad_dpad_x(state) must use CLC; ADC TEMP ($17), not ADC #$00
        Assert.Contains("186517", hex); // CLC; ADC $17 (TEMP)
    }

    [Fact]
    public void PadDpadX_MultiPad()
    {
        // Two pads polled; pad_dpad_x called on the first (not the most recent).
        // The intrinsic must reload from its own saved copy, not _padReloadAddress
        // which points to the second pad_poll result.
        var bytes = GetProgramBytes(
            """
            byte x0 = 128;
            byte x1 = 128;
            pal_col(0, 0);
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                PAD pad0 = pad_poll(0);
                PAD pad1 = pad_poll(1);
                x0 = (byte)(x0 + pad_dpad_x(pad0));
                x1 = (byte)(x1 + pad_dpad_x(pad1));
                pal_col(0, x0);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Two separate pad_dpad_x intrinsics, each with direction masks
        // Count occurrences of AND #$40 (PAD.LEFT) — should appear twice
        int leftCount = 0;
        int idx = 0;
        while ((idx = hex.IndexOf("2940", idx)) >= 0) { leftCount++; idx += 4; }
        Assert.Equal(2, leftCount);
    }
}
