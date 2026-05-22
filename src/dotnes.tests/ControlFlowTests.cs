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
        // CMP #$01, #$02, #$03 for cases 1..3
        Assert.Contains("C901", hex);
        Assert.Contains("C902", hex);
        Assert.Contains("C903", hex);
        // BNE (D0) for skipping JMP on mismatch
        Assert.Contains("D0", hex);
        // JMP (4C) for jumping to case/default targets
        Assert.Contains("4C", hex);
        // The default value 0x26 must appear (LDA #$26 = A926)
        Assert.Contains("A926", hex);
    }

    [Fact]
    public void SwitchStateMachine()
    {
        // The state-machine pattern from the issue: title / playing / over with a default.
        // Roslyn lowers a dense byte switch with default into IL `switch` + br to default.
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
                        state = STATE_TITLE;
                        break;
                }
                ppu_wait_nmi();
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // CMP #$01 and CMP #$02 for case 1 and case 2 of the dense switch
        Assert.Contains("C901", hex);
        Assert.Contains("C902", hex);
        // JMP (4C) trampolines from the switch dispatch
        Assert.Contains("4C", hex);
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
}
