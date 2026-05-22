using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class StackTrackingTests : RoslynTests
{
    public StackTrackingTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void RuntimeValueInA_ThenWordLocal_EmitsPusha()
    {
        // Regression: when _runtimeValueInA is true (e.g., from nesclock())
        // followed by ldloc of a ushort local, must emit JSR pusha to save A
        // before the word local clobbers it.
        using var transpiler = BuildProgram(
            """
            ushort total = 100;
            byte val = nesclock();
            ushort result = (ushort)(val + total);
            pal_col(0, (byte)result);
            ppu_on_all();
            while (true) ;
            """, out var program);

        // Scan all blocks for JSR pusha
        bool hasPusha = program.Blocks.Any(b =>
            b.InstructionsWithLabels.Any(il =>
                il.Instruction.Opcode == Opcode.JSR &&
                il.Instruction.Operand is LabelOperand lbl && lbl.Label == "pusha"));
        Assert.True(hasPusha, "Expected JSR pusha to save A before word local load. " +
            $"Blocks: {string.Join(", ", program.Blocks.Select(b => b.Label ?? "(no label)"))}");
    }

    [Fact]
    public void SubtractRuntimeFromPushaConstant()
    {
        // Pattern from climber: byte rowy = (byte)(59 - (byte)(rh % 60))
        // The Sub handler must pop the pusha'd 59 and compute 59 - A.
        var bytes = GetProgramBytes(
            """
            byte rh = 5;
            byte rowy = (byte)((byte)59 - (byte)(rh % (byte)60));
            pal_col(0, rowy);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SubPusha hex: {hex}");
        // Must contain JSR popa (20 xx xx) to pop the pusha'd constant
        // and SBC via TEMP2 ($19) — STA $19 (8519) then SBC $19 (E519)
        Assert.Contains("8519", hex); // STA TEMP2 (save runtime)
        Assert.Contains("E519", hex); // SBC TEMP2 (59 - runtime)
    }

    [Fact]
    public void AddWithPushaFunctionArgDoesNotConsumePusha()
    {
        // Regression: in `NTADR_A(1, (byte)(row + 10))`, the first arg (1) was pusha'd
        // for the function call, and the `row + 10` Add incorrectly consumed the pusha'd
        // value (1) instead of using the actual operand (10), giving `1 + row` not `row + 10`.
        var bytes = GetProgramBytes(
            """
            for (byte row = 0; row < 4; row++)
            {
                ushort addr = NTADR_A(1, (byte)(row + 10));
                vrambuf_flush();
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"AddWithPusha hex: {hex}");
        // The constant 10 (0x0A) must appear as ADC #$0A (690A) — the add of row+10.
        // If the pusha bug is present, we'd see ADC $TEMP2 instead (no 0x0A constant).
        Assert.Contains("690A", hex); // CLC; ADC #$0A
        // After the add, NTADR_A args must be set up correctly:
        // STA $19 (TEMP2=y), JSR popa, STA $17 (TEMP=x), LDA $19 (restore y)
        Assert.Contains("8519", hex); // STA TEMP2
        Assert.Contains("8517", hex); // STA TEMP (x from popa)
        Assert.Contains("A519", hex); // LDA TEMP2 (restore y)
    }

    [Fact]
    public void DupCascade_InterveningStloc_ReloadsFromTemp()
    {
        // Regression: In the climber sample, the pattern:
        //   byte st = actor_state[ai]; byte isEnemy = actor_name[ai];
        //   if (st == 1) { ... } if (st == 2) { ... }
        // The C# compiler (Release) emits IL that loads isEnemy BETWEEN st and the
        // dup cascade. HandleLdelemU1 saves st to TEMP ($17), but then stloc (for
        // isEnemy) clears _runtimeValueInA. Without the fix, the dup handler can't
        // start the cascade and CMP compares isEnemy instead of st.
        var bytes = GetProgramBytes(
            """
            byte[] states = new byte[8];
            byte[] names = new byte[8];
            states[0] = 1;
            names[0] = 5;
            for (byte i = 0; i < 4; i++)
            {
                byte st = states[i];
                byte nm = names[i];
                if (st == 1)
                {
                    pal_col(0, nm);
                }
                if (st == 2)
                {
                    pal_col(1, nm);
                }
            }
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"DupCascade hex: {hex}");

        // STA $17 (85 17) — save st to TEMP before nm load clobbers A
        Assert.Contains("8517", hex);

        // LDA $17 (A5 17) — reload st from TEMP before the cascade comparison
        Assert.Contains("A517", hex);

        // The sequence LDA $17 → CMP #$01 must appear (reload st, then compare)
        Assert.Contains("A517C901", hex);
    }

    [Fact]
    public void LdlocStloc_LocalToLocalCopy()
    {
        // Regression test: `lx = ladx` (local-to-local copy) must load from ladx's
        // runtime address, not use a stale constant 0. Previously WriteStloc's scalar
        // branch would RemoveLastInstructions(1) even when the previous LDA was Absolute
        // (from WriteLdloc), replacing `LDA $addr` with `LDA #$00`.
        var bytes = GetProgramBytes("""
            byte[] arr = new byte[8];
            byte[] lad = new byte[8];
            for (byte i = 0; i < 8; i++)
            {
                arr[i] = rand8();
                lad[i] = rand8();
            }
            byte idx = 0;
            byte lx = 0;
            byte ladx = (byte)(lad[idx] * 16);
            if ((byte)(arr[idx] - ladx) < 16) lx = ladx;
            if (lx != 0)
            {
                pal_col(0, lx);
            }
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdlocStloc hex: {hex}");

        // After BCS (B0), the body must load ladx from its Absolute address (AD xx xx)
        // then store to lx (8D xx xx). It must NOT be LDA #$00 (A9 00).
        // Find the BCS instruction after CMP #$10 (C9 10 B0)
        int cmpIdx = hex.IndexOf("C910B0");
        Assert.True(cmpIdx >= 0, "CMP #$10; BCS pattern not found");

        // The BCS operand is 1 byte (2 hex chars) after B0
        int bodyStart = cmpIdx + 8; // past "C910B0xx"
        string bodyHex = hex.Substring(bodyStart, 6); // first 3 bytes of body

        // Must be LDA Absolute (AD xx xx), not LDA Immediate (A9 00)
        Assert.StartsWith("AD", bodyHex);
        Assert.False(bodyHex.StartsWith("A9"), "Bug: local-to-local copy emitted LDA #constant instead of LDA $address");
    }

    [Fact]
    public void DupShr_UshortLoHiByteExtraction()
    {
        // Regression test: dup + conv_u1 + stloc (lo byte) + ldc_i4 8 + shr + conv_u1 + stloc (hi byte)
        // The transpiler must emit TXA (8A) for the hi byte (>> 8) instead of 8 LSR instructions
        // or LDA #$00. The ushort result of mul*8+16 has lo in A and hi in X.
        var bytes = GetProgramBytes("""
            byte pf = (byte)pad_poll(0);
            ushort floor_yy = (ushort)(pf * 8 + 16);
            byte fyy_lo = (byte)floor_yy;
            byte fyy_hi = (byte)(floor_yy >> 8);
            pal_col(0, fyy_lo);
            pal_col(1, fyy_hi);
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"DupShr hex: {hex}");

        // After the 16-bit mul (ASL/ROL) + ADC #$10 + BCC/INX sequence,
        // the lo byte is stored with STA $addr1, then TXA (8A) transfers
        // the hi byte from X to A, followed by STA $addr2.
        // Pattern: STA abs (8D xx xx) + TXA (8A) + STA abs (8D xx xx)
        int txaIdx = hex.IndexOf("8A8D");
        Assert.True(txaIdx >= 0, "TXA + STA pattern not found — hi byte extraction may be wrong");

        // The 3 bytes before TXA should be STA Absolute (8D xx xx)
        Assert.True(txaIdx >= 6, "Not enough bytes before TXA");
        string beforeTxa = hex.Substring(txaIdx - 6, 2);
        Assert.Equal("8D", beforeTxa); // STA Absolute before TXA
    }

    [Fact]
    public void DupCascade_StaleFlag_DoesNotCorruptSubsequentDup()
    {
        // Regression test: a dup cascade (dup + ldc + beq/bne) sets _dupCascadeActive,
        // which must be cleared before a subsequent non-cascade dup (e.g., lo/hi extraction).
        // Without the fix, the stale flag causes the later dup to emit LDA $18 (TEMP_HI),
        // corrupting the accumulator.
        var bytes = GetProgramBytes("""
            byte state = (byte)pad_poll(0);
            // dup cascade: if-else chain
            if (state == 1) pal_col(0, 0x10);
            else if (state == 2) pal_col(0, 0x20);

            // Non-cascade dup for lo/hi byte extraction
            byte pf = (byte)pad_poll(0);
            ushort val = (ushort)(pf * 8 + 16);
            byte lo = (byte)val;
            byte hi = (byte)(val >> 8);
            pal_col(1, lo);
            pal_col(2, hi);
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"DupCascadeStale hex: {hex}");

        // After the cascade, the lo/hi extraction must use TXA (8A) for the hi byte,
        // NOT LDA $18 (A5 18) from the stale cascade path.
        // Verify TXA appears (correct hi byte extraction)
        int txaIdx = hex.IndexOf("8A8D");
        Assert.True(txaIdx >= 0, "TXA + STA pattern not found after cascade — stale _dupCascadeActive may corrupt dup");
    }

    [Fact]
    public void ByteUshortVarExtraction_NoPushaLeak()
    {
        // Regression: (byte)ushort_var patterns inside stelem_i1 must not leave
        // unmatched JSR pusha calls in the generated code. Each pusha must have
        // a corresponding popa or incsp1, otherwise the cc65 stack leaks.
        // This mimics the climber sample's draw_entire_stage init pattern with
        // preceding array stores that set LastLDA = true before word local loads.
        using var transpiler = BuildProgram(
            """
            byte[] arr_lo = new byte[8];
            byte[] arr_hi = new byte[8];
            byte[] arr_state = new byte[8];
            byte[] arr_x = new byte[8];
            byte[] ypos = new byte[8];
            ypos[0] = 10; ypos[1] = 20; ypos[2] = 30; ypos[3] = 40;
            for (byte i = 0; i < 4; i++)
            {
                arr_state[i] = 1;
                arr_x[i] = rand8();
                byte aypos = ypos[i];
                ushort ayy = (ushort)(aypos * 8 + 16);
                arr_lo[i] = (byte)ayy;
                arr_hi[i] = (byte)(ayy >> 8);
            }
            ppu_on_all();
            while (true) ;
            """, out var program);

        // Count JSR pusha and JSR popa/incsp1 in the main block
        var mainBlock = program.GetBlock("main");
        Assert.NotNull(mainBlock);

        int pushaCount = 0, popaCount = 0;
        foreach (var (instruction, _) in mainBlock.InstructionsWithLabels)
        {
            if (instruction.Opcode == Opcode.JSR && instruction.Operand is LabelOperand lbl)
            {
                if (lbl.Label == "pusha") pushaCount++;
                if (lbl.Label == "popa" || lbl.Label == "incsp1") popaCount++;
            }
        }

        _logger.WriteLine($"Main block: pusha={pushaCount}, popa/incsp1={popaCount}");

        // The (byte)ushort_var stelem pattern must not leave any unmatched pusha
        // calls in the main block — HandleStelemI1 removes and re-emits the
        // entire sequence, so every pusha must be balanced by popa or incsp1.
        Assert.True(pushaCount <= popaCount,
            $"Unmatched pusha calls detected: pusha={pushaCount}, popa/incsp1={popaCount}. " +
            "Each pusha must be balanced by popa or incsp1 to prevent cc65 stack leaks.");
    }

    [Fact]
    public void ByteUshortVarExtraction_Stloc_NoPushaLeak()
    {
        // Test the stloc variant: byte lo = (byte)ushort_var where the result is
        // stored to a byte local (not an array). This goes through WriteStloc
        // instead of HandleStelemI1.
        using var transpiler = BuildProgram(
            """
            byte[] ypos = new byte[8];
            ypos[0] = 10;
            for (byte f = 0; f < 4; f++)
            {
                byte pypos = ypos[f];
                ushort floor_yy = (ushort)(pypos * 8 + 16);
                byte fyy_lo = (byte)floor_yy;
                byte fyy_hi = (byte)(floor_yy >> 8);
                pal_col(fyy_lo, fyy_hi);
            }
            ppu_on_all();
            while (true) ;
            """, out var program);

        var mainBlock = program.GetBlock("main");
        Assert.NotNull(mainBlock);

        int pushaCount = 0, popaCount = 0;
        foreach (var (instruction, _) in mainBlock.InstructionsWithLabels)
        {
            if (instruction.Opcode == Opcode.JSR && instruction.Operand is LabelOperand lbl)
            {
                if (lbl.Label == "pusha") pushaCount++;
                if (lbl.Label == "popa" || lbl.Label == "incsp1") popaCount++;
            }
        }

        _logger.WriteLine($"Stloc variant - Main block: pusha={pushaCount}, popa/incsp1={popaCount}");

        Assert.True(pushaCount <= popaCount,
            $"Unmatched pusha calls detected: pusha={pushaCount}, popa/incsp1={popaCount}. " +
            "Each pusha must be balanced by popa or incsp1 to prevent cc65 stack leaks.");
    }
}
