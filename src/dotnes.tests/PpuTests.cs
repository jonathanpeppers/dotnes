using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class PpuTests : RoslynTests
{
    public PpuTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void WaitvsyncEmitsJsr()
    {
        // waitvsync() should emit JSR to waitvsync subroutine
        using var transpiler = BuildProgram(
            """
            waitvsync();
            ppu_on_all();
            while (true) ;
            """, out var program);
        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        // Main block should start with JSR (opcode 0x20) for waitvsync call
        Assert.Equal(0x20, mainBlock[0]);

        // Verify the waitvsync block exists and has correct 6502 instructions
        var waitvsyncBlock = program.GetBlock("waitvsync");
        Assert.NotNull(waitvsyncBlock);
        // Expected: BIT $2002 (2C 02 20), BPL -5 (10 FB), RTS (60)
        Assert.Equal(3, waitvsyncBlock.Count); // 3 instructions
    }

    [Fact]
    public void NtadrResultStoredToUshortLocal()
    {
        // Pattern from climber: ushort addr; if (row < 2) addr = NTADR_A(1, row);
        // else addr = NTADR_C(1, row); vrambuf_put(addr, buf, 30);
        // The NTADR result must be stored to a ushort local and then
        // loaded back for vrambuf_put, with proper TEMP/TEMP2 setup.
        // The if/else forces Roslyn to allocate the ushort local.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            vrambuf_clear();
            set_vram_update(buf);
            for (byte row = 0; row < 4; row++)
            {
                ushort addr;
                if (row < 2)
                    addr = NTADR_A(1, row);
                else
                    addr = NTADR_C(1, row);
                vrambuf_put(addr, buf, 30);
                vrambuf_flush();
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NtadrLocal hex: {hex}");
        // Must contain STA $19 (TEMP2) from NTADR handler
        Assert.Contains("8519", hex);
        // Must contain STX $17 (TEMP) from NTADR handler
        Assert.Contains("8617", hex);
        // Must contain LDA TEMP2 (A519) in the stloc path — store lo byte to local
        Assert.Contains("A519", hex);
        // Must contain LDA TEMP (A517) in the stloc path — store hi byte to local
        Assert.Contains("A517", hex);
        // Must NOT contain ORA #$80 — vrambuf_put now uses horizontal (ORA $40 in subroutine)
        Assert.DoesNotContain("0980", hex);
    }

    [Fact]
    public void VrambufPutUsesHorizontalFlag()
    {
        // vrambuf_put(addr, buf, len) should NOT add $80 vertical flag to the VRAM address.
        // NTADR_A(1, 0) = $2001. Compile-time path stores addr_hi=$20 to TEMP.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            vrambuf_clear();
            set_vram_update(buf);
            vrambuf_put(NTADR_A(1, 0), buf, 30);
            vrambuf_flush();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"VrambufPutHorz hex: {hex}");
        // addr_hi = $20 stored to TEMP: LDA #$20, STA $17 → "A920" + "8517"
        Assert.Contains("A9208517", hex);
        // Must NOT contain LDA #$A0 (addr_hi | $80) which would mean vertical
        Assert.DoesNotContain("A9A08517", hex);
    }

    [Fact]
    public void VrambufPutVertUsesVerticalFlag()
    {
        // vrambuf_put_vert(addr, buf, len) should add $80 vertical flag.
        // NTADR_A(1, 4) = $2081. Compile-time path should store (addr_hi | $80) = $A0 to TEMP.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[26];
            vrambuf_clear();
            set_vram_update(buf);
            vrambuf_put_vert(NTADR_A(1, 4), buf, 26);
            vrambuf_flush();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"VrambufPutVert hex: {hex}");
        // addr_hi = $20 | $80 = $A0 stored to TEMP: LDA #$A0, STA $17
        Assert.Contains("A9A08517", hex);
    }

    [Fact]
    public void NtadrInsideLoopWithVrambufPut()
    {
        // Regression: NTADR_C inside a loop body with vrambuf_put causes the
        // backward scan for JSR pusha to match pushes from vrambuf_put instead
        // of the NTADR_C first arg, producing incorrect nametable addresses.
        var bytes = GetProgramBytes(
            """
            byte[] tile_row = new byte[4];
            byte nx = 5;
            byte ny = 3;
            vrambuf_clear();
            set_vram_update(tile_row);
            for (byte k = 0; k < 4; k++)
            {
                ushort addr = NTADR_C(nx, ny);
                vrambuf_put(addr, tile_row, 4);
                ny = (byte)(ny + 1);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NtadrLoopVrambuf hex: {hex}");
        // NTADR_C handler must correctly resolve x=nx. The nametable_c subroutine
        // must be called (JSR nametable_c). If the backward scan matched an
        // unrelated pusha, the codegen would be incorrect or crash.
        // TEMP = x (nx), A = y (ny) before calling nametable_c.
        // STA $17 (TEMP = x) must appear before JSR nametable_c
        Assert.Contains("8517", hex); // STA TEMP (x)
    }

    [Fact]
    public void NtadrExpressionAfterMultiArgCall()
    {
        // Regression: when a multi-arg function (e.g. pal_col) precedes NTADR_C
        // with a runtime expression y arg, the yIsExpression backward scan could
        // match pal_col's pusha instead of NTADR_C's. The fix stops the scan
        // when a non-helper JSR is encountered.
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x30);
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
        _logger.WriteLine($"NtadrAfterMultiArg hex: {hex}");
        // The constant 10 (0x0A) must appear as ADC #$0A (690A) — the add of row+10.
        Assert.Contains("690A", hex);
        // NTADR args must be set up correctly with TEMP/TEMP2
        Assert.Contains("8519", hex); // STA TEMP2
        Assert.Contains("8517", hex); // STA TEMP (x from popa)
    }

    [Fact]
    public void NtadrWithBothVariableArgs()
    {
        // Regression: NTADR_A(x1, y1) with byte local args. Roslyn (Release)
        // inlines x1 to a constant but keeps y1 as a local, emitting an
        // stloc *between* the two NTADR args:
        //   ldc.i4.2; ldc.i4.2; stloc.0; ldloc.0; call NTADR_A
        // The NTADR handler's backward scan from `pusha` was wiping out
        // the stloc pair (LDA #val, STA $addr), so the re-emitted
        // LDA $addr (the y load) read uninitialized memory.
        var bytes = GetProgramBytes(
            """
            byte x1 = 2;
            byte y1 = 2;
            vram_adr(NTADR_A(x1, y1));
            vram_write("B");
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NtadrWithBothVariableArgs hex: {hex}");
        // Required generated sequence at the NTADR_A call site, in order
        // (Roslyn eliminates x1; y1 is the only local at $0325):
        //   LDA #$02    A902     — x constant
        //   STA $17     8517     — STA TEMP (x), emitted first so the
        //                          NTADR call's pending label anchors here
        //   LDA #$02    A902     — y init value
        //   STA $0325   8D2503   — stloc y1
        //   LDA $0325   AD2503   — ldloc y1 (NTADR's y arg)
        // Asserting the full ordered substring guarantees the stloc is
        // preserved AND that the subsequent ldloc reads the same address
        // it was just stored to.
        Assert.Contains("A9028517A9028D2503AD2503", hex);
    }

    [Fact]
    public void VramFillAttributeTable_CorrectAddresses()
    {
        // Regression: climber attribute table fill used $27C0 (nametable B) instead
        // of $2BC0 (nametable C). Verify vram_adr + vram_fill emit correct addresses.
        var bytes = GetProgramBytes(
            """
            vram_adr(0x2000);
            vram_fill(0, 0x1000);
            vram_adr(0x23C0);
            vram_fill(0x55, 64);
            vram_adr(0x2BC0);
            vram_fill(0x55, 64);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"VramFillAttr hex: {hex}");

        // vram_adr loads high byte into X, low byte into A
        // vram_adr($23C0): LDX #$23 (A223), LDA #$C0 (A9C0)
        Assert.Contains("A223", hex);
        Assert.Contains("A9C0", hex);
        // vram_adr($2BC0): LDX #$2B (A22B) — NOT $27 (nametable B)
        Assert.Contains("A22B", hex);
        Assert.DoesNotContain("A227", hex);
        // vram_fill value 0x55: LDA #$55 (A955)
        Assert.Contains("A955", hex);
    }

    [Fact]
    public void VramReadEmitsPushaxAndSize()
    {
        // vram_read(byte[], uint) must emit the same pushax + size calling convention
        // as vram_write: pointer pushed via pushax, size in A:X, then JSR vram_read.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[4] { 0x01, 0x02, 0x03, 0x04 };
            vram_adr(NTADR_A(2, 2));
            vram_read(buf, 4);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"VramRead hex: {hex}");

        // The sequence for vram_read(buf, 4) via WriteLdloc should be:
        //   LDA #lo(buf)   -> A9 xx      (byte array label low byte)
        //   LDX #hi(buf)   -> A2 xx      (byte array label high byte)
        //   JSR pushax     -> 20 xx xx   (push pointer onto cc65 stack)
        //   LDX #$00       -> A2 00      (size high byte from local.Value)
        //   LDA #$04       -> A9 04      (size low byte = 4)
        //   ...
        //   JSR vram_read  -> 20 xx xx

        // Verify JSR pushax (opcode 0x20) appears before the size load sequence.
        // The full pattern is: JSR pushax (20 xx xx), LDX #$00 (A2 00), LDA #$04 (A9 04)
        // We match "20" + 4 hex chars (address) + "A200A904" to ensure pushax precedes the size.
        int jsrIdx = hex.IndexOf("A200A904");
        Assert.True(jsrIdx >= 6, "Size-load sequence A200A904 not found or too early for preceding JSR");
        // The 6 hex chars before A200A904 should be a JSR (20 xx xx)
        Assert.Equal("20", hex.Substring(jsrIdx - 6, 2));
    }

    [Fact]
    public void NtadrWithTwoLocalVarArgs()
    {
        // Bug: NTADR_A(localVar, localVar) crashed because the handler's
        // "both runtime" path removed the JSR pusha then called JSR popa,
        // finding nothing on the cc65 stack. Fix: use direct loads instead
        // of pusha/popa for consecutive local loads.
        var bytes = GetProgramBytes(
            """
            byte x = 1;
            byte y = 2;
            ushort addr = NTADR_A(x, y);
            vram_adr(addr);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NtadrTwoLocals hex: {hex}");
        // Must contain STA $17 (8517) to save x into TEMP
        Assert.Contains("8517", hex); // STA TEMP (x)
        // Must contain STA $19 (8519) from NTADR result lo byte
        Assert.Contains("8519", hex); // STA TEMP2

        // Verify JSR (opcode 0x20) appears between TEMP and TEMP2 stores (runtime nametable_a),
        // and at least one JSR after TEMP2 (vram_adr).
        int tempIdx = hex.IndexOf("8517");
        int temp2Idx = hex.IndexOf("8519");
        Assert.True(temp2Idx > tempIdx, "TEMP2 store should occur after TEMP store.");
        string between = hex.Substring(tempIdx, temp2Idx - tempIdx);
        Assert.Contains("20", between); // JSR nametable_a between TEMP stores
    }

    [Fact]
    public void NtadrWithTwoRuntimeMultiplyExpressions()
    {
        // Regression: NTADR_C((byte)(4 + col * 6), (byte)(4 + row * 6))
        // The multiply for the y argument clobbers TEMP which held the x value.
        // The Mul handler's _savedRuntimeToTemp path incorrectly treats the multiply
        // as runtime × runtime when it's actually runtime × constant.
        var bytes = GetProgramBytes(
            """
            byte col = 1;
            byte row = 2;
            ushort addr = NTADR_C((byte)(4 + col * 6), (byte)(4 + row * 6));
            vram_adr(addr);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NtadrTwoMul hex: {hex}");

        // Both args involve * 6 (non-power-of-2), so the multiply loop must appear.
        // After the fix:
        // - First multiply (col * 6) uses TEMP correctly, adds #$04 (not #$06)
        // - First arg saved to TEMP, then pushed to cc65 stack before second multiply
        // - Second multiply (row * 6) uses TEMP freely, adds #$04 (not #$0C)
        // - NTADR handler recovers first arg via popa

        // ADC #$04 must appear (add 4 to multiply results), not ADC #$06 or ADC #$0C
        Assert.Contains("6904", hex); // CLC; ADC #$04
        // The NTADR handler must set up args correctly via popa
        Assert.Contains("8519", hex); // STA TEMP2 (save y)
        Assert.Contains("8517", hex); // STA TEMP (save x from popa)
        Assert.Contains("A519", hex); // LDA TEMP2 (restore y)
    }
}
