using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ControlFlowTests : RoslynTests
{
    public ControlFlowTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void EnumSwitch()
    {
        // Enum used in a switch-like if/else chain (common game pattern)
        var bytes = GetProgramBytes(
            """
            byte x = 128;
            Direction dir = Direction.Right;
            if (dir == Direction.Left) x--;
            if (dir == Direction.Right) x++;
            ppu_on_all();
            while (true) ;

            enum Direction : byte { Left, Right, Up, Down }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Direction.Right = 1, stored with LDA #$01 (A901)
        Assert.Contains("A901", hex);
        // Direction.Left = 0: compiler optimizes == 0 to BNE (D0) without CMP
        Assert.Contains("D0", hex);
        // INC (EE) for x++ and DEC (CE) for x--
        Assert.Contains("EE", hex);
        Assert.Contains("CE", hex);
    }

    [Fact]
    public void ForLoop()
    {
        // Verify for loops produce valid 6502 code
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x30);
            vram_adr(NTADR_A(0, 0));
            for (byte i = 0; i < 5; i++)
            {
                vram_fill(i, 1);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // INC for i++ 
        Assert.Contains("EE", hex);
        // CMP #$05 (C905) for i < 5
        Assert.Contains("C905", hex);
    }

    [Fact]
    public void SwitchSmall()
    {
        // Small switch (3 cases) — like ActorState checks
        var bytes = GetProgramBytes(
            """
            byte state = 1;
            switch (state) {
                case 0: pal_col(0, 0x10); break;
                case 1: pal_col(0, 0x20); break;
                case 2: pal_col(0, 0x30); break;
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain CMP #$01 and CMP #$02 for case 1 and 2
        Assert.Contains("C901", hex);
        Assert.Contains("C902", hex);
        // BNE (D0) for skipping JMP on mismatch
        Assert.Contains("D0", hex);
        // JMP (4C) for jumping to case targets
        Assert.Contains("4C", hex);
    }

    [Fact]
    public void SwitchEnum()
    {
        // Switch on enum — like climber.c's move_actor switch on ActorState
        var bytes = GetProgramBytes(
            """
            ActorState state = ActorState.Walking;
            switch (state) {
                case ActorState.Inactive: pal_col(0, 0x00); break;
                case ActorState.Standing: pal_col(0, 0x10); break;
                case ActorState.Walking:  pal_col(0, 0x20); break;
                case ActorState.Climbing: pal_col(0, 0x30); break;
                case ActorState.Jumping:  pal_col(0, 0x16); break;
                case ActorState.Falling:  pal_col(0, 0x26); break;
                case ActorState.Pacing:   pal_col(0, 0x36); break;
            }
            ppu_on_all();
            while (true) ;

            enum ActorState : byte { Inactive, Standing, Walking, Climbing, Jumping, Falling, Pacing }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // 7-case switch should have CMP for cases 1-6
        Assert.Contains("C901", hex);
        Assert.Contains("C906", hex);
    }

    [Fact]
    public void SwitchWithDefault()
    {
        // switch on byte with 4+ cases and a default clause.
        // The default must run when the discriminator does not match any case.
        var bytes = GetProgramBytes(
            """
            byte op = 4;
            byte r = 0;
            switch (op) {
                case 0: r = 0x10; break;
                case 1: r = 0x20; break;
                case 2: r = 0x30; break;
                case 3: r = 0x16; break;
                default: r = 0x26; break;
            }
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Each non-zero case must emit the BNE+JMP trampoline:
        //   CMP #imm (C9 imm), BNE +3 (D0 03), JMP target (4C lo hi)
        // Assert the 5-byte prefix C9 imm D0 03 4C is present for cases 1..3.
        Assert.Contains("C901D0034C", hex);
        Assert.Contains("C902D0034C", hex);
        Assert.Contains("C903D0034C", hex);
        // Case 0 uses BEQ-style trampoline (no CMP): BNE +3, JMP target.
        Assert.Contains("D0034C", hex);
        // The default value 0x26 must appear (LDA #$26 = A926) — this is the
        // distinctive side effect of the default arm and proves fall-through dispatch.
        Assert.Contains("A926", hex);
    }

    [Fact]
    public void SwitchStateMachine()
    {
        // The state-machine pattern from the issue: title / playing / over with a default.
        // Roslyn lowers a dense byte switch with default into IL `switch` + br to default.
        // The default arm sets `lives` to a distinctive sentinel value (0xAB) so the
        // assertions can prove the default arm's body is actually emitted, separate
        // from the normal state transitions.
        var bytes = GetProgramBytes(
            """
            const byte STATE_TITLE   = 0;
            const byte STATE_PLAYING = 1;
            const byte STATE_OVER    = 2;

            byte state = STATE_TITLE;
            byte lives = 3;

            while (true)
            {
                pad_poll(0);
                switch (state)
                {
                    case STATE_TITLE:
                        if ((pad_trigger(0) & PAD.START) != 0)
                            state = STATE_PLAYING;
                        break;
                    case STATE_PLAYING:
                        if (lives == 0)
                            state = STATE_OVER;
                        break;
                    case STATE_OVER:
                        if ((pad_trigger(0) & PAD.START) != 0)
                            state = STATE_TITLE;
                        break;
                    default:
                        lives = 0xAB;
                        state = STATE_TITLE;
                        break;
                }
                ppu_wait_nmi();
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Each non-zero case must emit the BNE+JMP trampoline signature:
        //   CMP #imm (C9 imm), BNE +3 (D0 03), JMP target (4C lo hi).
        // Asserting the 5-byte prefix proves the dispatch is CMP/BNE+JMP, not
        // unrelated CMP/BNE/JMP from surrounding control flow.
        Assert.Contains("C901D0034C", hex);
        Assert.Contains("C902D0034C", hex);
        // Case 0 uses BEQ-style trampoline (value already in A): BNE +3, JMP target.
        Assert.Contains("D0034C", hex);
        // The default arm's distinctive sentinel value 0xAB must appear
        // (LDA #$AB = A9AB), proving the default body is reachable/emitted.
        Assert.Contains("A9AB", hex);
    }

    [Fact]
    public void SwitchStateMachineWithRendering()
    {
        // Full game-style state machine: nametable text setup + per-state background
        // color via pal_col, driven by START on controller 1. Each case body has a
        // distinctive pal_col immediate so we can assert the dispatch lands in each arm.
        var bytes = GetProgramBytes(
            """
            const byte STATE_TITLE   = 0;
            const byte STATE_PLAYING = 1;
            const byte STATE_OVER    = 2;

            pal_col(0, 0x02);
            pal_col(1, 0x14);
            pal_col(2, 0x20);
            pal_col(3, 0x30);

            vram_adr(NTADR_A(8, 14));
            vram_write("PRESS START");
            ppu_on_all();

            byte state = STATE_TITLE;

            while (true)
            {
                ppu_wait_nmi();
                pad_poll(0);

                switch (state)
                {
                    case STATE_TITLE:
                        pal_col(0, 0x12);
                        if ((pad_trigger(0) & PAD.START) != 0)
                            state = STATE_PLAYING;
                        break;
                    case STATE_PLAYING:
                        pal_col(0, 0x0A);
                        if ((pad_trigger(0) & PAD.START) != 0)
                            state = STATE_OVER;
                        break;
                    case STATE_OVER:
                        pal_col(0, 0x06);
                        if ((pad_trigger(0) & PAD.START) != 0)
                            state = STATE_TITLE;
                        break;
                    default:
                        state = STATE_TITLE;
                        break;
                }
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // CMP/BNE+JMP trampolines for cases 1 and 2 (case 0 uses BEQ-style).
        Assert.Contains("C901D0034C", hex);
        Assert.Contains("C902D0034C", hex);
        Assert.Contains("D0034C", hex);
        // Distinctive per-case pal_col immediates prove each arm's body is emitted.
        // Each value is chosen to NOT appear in the setup pal_col calls above,
        // so the assertion can only be satisfied by the switch arm itself.
        // LDA #imm = A9 imm.
        Assert.Contains("A912", hex); // STATE_TITLE arm
        Assert.Contains("A90A", hex); // STATE_PLAYING arm
        Assert.Contains("A906", hex); // STATE_OVER arm
    }

    [Fact]
    public void SwitchLarge()
    {
        // Build a dense 32-case switch programmatically. Roslyn lowers a dense
        // integral switch to the IL `switch` opcode (jump table), which dotnes
        // expands into a linear chain of CMP/BNE+JMP trampolines. This just
        // verifies the transpiler doesn't choke on a large case count and that
        // the dispatch chain is actually emitted.
        const int caseCount = 32;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("byte op = 17;");
        sb.AppendLine("byte r = 0;");
        sb.AppendLine("switch (op) {");
        for (int i = 0; i < caseCount; i++)
        {
            // Distinctive value per case: 0x40 + i (avoids collision with case index).
            sb.AppendLine($"    case {i}: r = 0x{0x40 + i:X2}; break;");
        }
        sb.AppendLine("    default: r = 0xFF; break;");
        sb.AppendLine("}");
        sb.AppendLine("pal_col(0, r);");
        sb.AppendLine("ppu_on_all();");
        sb.AppendLine("while (true) ;");

        var bytes = GetProgramBytes(sb.ToString());
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Spot-check the BNE+JMP trampoline signature at several case indices.
        // CMP #i, BNE +3, JMP target = C9 ii D0 03 4C.
        Assert.Contains("C901D0034C", hex);   // case 1
        Assert.Contains("C90FD0034C", hex);   // case 15
        Assert.Contains("C91FD0034C", hex);   // case 31 (last)
        // Default sentinel 0xFF must appear (LDA #$FF = A9FF).
        Assert.Contains("A9FF", hex);
    }

    [Fact]
    public void DoWhileLoop()
    {
        // do { } while (cond) — body executes at least once
        var bytes = GetProgramBytes(
            """
            byte x = 0;
            do {
                x = (byte)(x + 1);
            } while (x < 5);
            pal_col(0, x);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // CMP #$05 for the comparison and BCC for backward branch
        Assert.Contains("C905", hex); // CMP #$05
        Assert.Contains("90", hex);   // BCC (backward branch)
    }

    [Fact]
    public void TernaryOperator()
    {
        // ternary: byte r = (x > 3) ? 10 : 20
        var bytes = GetProgramBytes(
            """
            byte x = 5;
            byte r = (x > 3) ? (byte)10 : (byte)20;
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90A", hex); // LDA #$0A (10)
        Assert.Contains("A914", hex); // LDA #$14 (20)
    }

    [Fact]
    public void NestedLoopWithBufferFill()
    {
        // Minimal nested loop: outer loop iterates rows, inner loop fills a buffer,
        // then calls vrambuf_put. This is the core climber draw_entire_stage pattern.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            byte[] heights = new byte[4];
            heights[0] = 3; heights[1] = 3; heights[2] = 3; heights[3] = 3;
            for (byte row = 0; row < 4; row++)
            {
                for (byte col = 0; col < 30; col += 2)
                {
                    buf[col] = 0xF4;
                    buf[(byte)(col + 1)] = 0xF6;
                }
                ushort addr = NTADR_A(1, row);
                vrambuf_put(addr, buf, 30);
                vrambuf_flush();
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"NestedLoop hex: {hex}");
        // Must have two loop back-edges (JMP or BCC/BCS for outer and inner loops)
        // The inner loop fills buf[col] and buf[col+1]
        // F4 and F6 tile values must appear
        Assert.Contains("A9F4", hex); // LDA #$F4
        Assert.Contains("A9F6", hex); // LDA #$F6
    }

    [Fact]
    public void UserMethodBranchLabelsDoNotCollideWithMain()
    {
        // Regression test: branch labels like instruction_XX were not scoped per method,
        // so a JMP in main() could resolve to a label in a user method (or vice versa)
        // if they shared the same IL offset number.
        var bytes = GetProgramBytes(
            """
            static void setup_graphics()
            {
                pal_col(0, 0x02);
                pal_col(1, 0x14);
                bank_spr(0);
                bank_bg(1);
            }
            
            byte[] buf = new byte[30];
            for (byte row = 0; row < 4; row++)
            {
                for (byte col = 0; col < 30; col++)
                {
                    buf[col] = 0xAB;
                }
            }
            setup_graphics();
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UserMethodBranchLabels hex: {hex}");

        // The key assertion: the ROM must contain LDA #$AB (A9AB) for the inner loop body.
        // If labels collide, the inner loop JMP would jump into setup_graphics instead
        // of back to the loop condition, causing the tile value to never be stored.
        Assert.Contains("A9AB", hex); // LDA #$AB
    }

    [Fact]
    public void UserFunctionInWhileTrueBreakInsideForLoop()
    {
        // Reproduces issue #408: user function with early return (return 1 before return 0)
        // called inside while(true){...break;} in a for loop.
        // The IL has two 'ret' instructions. The transpiler treats 'ret' as no-op,
        // so the early 'return 1' falls through to 'return 0', making the function
        // always return 0.
        using var transpiler = BuildProgram(
            """
            byte prev1 = 3;
            byte prev2 = 7;
            byte[] gaps = new byte[5];

            for (byte i = 0; i < 5; i++)
            {
                while (true)
                {
                    gaps[i] = rand8();
                    if (check_overlap(prev1, gaps[i]) == 0 &&
                        check_overlap(prev2, gaps[i]) == 0)
                        break;
                }
            }

            ppu_on_all();
            while (true) ;

            static byte check_overlap(byte x, byte gap)
            {
                if (gap != 0 && x >= gap && x < (byte)(gap + 8))
                    return 1;
                return 0;
            }
            """, out var program);

        program.ResolveAddresses();

        // Dump the method block instructions
        var methodBlock = program.GetBlock("check_overlap");
        Assert.NotNull(methodBlock);

        _logger.WriteLine($"{"=== check_overlap method block ==="}");
        bool foundLda1 = false;
        bool earlyReturnHasJmp = false;
        foreach (var (instruction, label) in methodBlock.InstructionsWithLabels)
        {
            _logger.WriteLine($"  {(label != null ? $"[{label}] " : "")}{instruction.Opcode} {instruction.Mode} {instruction.Operand}");

            // Track: after LDA #$01 (return 1), there should be JMP to method end
            // NOT another LDA #$00 (return 0) -- that would mean fall-through
            if (instruction.Opcode == Opcode.LDA
                && instruction.Operand is ImmediateOperand imm1 && imm1.Value == 1)
            {
                foundLda1 = true;
            }
            else if (foundLda1)
            {
                if (instruction.Opcode == Opcode.JMP)
                    earlyReturnHasJmp = true;
                else if (instruction.Opcode == Opcode.LDA
                    && instruction.Operand is ImmediateOperand imm0 && imm0.Value == 0)
                {
                    // LDA #$01 followed by LDA #$00 = fall-through bug
                    _logger.WriteLine($"{"  *** BUG: LDA #$01 falls through to LDA #$00 ***"}");
                }
                foundLda1 = false;
            }
        }

        Assert.True(earlyReturnHasJmp,
            "User method with early 'return 1' must JMP to epilogue. " +
            "Without it, A is overwritten by 'return 0' and the function always returns 0.");
    }

    [Fact]
    public void GotoBreakOutOfNestedLoops()
    {
        // Issue: support `goto label;` to break out of nested loops (From Below pattern).
        // Roslyn lowers this to Br/Br_s IL targeting the label offset.
        var bytes = GetProgramBytes(
            """
            const byte BOARD_W = 10;
            const byte BOARD_H = 4;

            byte[] board = new byte[BOARD_W * BOARD_H];
            board[(byte)(2 * BOARD_W + 3)] = 1;
            byte foundX = 0, foundY = 0;
            bool found = false;

            for (byte y = 0; y < BOARD_H; y++)
            {
                for (byte x = 0; x < BOARD_W; x++)
                {
                    if (board[(byte)(y * BOARD_W + x)] != 0)
                    {
                        foundX = x;
                        foundY = y;
                        found = true;
                        goto done;
                    }
                }
            }
            done:

            if (found)
                pal_col(0, foundX);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"GotoBreakOutOfNestedLoops hex: {hex}");
        // BOARD_W=10 (0x0A) and BOARD_H=4 are loop bounds → CMP #$0A, CMP #$04
        Assert.Contains("C90A", hex);
        Assert.Contains("C904", hex);
        // The body must execute (LDA #$01 stores 'true' to found)
        Assert.Contains("A901", hex);
    }

    [Fact]
    public void GotoCaseFallthrough()
    {
        // Issue: support `goto case X;` as explicit switch fall-through.
        // Roslyn lowers this to a Br targeting the case label.
        var bytes = GetProgramBytes(
            """
            byte state = 0;
            switch (state)
            {
                case 0:
                    pal_col(0, 0x0F);
                    goto case 1;
                case 1:
                    pal_col(1, 0x30);
                    break;
                default:
                    break;
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"GotoCaseFallthrough hex: {hex}");
        // Both pal_col bodies should be present: LDA #$0F (#15) and LDA #$30 (#48)
        Assert.Contains("A90F", hex);
        Assert.Contains("A930", hex);
        // JMP (4C) for the goto case 1 fall-through
        Assert.Contains("4C", hex);
    }

    [Fact]
    public void GotoBackwardLabel()
    {
        // Issue: `top: ... goto top;` should not produce a stack underflow.
        // Roslyn emits Br_s with a negative offset; the transpiler must resolve to
        // the label declared earlier in the method.
        var bytes = GetProgramBytes(
            """
            byte i = 0;
            top:
            i++;
            if (i < 5)
                goto top;
            pal_col(0, i);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"GotoBackwardLabel hex: {hex}");
        // CMP #$05 for the i < 5 test
        Assert.Contains("C905", hex);
    }
}
