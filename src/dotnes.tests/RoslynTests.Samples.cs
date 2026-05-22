using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RoslynTests_Samples : RoslynTests
{
    public RoslynTests_Samples(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void HelloWorld()
    {
        AssertProgram(
            csharpSource:
                """
                pal_col(0, 0x02);
                pal_col(1, 0x14);
                pal_col(2, 0x20);
                pal_col(3, 0x30);
                vram_adr(NTADR_A(2, 2));
                vram_write("HELLO, .NET!");
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A900    ; LDA #$00
                20A285  ; JSR pusha
                A902    ; LDA #$02
                203E82  ; JSR pal_col
                A901    ; LDA #$01
                20A285  ; JSR pusha
                A914    ; LDA #$14
                203E82  ; JSR pal_col
                A902    ; LDA #$02
                20A285  ; JSR pusha
                A920    ; LDA #$20
                203E82  ; JSR pal_col
                A903    ; LDA #$03
                20A285  ; JSR pusha
                A930    ; LDA #$30
                203E82  ; JSR pal_col
                A220    ; LDX #$20
                A942    ; LDA #$42
                20D483  ; JSR vram_adr
                A9F1    ; LDA #$F1
                A285    ; LDX #$85
                20B885  ; JSR pushax
                A200    ; LDX #$00
                A90C    ; LDA #$0C
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C4085  ; JMP $8540
                """);
    }

    [Fact]
    public void AttributeTable()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] ATTRIBUTE_TABLE = new byte[0x40] {
                  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                  0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                  0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                  0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                  0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                  0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
                };
                byte[] PALETTE = new byte[16] {
                  0x03,
                  0x11,0x30,0x27,0x0,
                  0x1c,0x20,0x2c,0x0,
                  0x00,0x10,0x20,0x0,
                  0x06,0x16,0x26
                };
                pal_bg(PALETTE);
                vram_adr(NAMETABLE_A);
                vram_fill(0x16, 32 * 30);
                vram_write(ATTRIBUTE_TABLE);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A91C    ; LDA #$1C
                A286    ; LDX #$86
                202B82  ; JSR pal_bg
                A220    ; LDX #$20
                A900    ; LDA #$00
                20D483  ; JSR vram_adr
                A916    ; LDA #$16
                208D85  ; JSR pusha
                A203    ; LDX #$03
                A9C0    ; LDA #$C0
                20DF83  ; JSR vram_fill
                A9DC    ; LDA #$DC
                A285    ; LDX #$85
                20A385  ; JSR pushax
                A200    ; LDX #$00
                A940    ; LDA #$40
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C2B85  ; JMP $852B
                """);
    }

    [Fact]
    public void OneLocal()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] ATTRIBUTE_TABLE = new byte[0x40] {
                  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                  0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                  0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                  0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                  0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                  0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
                };
                byte[] PALETTE = new byte[16] {
                  0x03,
                  0x11,0x30,0x27,0x0,
                  0x1c,0x20,0x2c,0x0,
                  0x00,0x10,0x20,0x0,
                  0x06,0x16,0x26
                };
                uint num = 32 * 30;
                pal_bg(PALETTE);
                vram_adr(NAMETABLE_A);
                vram_fill(0x16, num);
                vram_write(ATTRIBUTE_TABLE);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A203
                A9C0
                8D2503
                8E2603
                A928
                A286
                202B82  ; JSR pal_bg
                A220
                A900
                20D483  ; JSR vram_adr
                A916
                209985  ; JSR pusha
                AD2503
                AE2603
                20DF83  ; JSR vram_fill
                A9E8
                A285
                20AF85  ; JSR pushax
                A200
                A940
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C3785  ; JMP $8537
                """);
    }

    [Fact]
    public void OneLocalByte()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] ATTRIBUTE_TABLE = new byte[0x40] {
                  0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                  0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                  0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                  0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                  0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                  0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                  0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
                };
                byte[] PALETTE = new byte[16] {
                  0x03,
                  0x11,0x30,0x27,0x0,
                  0x1c,0x20,0x2c,0x0,
                  0x00,0x10,0x20,0x0,
                  0x06,0x16,0x26
                };
                byte nametable = 0x16;
                pal_bg(PALETTE);
                vram_adr(NAMETABLE_A);
                vram_fill(nametable, 32 * 30);
                vram_write(ATTRIBUTE_TABLE);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A916
                8D2503  ; STA M0001
                A91F
                A286
                202B82  ; JSR pal_bg
                A220
                A900
                20D483  ; JSR vram_adr
                AD2503
                A203
                A9C0
                20DF83  ; JSR vram_fill
                A9DF
                A285
                20A685  ; JSR pushax
                A200
                A940
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C2E85  ; JMP loop
                """);
    }

    [Fact]
    public void StaticSprite()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] PALETTE = new byte[32] { 
                    0x01,
                    0x11,0x30,0x27,0x0,
                    0x1c,0x20,0x2c,0x0,
                    0x00,0x10,0x20,0x0,
                    0x06,0x16,0x26,0x0,
                    0x16,0x35,0x24,0x0,
                    0x00,0x37,0x25,0x0,
                    0x0d,0x2d,0x3a,0x0,
                    0x0d,0x27,0x2a
                };
                pal_all(PALETTE);
                oam_spr(40, 40, 0x10, 3, 0);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A9E1
                A285
                201182  ; JSR pal_all
                206385  ; JSR decsp4
                A928
                A003    ; LDY #$03
                9122    ; STA ($22),Y
                A928
                88      ; DEY
                9122    ; STA ($22),Y
                A910
                88      ; DEY
                9122    ; STA ($22),Y
                A903
                88      ; DEY
                9122    ; STA ($22),Y
                A900
                20B585  ; JSR oam_spr
                208982  ; JSR ppu_on_all
                4C2785  ; JMP loop
                """);
    }

    [Fact]
    public void Branch()
    {
        AssertProgram(
            csharpSource:
                """
                byte[] PALETTE = new byte[32] { 
                    0x01,
                    0x11,0x30,0x27,0x0,
                    0x1c,0x20,0x2c,0x0,
                    0x00,0x10,0x20,0x0,
                    0x06,0x16,0x26,0x0,
                    0x16,0x35,0x24,0x0,
                    0x00,0x37,0x25,0x0,
                    0x0d,0x2d,0x3a,0x0,
                    0x0d,0x27,0x2a
                };
                pal_all(PALETTE);
                byte x = 40;
                if (x == 40)
                    oam_spr(x, 40, 0x10, 3, 0);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A9EEA285
                201182  ; JSR pal_all
                A928    ; LDA #$28 (x = 40)
                8D2503  ; STA $0325
                AD2503  ; LDA $0325 (load x)
                C928    ; CMP #$28
                D01E    ; BNE skip
                207085  ; JSR decsp4
                AD2503  ; LDA $0325 (x)
                A003    ; LDY #$03
                9122    ; STA ($22),Y
                A928    ; LDA #$28
                88      ; DEY
                9122    ; STA ($22),Y
                A910    ; LDA #$10
                88      ; DEY
                9122    ; STA ($22),Y
                A903    ; LDA #$03
                88      ; DEY
                9122    ; STA ($22),Y
                A900    ; LDA #$00
                20C285  ; JSR oam_spr
                208982  ; JSR ppu_on_all
                4C3485  ; JMP loop
                """);
    }

    [Fact]
    public void PadPollWithBranch()
    {
        // This test isolates the if ((pad & PAD.LEFT) != 0) pattern from movingsprite
        AssertProgram(
            csharpSource:
                """
                byte[] PALETTE = [ 
                    0x30,
                    0x11,0x30,0x27,0x0,
                    0x1c,0x20,0x2c,0x0,
                    0x00,0x10,0x20,0x0,
                    0x06,0x16,0x26,0x0,
                    0x14,0x34,0x0d,0x0,
                    0x00,0x37,0x25,0x0,
                    0x0d,0x2d,0x3a,0x0,
                    0x0d,0x27,0x2a
                ];
                byte x = 40;
                pal_all(PALETTE);
                ppu_on_all();
                while (true)
                {
                    ppu_wait_nmi();
                    PAD pad = pad_poll(0);
                    if ((pad & PAD.LEFT) != 0) x--;
                    oam_spr(x, 40, 0xD8, 0, 0);
                }
                """,
            expectedAssembly:
                """
                A928        ; LDA #$28 (x = 40)
                8D2503      ; STA $0325 (store x to local)
                A948        ; LDA byte array low (deferred)
                A286        ; LDX byte array high
                201182      ; JSR pal_all
                208982      ; JSR ppu_on_all
                20F082      ; JSR ppu_wait_nmi (loop start)
                A900        ; LDA #$00 (pad_poll argument)
                20CB85      ; JSR pad_poll
                8D2603      ; STA $0326 (store pad for reuse)
                2940        ; AND #$40 (PAD.LEFT)
                F003        ; BEQ +3 (skip DEC if zero)
                CE2503      ; DEC $0325 (x--)
                207985      ; JSR decsp4
                AD2503      ; LDA $0325 (load x for oam_spr)
                A003        ; LDY #$03
                9122        ; STA ($22),Y
                A928        ; LDA #$28 (y = 40)
                88          ; DEY
                9122        ; STA ($22),Y
                A9D8        ; LDA #$D8 (tile)
                88          ; DEY
                9122        ; STA ($22),Y
                A900        ; LDA #$00 (attr)
                88          ; DEY
                9122        ; STA ($22),Y
                201C86      ; JSR oam_spr
                4C0F85      ; JMP loop
                """);
    }

    [Fact]
    public void MovingSpritePattern()
    {
        // This test reproduces the full movingsprite sample pattern with 4 directional checks
        AssertProgram(
            csharpSource:
                """
                byte[] PALETTE = [ 
                    0x30,
                    0x11,0x30,0x27,0x0,
                    0x1c,0x20,0x2c,0x0,
                    0x00,0x10,0x20,0x0,
                    0x06,0x16,0x26,0x0,
                    0x14,0x34,0x0d,0x0,
                    0x00,0x37,0x25,0x0,
                    0x0d,0x2d,0x3a,0x0,
                    0x0d,0x27,0x2a
                ];
                byte x = 40;
                byte y = 40;
                pal_all(PALETTE);
                ppu_on_all();
                while (true)
                {
                    ppu_wait_nmi();
                    PAD pad = pad_poll(0);
                    if ((pad & PAD.LEFT) != 0) x--;
                    if ((pad & PAD.RIGHT) != 0) x++;
                    if ((pad & PAD.UP) != 0) y--;
                    if ((pad & PAD.DOWN) != 0) y++;
                    oam_spr(x, y, 0xD8, 0, 0);
                }
                """,
            expectedAssembly:
                """
                A928        ; LDA #$28 (x = y = 40)
                8D2503      ; STA $0325 (store x)
                8D2603      ; STA $0326 (store y, reuse A=40)
                A96A        ; LDA byte array low (deferred)
                A286        ; LDX byte array high
                201182      ; JSR pal_all
                208982      ; JSR ppu_on_all
                20F082      ; JSR ppu_wait_nmi (loop start)
                A900        ; LDA #$00
                20ED85      ; JSR pad_poll
                8D2703      ; STA $0327 (store pad for reuse)
                2940        ; AND #$40 (PAD.LEFT)
                F003        ; BEQ +3 (skip DEC if zero)
                CE2503      ; DEC $0325 (x--)
                AD2703      ; LDA $0327 (reload pad)
                2980        ; AND #$80 (PAD.RIGHT)
                F003        ; BEQ +3 (skip INC if zero)
                EE2503      ; INC $0325 (x++)
                AD2703      ; LDA $0327 (reload pad)
                2910        ; AND #$10 (PAD.UP)
                F003        ; BEQ +3 (skip DEC if zero)
                CE2603      ; DEC $0326 (y--)
                AD2703      ; LDA $0327 (reload pad)
                2920        ; AND #$20 (PAD.DOWN)
                F003        ; BEQ +3 (skip INC if zero)
                EE2603      ; INC $0326 (y++)
                209B85      ; JSR decsp4
                AD2503      ; LDA $0325 (load x)
                A003        ; LDY #$03
                9122        ; STA ($22),Y
                AD2603      ; LDA $0326 (load y)
                88          ; DEY
                9122        ; STA ($22),Y
                A9D8        ; LDA #$D8 (tile)
                88          ; DEY
                9122        ; STA ($22),Y
                A900        ; LDA #$00 (attr)
                88          ; DEY
                9122        ; STA ($22),Y
                203E86      ; JSR oam_spr
                4C1285      ; JMP loop
                """);
    }

    [Fact]
    public void OneLocal_UshortLocalForVramFill()
    {
        // Ported from onelocal.dll (no source existed).
        // Tests: ushort local variable (960) passed to vram_fill.
        var bytes = GetProgramBytes(
            """
            byte[] ATTRIBUTE_TABLE = [
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
            ];

            byte[] PALETTE = [
                0x03,
                0x11, 0x30, 0x27, 0x0,
                0x1c, 0x20, 0x2c, 0x0,
                0x00, 0x10, 0x20, 0x0,
                0x06, 0x16, 0x26
            ];

            ushort fill_count = 960;

            pal_bg(PALETTE);
            vram_adr(NAMETABLE_A);
            vram_fill(0x16, fill_count);
            vram_write(ATTRIBUTE_TABLE);
            ppu_on_all();

            while (true) ;
            """);

        Assert.Equal(
            "A203A9C08D25038E2603A928A286202B82A220A90020D483A916209985AD2503AE260320DF83A9E8A28520AF85A200A940204F832089824C3785",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void OneLocalByte_ByteLocalForVramFill()
    {
        // Ported from onelocalbyte.dll (no source existed).
        // Tests: byte local variable (0x16) passed to vram_fill.
        var bytes = GetProgramBytes(
            """
            byte[] ATTRIBUTE_TABLE = [
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55,
                0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f
            ];

            byte[] PALETTE = [
                0x03,
                0x11, 0x30, 0x27, 0x0,
                0x1c, 0x20, 0x2c, 0x0,
                0x00, 0x10, 0x20, 0x0,
                0x06, 0x16, 0x26
            ];

            byte tile = 0x16;

            pal_bg(PALETTE);
            vram_adr(NAMETABLE_A);
            vram_fill(tile, 960);
            vram_write(ATTRIBUTE_TABLE);
            ppu_on_all();

            while (true) ;
            """);

        Assert.Equal(
            "A9168D2503A91FA286202B82A220A90020D483AD2503A203A9C020DF83A9DFA28520A685A200A940204F832089824C2E85",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void StaticSprite_OamSprCalls()
    {
        // Ported from staticsprite sample (18 LOC).
        // Tests: pal_all + multiple oam_spr calls with immediate args.
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
            oam_spr(40, 40, 0xD8, 0, 0);
            oam_spr(48, 40, 0xDA, 0, 4);
            oam_spr(40, 48, 0xD9, 0, 8);
            oam_spr(48, 48, 0xDB, 0, 12);
            ppu_on_all();

            while (true) ;
            """);

        Assert.Equal(
            "A936A28620118220B885A928A0039122A928889122A9D8889122A900889122200A8620B885A930A0039122A928889122A9DA889122A900889122A904200A8620B885A928A0039122A930889122A9D9889122A900889122A908200A8620B885A930A0039122A930889122A9DB889122A900889122A90C200A862089824C7C85",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void PpuHello_PokeCalls()
    {
        // Ported from ppuhello sample (37 LOC).
        // Tests: poke() for direct PPU register access.
        var bytes = GetProgramBytes(
            """
            waitvsync();
            waitvsync();

            poke(PPU_CTRL, 0);
            poke(PPU_MASK, 0);

            poke(PPU_ADDR, 0x3F);
            poke(PPU_ADDR, 0x00);
            poke(PPU_DATA, 0x01);
            poke(PPU_DATA, 0x00);
            poke(PPU_DATA, 0x10);
            poke(PPU_DATA, 0x20);

            poke(PPU_ADDR, 0x21);
            poke(PPU_ADDR, 0xC9);
            poke(PPU_DATA, 0x48);
            poke(PPU_DATA, 0x45);
            poke(PPU_DATA, 0x4C);
            poke(PPU_DATA, 0x4C);
            poke(PPU_DATA, 0x4F);
            poke(PPU_DATA, 0x20);
            poke(PPU_DATA, 0x50);
            poke(PPU_DATA, 0x50);
            poke(PPU_DATA, 0x55);
            poke(PPU_DATA, 0x21);

            poke(PPU_SCROLL, 0);
            poke(PPU_SCROLL, 0);

            poke(PPU_ADDR, 0x20);
            poke(PPU_ADDR, 0x00);

            poke(PPU_MASK, (byte)(MASK.BG | MASK.SPR | MASK.EDGE_BG | MASK.EDGE_SPR));

            while (true) ;
            """);

        Assert.Equal(
            "202C86202C86A9008D00208D0120A93F8D0620A9008D0620A9018D0720A9008D0720A9108D0720A9208D0720A9218D0620A9C98D0620A9488D0720A9458D0720A94C8D07208D0720A94F8D0720A9208D0720A9508D07208D0720A9558D0720A9218D0720A9008D05208D0520A9208D0620A9008D0620A91E8D01204C7B85",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void Lols_ScrollLoop()
    {
        // Ported from lols sample (36 LOC).
        // Tests: pal_col + vram_write + ppu_wait_frame + scroll in a loop.
        var bytes = GetProgramBytes(
            """
            byte scroll_y = 0;

            pal_col(0, 0x02);
            pal_col(1, 0x14);
            pal_col(2, 0x20);
            pal_col(3, 0x30);

            vram_adr(NTADR_A(2, 2));
            vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
            vram_adr(NTADR_A(2, 8));
            vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
            vram_adr(NTADR_A(2, 14));
            vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
            vram_adr(NTADR_A(2, 20));
            vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");
            vram_adr(NTADR_A(2, 26));
            vram_write("LOL! LOL! LOL! LOL! LOL! LOL!");

            ppu_on_all();

            while (true)
            {
                ppu_wait_frame();
                scroll_y += 1;
                scroll(0, scroll_y);
            }
            """);

        Assert.Equal(
            "A9008D2503A900200E86A902203E82A901200E86A914203E82A902200E86A920203E82A903200E86A930203E82A220A94220D483A95DA286202486A200A91D204F83A221A90220D483A95DA286202486A200A91D204F83A221A9C220D483A95DA286202486A200A91D204F83A222A98220D483A95DA286202486A200A91D204F83A223A94220D483A95DA286202486A200A91D204F8320898220DB82EE2503A900A200202486AD250320FB824C9985",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void OamStatic_StaticFieldCompoundExpr()
    {
        // Ported from oamstatic sample (29 LOC).
        // Tests: static fields + compound oam_spr expressions (field - const, field + const).
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
            oam_clear();

            G.spr_x = 124;
            G.spr_y = 108;

            G.spr = oam_spr((byte)(G.spr_x - 4), (byte)(G.spr_y - 4), 0xC0, 0x03, 0);
            G.spr = oam_spr((byte)(G.spr_x - 4), (byte)(G.spr_y + 4), 0xC1, 0x03, G.spr);
            ppu_on_all();

            while (true) ;

            static class G
            {
                public static byte spr_x;
                public static byte spr_y;
                public static byte spr;
            }
            """);

        Assert.Equal(
            "A926A28620118220AE82A97CA97C8D2603A96CA96C8D270320A885AD260338E904A0039122AD270338E904889122A9C0889122A903889122A90020FA858D250320A885AD260338E904A0039122AD2703186904889122A9C1889122A903889122AD250320FA858D25032089824C6C85",
            Convert.ToHexString(bytes));
    }

    [Fact]
    public void OamSpr_ConstantOnlyCompoundArg()
    {
        // Regression: oam_spr with constant-only compound expressions like
        // (byte)(0x41 | 0x43) in the attr arg caused KeyNotFoundException or
        // "Unsupported constant-only compound" because the backward scan
        // produced a compound arg with no source variable. The transpiler
        // should evaluate the expression at compile time.
        // Uses overlapping bits: 0x41 | 0x43 = 0x43 (but 0x41 + 0x43 = 0x84),
        // so the test distinguishes OR from accidental addition.
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
            // attr arg is a constant-only compound: (0x41 | 0x43) = 0x43
            oam_spr(40, 50, 0x10, (byte)(0x41 | 0x43), 0);
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);

        // The attr byte should be 0x43 (0x41 | 0x43 evaluated at compile time)
        // NOT 0x84 which would result from incorrect addition
        // In the decsp4 sequence: LDA #43 / DEY / STA ($22),Y
        Assert.Contains("A943", hex);
        Assert.DoesNotContain("A984", hex);
    }

    [Fact]
    public void MovingSprite_PadPollBranches()
    {
        // Ported from movingsprite sample (32 LOC).
        // Tests: pad_poll + conditional branches + oam_spr in a game loop.
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

            byte x = 40;
            byte y = 40;

            pal_all(PALETTE);
            ppu_on_all();

            while (true)
            {
                ppu_wait_nmi();

                PAD pad = pad_poll(0);

                if ((pad & PAD.LEFT) != 0) x--;
                if ((pad & PAD.RIGHT) != 0) x++;
                if ((pad & PAD.UP) != 0) y--;
                if ((pad & PAD.DOWN) != 0) y++;

                oam_spr(x, y, 0xD8, 0, 0);
                oam_spr((byte)(x + 8), y, 0xDA, 0, 4);
                oam_spr(x, (byte)(y + 8), 0xD9, 0, 8);
                oam_spr((byte)(x + 8), (byte)(y + 8), 0xDB, 0, 12);
            }
            """);

        Assert.Equal(
            "A9288D25038D2603A9D3A28620118220898220F082A9002056868D27032940F003CE2503AD27032980F003EE2503AD27032910F003CE2603AD27032920F003EE2603200486AD2503A0039122AD2603889122A9D8889122A90088912220A786200486AD2503186908A0039122AD2603889122A9DA889122A900889122A90420A786200486AD2503A0039122AD2603186908889122A9D9889122A900889122A90820A786200486AD2503186908A0039122AD2603186908889122A9DB889122A900889122A90C20A7864C1285",
            Convert.ToHexString(bytes));
    }
}
