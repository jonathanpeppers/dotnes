using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace dotnes.tests;

/// <summary>
/// A set of tests to assert a short C# program -> 6502 assembly result
/// </summary>
public class RoslynTests
{
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public RoslynTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    void AssertProgram(string csharpSource, string expectedAssembly)
    {
        _stream.SetLength(0);

        // Implicit global using
        csharpSource = $"using NES;using static NES.NESLib;{Environment.NewLine}{csharpSource}";

        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
        var systemPrivateCoreLib = typeof(object).Assembly.Location;
        var frameworkDir = Path.GetDirectoryName(systemPrivateCoreLib);
        Assert.NotNull(frameworkDir);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(systemPrivateCoreLib),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };

        var compilation = CSharpCompilation
            .Create(
                "hello.dll",
                [syntaxTree],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release,
                    deterministic: true));

        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
        {
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        }

        _stream.Seek(0, SeekOrigin.Begin);

        using var transpiler = new Transpiler(_stream, [new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")))], _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        var mainBlock = program.GetMainBlock();
        AssertEx.Equal(Utilities.ToByteArray(expectedAssembly), mainBlock);
    }

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
                A922
                A286
                202B82  ; JSR pal_bg
                A220
                A900
                20D483  ; JSR vram_adr
                AD2503
                209385  ; JSR pusha
                A203
                A9C0
                20DF83  ; JSR vram_fill
                A9E2
                A285
                20A985  ; JSR pushax
                A200
                A940
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C3185  ; JMP $8531
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
                A900
                A286
                201182  ; JSR pal_all
                A928
                208585  ; JSR popa
                A928
                208585  ; JSR popa
                A910
                208585  ; JSR popa
                A903
                208585  ; JSR popa
                A900
                20D485  ; JSR oam_spr
                208982  ; JSR ppu_on_all
                4C2385  ; JMP $8523
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
                A911
                A286
                201182  ; JSR pal_all
                A928
                8D2403  ; STA M0001
                A922
                A286
                AD2403  ; LDA M0001
                C928    ; CMP #$28
                D01D    ; BNE $8530
                AD2403  ; LDA M0001
                209685  ; JSR popa
                A928
                209685  ; JSR popa
                A910
                209685  ; JSR popa
                A903
                209685  ; JSR popa
                A900
                20E585  ; JSR oam_spr
                208982  ; JSR ppu_on_all
                4C3485
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
                8D2403      ; STA $0324 (store x to local)
                A922        ; LDA #$22
                A286        ; LDX #$86
                201182      ; JSR pal_all
                208982      ; JSR ppu_on_all
                20F082      ; JSR ppu_wait_nmi (loop start)
                A900        ; LDA #$00 (pad_poll argument)
                201886      ; JSR pad_poll
                8D2703      ; STA $0327 (store pad for reuse)
                2940        ; AND #$40 (PAD.LEFT)
                F003        ; BEQ +3 (skip DEC if zero)
                CE2403      ; DEC $0324 (x--)
                AD2403      ; LDA $0324 (load x for oam_spr)
                209D85      ; JSR pusha
                A928        ; LDA #$28 (y = 40)
                209D85      ; JSR pusha
                A9D8        ; LDA #$D8 (tile)
                209D85      ; JSR pusha
                A900        ; LDA #$00 (attr)
                209D85      ; JSR pusha
                A900        ; LDA #$00 (id)
                20EC85      ; JSR oam_spr
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
                A928        ; LDA #$28 (x = 40)
                8D2403      ; STA $0324
                A922        ; LDA #$22
                A286        ; LDX #$86
                A928        ; LDA #$28 (y = 40)
                8D2503      ; STA $0325
                A922        ; LDA #$22
                A286        ; LDX #$86
                201182      ; JSR pal_all
                208982      ; JSR ppu_on_all
                20F082      ; JSR ppu_wait_nmi (loop start)
                A900        ; LDA #$00
                204086      ; JSR pad_poll
                8D2703      ; STA $0327 (store pad for reuse)
                2940        ; AND #$40 (PAD.LEFT)
                F003        ; BEQ +3
                CE2403      ; DEC $0324 (x--)
                AD2703      ; LDA $0327 (reload pad)
                2980        ; AND #$80 (PAD.RIGHT)
                F003        ; BEQ +3
                EE2403      ; INC $0324 (x++)
                AD2703      ; LDA $0327 (reload pad)
                2910        ; AND #$10 (PAD.UP)
                F003        ; BEQ +3
                CE2503      ; DEC $0325 (y--)
                AD2703      ; LDA $0327 (reload pad)
                2920        ; AND #$20 (PAD.DOWN)
                F003        ; BEQ +3
                EE2503      ; INC $0325 (y++)
                AD2403      ; LDA $0324 (load x)
                20C585      ; JSR pusha
                AD2503      ; LDA $0325 (load y)
                20C585      ; JSR pusha
                A9D8        ; LDA #$D8 (tile)
                20C585      ; JSR pusha
                A900        ; LDA #$00 (attr)
                20C585      ; JSR pusha
                A900        ; LDA #$00 (id)
                201486      ; JSR oam_spr
                4C1885      ; JMP loop
                """);
    }
}
