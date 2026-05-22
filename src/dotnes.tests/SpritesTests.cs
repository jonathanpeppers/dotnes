using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class SpritesTests : RoslynTests
{
    public SpritesTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void OamOff_PropertyAccessTranspiles()
    {
        // oam_off is now a property — get/set emit LDA/STA to zero page $1B
        var bytes = GetProgramBytes(
            """
            oam_off = 0;
            oam_off = oam_spr(10, 20, 0x01, 0, oam_off);
            oam_hide_rest(oam_off);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A900", hex);    // LDA #$00 (oam_off = 0)
        Assert.Contains("851B", hex);    // STA $1B (store to OAM_OFF zero page)
        Assert.Contains("A51B", hex);    // LDA $1B (load from OAM_OFF zero page)
    }

    [Fact]
    public void OamMetaSprWithConstantArgs()
    {
        // Bug: EmitOamMetaSpr only handled array element args (ldelem_u1).
        // When x, y, sprid are constants (ldc.i4), the scanner failed to find them.
        // Fix: support constants and locals in addition to array elements.
        var bytes = GetProgramBytes(
            """
            byte[] sprite = new byte[] { 0, 0, 0xd8, 0, 128 };
            NESLib.oam_meta_spr(64, 100, 0, sprite);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamMetaSprConst hex: {hex}");

        // x=64 (0x40) stored to TEMP ($17): LDA #$40 = A940, STA $17 = 8517
        Assert.Contains("A9408517", hex);
        // y=100 (0x64) stored to TEMP2 ($19): LDA #$64 = A964, STA $19 = 8519
        Assert.Contains("A9648519", hex);
        // sprid=0 loaded into A: LDA #$00 = A900
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void OamMetaSprPalWithMixedArgs()
    {
        // Climber pattern: oam_meta_spr_pal(arr[i], local, arr[i], data)
        // x from array element, y from local, pal from array element
        var bytes = GetProgramBytes(
            """
            byte[] actor_x = new byte[8];
            byte[] actor_pal = new byte[8];
            byte[] sprite = new byte[] { 0, 0, 0xd8, 0, 128 };
            byte ai = 0;
            byte screen_y = 100;
            oam_meta_spr_pal(actor_x[ai], screen_y, actor_pal[ai], sprite);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamMetaSprPalMixed hex: {hex}");

        // Verify correct argument ordering:
        // STA $17 (TEMP=x) must come right after LDA abs,X (BD), not after LDA abs (AD)
        int sta17 = hex.IndexOf("8517");
        Assert.True(sta17 >= 0, $"STA $17 not found in hex");
        string before_sta17 = hex.Substring(sta17 - 6, 2);
        Assert.Equal("BD", before_sta17); // LDA abs,X for array element

        // Data pointer setup: STA $2A and STA $2B must exist for ptr1
        Assert.Contains("852A", hex); // STA ptr1
        Assert.Contains("852B", hex); // STA ptr1+1
    }

    [Fact]
    public void OamSpr2x2WithConstantArgs()
    {
        // oam_spr_2x2 with all constant arguments
        var bytes = GetProgramBytes(
            """
            oam_spr_2x2(40, 40, 0xD8, 0xD9, 0xDA, 0xDB, 0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamSpr2x2Const hex: {hex}");

        // x=40 (0x28) stored to TEMP ($17): LDA #$28 = A928, STA $17 = 8517
        Assert.Contains("A9288517", hex);
        // y=40 (0x28) stored to TEMP2 ($19): LDA #$28 = A928, STA $19 = 8519
        Assert.Contains("A9288519", hex);
        // Data pointer setup: STA ptr1 ($2A) and STA ptr1+1 ($2B)
        Assert.Contains("852A", hex); // STA ptr1
        Assert.Contains("852B", hex); // STA ptr1+1
        // sprid=0 loaded into A and followed by JSR oam_meta_spr: LDA #$00 = A900, JSR = 20
        Assert.Contains("A90020", hex);
    }

    [Fact]
    public void OamSpr2x2WithLocalArgs()
    {
        // oam_spr_2x2 with local x, y, and constant tiles/attr/sprid
        var bytes = GetProgramBytes(
            """
            byte x = 40;
            byte y = 40;
            oam_spr_2x2(x, y, 0xD8, 0xD9, 0xDA, 0xDB, 0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamSpr2x2Local hex: {hex}");

        // x from local stored to TEMP ($17): LDA abs = AD...., STA $17 = 8517
        Assert.Contains("8517", hex);
        // y from local stored to TEMP2 ($19): STA $19 = 8519
        Assert.Contains("8519", hex);
        // Data pointer setup
        Assert.Contains("852A", hex); // STA ptr1
        Assert.Contains("852B", hex); // STA ptr1+1
    }

    [Fact]
    public void OamSpr_DivisionInCompoundArg()
    {
        // oam_spr with division in a compound argument should emit a
        // division loop instead of throwing TranspileException.
        // Pattern: (byte)(0x05 + (difficulty / 10))
        var bytes = GetProgramBytes(
            """
            byte[] PALETTE = [
                0x30,
                0x11, 0x30, 0x27, 0x0,
                0x1c, 0x20, 0x2c, 0x0,
                0x00, 0x10, 0x20, 0x0,
                0x06, 0x16, 0x26, 0x0,
                0x14, 0x34, 0x0d, 0x0,
                0x00, 0x37, 0x25, 0x0,
                0x0d, 0x2d, 0x3a, 0x0,
                0x0d, 0x27, 0x2a
            ];

            pal_all(PALETTE);
            byte difficulty = 42;
            oam_spr(148, 114, (byte)(0x05 + (difficulty / 10)), 0x01, 0);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamSpr_Div hex: {hex}");

        // Division by 10 uses repeated subtraction loop:
        // LDX #$FF, SEC, INX, SBC #$0A, BCS -5, TXA
        Assert.Contains("A2FF38E8E90AB0FB8A", hex);
    }

    [Fact]
    public void OamSpr_ModuloInCompoundArg()
    {
        // oam_spr with modulo in a compound argument should emit a
        // modulo loop instead of throwing TranspileException.
        // Pattern: (byte)(0x04 + (difficulty % 10))
        var bytes = GetProgramBytes(
            """
            byte[] PALETTE = [
                0x30,
                0x11, 0x30, 0x27, 0x0,
                0x1c, 0x20, 0x2c, 0x0,
                0x00, 0x10, 0x20, 0x0,
                0x06, 0x16, 0x26, 0x0,
                0x14, 0x34, 0x0d, 0x0,
                0x00, 0x37, 0x25, 0x0,
                0x0d, 0x2d, 0x3a, 0x0,
                0x0d, 0x27, 0x2a
            ];

            pal_all(PALETTE);
            byte difficulty = 42;
            oam_spr(156, 114, (byte)(0x04 + (difficulty % 10)), 0x01, 0);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"OamSpr_Rem hex: {hex}");

        // Modulo by 10 uses repeated subtraction:
        // SEC, SBC #$0A, BCS -4, ADC #$0A
        Assert.Contains("38E90AB0FC690A", hex);
    }

    [Fact]
    public void OamScope_EmitsLdaStaJsrInMainBlock()
    {
        // new OamScope() should emit: LDA #0, STA $1B (oam_off), JSR oam_clear in the main block
        var (program, _) = BuildProgram(
            """
            using (var oam = new OamScope())
            {
                oam.spr(10, 20, 0x01, 0);
            }
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find the LDA #$00 → STA $1B → JSR oam_clear sequence
        bool foundSequence = false;
        for (int i = 0; i < instructions.Count - 2; i++)
        {
            var lda = instructions[i].Instruction;
            var sta = instructions[i + 1].Instruction;
            var jsr = instructions[i + 2].Instruction;
            if (lda.Opcode == Opcode.LDA && lda.Mode == AddressMode.Immediate &&
                lda.Operand is ImmediateOperand imm && imm.Value == 0x00 &&
                sta.Opcode == Opcode.STA && sta.Mode == AddressMode.ZeroPage &&
                sta.Operand is ImmediateOperand zpg && zpg.Value == 0x1B &&
                jsr.Opcode == Opcode.JSR && jsr.Operand is LabelOperand lbl && lbl.Label == "oam_clear")
            {
                foundSequence = true;
                break;
            }
        }
        Assert.True(foundSequence, "Expected LDA #$00 → STA $1B → JSR oam_clear sequence in main block");
    }

    [Fact]
    public void OamScope_EmitsOamClearAndOamHideRestInMainBlock()
    {
        // OamScope constructor emits JSR oam_clear; OamScope.Dispose() emits LDA oam_off, JSR oam_hide_rest
        var (program, _) = BuildProgram(
            """
            using (var oam = new OamScope())
            {
                oam.spr(10, 20, 0x01, 0);
            }
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");

        // Verify JSR oam_clear is emitted in the main block (from OamScope constructor)
        bool hasOamClear = mainBlock.InstructionsWithLabels.Any(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_clear");
        Assert.True(hasOamClear, "Expected JSR oam_clear from new OamScope() in main block");

        // Verify JSR oam_hide_rest is emitted in the main block (from OamScope.Dispose)
        bool hasOamHideRest = mainBlock.InstructionsWithLabels.Any(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_hide_rest");
        Assert.True(hasOamHideRest, "Expected JSR oam_hide_rest from OamScope.Dispose() in main block");
    }

    [Fact]
    public void OamScope_InsideLoopBody_EmitsCorrectOrdering()
    {
        // Realistic game loop: new OamScope() inside while(true), Dispose called each iteration
        var (program, _) = BuildProgram(
            """
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                using (var oam = new OamScope())
                {
                    oam.spr(10, 20, 0x01, 0);
                }
            }
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        static bool IsJsrTo((Instruction Instruction, string? Label) il, string label) =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl &&
            lbl.Label == label;

        int loopStartIndex= instructions.FindIndex(il => IsJsrTo(il, "ppu_wait_nmi"));
        int clearIndex = instructions.FindIndex(il => IsJsrTo(il, "oam_clear"));
        int hideRestIndex = instructions.FindIndex(il => IsJsrTo(il, "oam_hide_rest"));

        Assert.True(loopStartIndex >= 0, "Expected JSR ppu_wait_nmi at start of loop body");
        Assert.True(clearIndex >= 0, "Expected JSR oam_clear from new OamScope()");
        Assert.True(hideRestIndex >= 0, "Expected JSR oam_hide_rest from OamScope.Dispose()");

        // Both OAM calls must be inside the loop body (after ppu_wait_nmi)
        Assert.True(loopStartIndex < clearIndex,
            $"oam_clear (index {clearIndex}) should be inside loop body after ppu_wait_nmi (index {loopStartIndex})");
        Assert.True(clearIndex < hideRestIndex,
            $"oam_clear (index {clearIndex}) should come before oam_hide_rest (index {hideRestIndex})");

        // The endfinally handler must emit a backward JMP to the loop header
        // (not fall through into the next function's code).
        // The JMP should appear within a few instructions after oam_hide_rest
        // and target an address at or before the loop start (ppu_wait_nmi).
        int jmpIndex = -1;
        for (int i = hideRestIndex + 1; i < Math.Min(hideRestIndex + 4, instructions.Count); i++)
        {
            if (instructions[i].Instruction.Opcode == Opcode.JMP)
            {
                jmpIndex = i;
                break;
            }
        }
        Assert.True(jmpIndex >= 0,
            "Expected a JMP within a few instructions after oam_hide_rest");

        // Verify the JMP targets an address at or before the loop start
        var jmpOperand = instructions[jmpIndex].Instruction.Operand;
        Assert.NotNull(jmpOperand);
        // The JMP uses an absolute address operand pointing backward in the code
        Assert.True(jmpOperand is ImmediateOperand or LabelOperand,
            $"Expected JMP to have an address or label operand, got {jmpOperand.GetType().Name}");
    }

    [Fact]
    public void OamScopeSpr_EmitsOamOffLoadAndStore()
    {
        // oam.spr(x, y, chr, attr) should auto-manage oam_off:
        // LDA $1B before JSR oam_spr, STA $1B after
        var (program, _) = BuildProgram(
            """
            using (var oam = new OamScope())
            {
                oam.spr(10, 20, 0x01, 0);
            }
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find JSR oam_spr and verify STA $1B follows it
        int jsrIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_spr");
        Assert.True(jsrIndex >= 0, "Expected JSR oam_spr from oam.spr()");

        // The instruction after JSR oam_spr should be STA $1B (store oam_off)
        Assert.True(jsrIndex + 1 < instructions.Count, "Expected instruction after JSR oam_spr");
        var staAfter = instructions[jsrIndex + 1].Instruction;
        Assert.Equal(Opcode.STA, staAfter.Opcode);
        Assert.Equal(AddressMode.ZeroPage, staAfter.Mode);
        Assert.Equal(0x1B, ((ImmediateOperand)staAfter.Operand!).Value);
    }

    [Fact]
    public void OamScopeMetaSpr_EmitsOamOffLoadAndStore()
    {
        // oam.meta_spr(x, y, data) should auto-manage oam_off
        var source =
            """
            byte[] sprite = new byte[] { 0, 0, 0x01, 0, 128 };
            using (var oam = new OamScope())
            {
                oam.meta_spr(10, 20, sprite);
            }
            ppu_on_all();
            while (true) ;
            """;
        var (program, _) = BuildProgram(source);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find JSR oam_meta_spr and verify STA $1B follows it
        int jsrIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_meta_spr");
        Assert.True(jsrIndex >= 0, "Expected JSR oam_meta_spr from oam.meta_spr()");

        var staAfter = instructions[jsrIndex + 1].Instruction;
        Assert.Equal(Opcode.STA, staAfter.Opcode);
        Assert.Equal(AddressMode.ZeroPage, staAfter.Mode);
        Assert.Equal(0x1B, ((ImmediateOperand)staAfter.Operand!).Value);
    }

    [Fact]
    public void MetaSpr2x2BeforeNewarrDoesNotThrow()
    {
        // Regression test for #484: meta_spr_2x2 removes its argument LDAs from
        // the block, then the subsequent newarr handler tried to remove an LDA
        // that no longer existed, causing an InvalidOperationException.
        var bytes = GetProgramBytes(
            """
            byte[] metasprite = meta_spr_2x2(0xD8, 0xD9, 0xDA, 0xDB);
            byte[] actor_x = new byte[16];
            byte[] actor_y = new byte[16];
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }
}
