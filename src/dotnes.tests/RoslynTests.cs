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

    byte[] GetProgramBytes(string csharpSource)
    {
        _stream.SetLength(0);
        csharpSource = $"using NES;using static NES.NESLib;{Environment.NewLine}{csharpSource}";
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
        var systemPrivateCoreLib = typeof(object).Assembly.Location;
        var frameworkDir = Path.GetDirectoryName(systemPrivateCoreLib)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(systemPrivateCoreLib),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(frameworkDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };
        var compilation = CSharpCompilation
            .Create("test.dll", [syntaxTree], references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true));
        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        _stream.Seek(0, SeekOrigin.Begin);
        using var transpiler = new Transpiler(_stream, [new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")))], _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        return program.GetMainBlock();
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
                A9F2A285
                201182  ; JSR pal_all
                A928    ; LDA #$28 (x = 40)
                8D2503  ; STA $0325
                A922A286
                AD2503  ; LDA $0325 (load x)
                C928    ; CMP #$28
                D01E    ; BNE skip
                207485  ; JSR decsp4
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
                20C685  ; JSR oam_spr
                208982  ; JSR ppu_on_all
                4C3885  ; JMP loop
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
    public void ArrayIndexers()
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
                ATTRIBUTE_TABLE[0] = 0x55; // This line is new
                pal_bg(PALETTE);
                vram_adr(NAMETABLE_A);
                vram_fill(0x16, 32 * 30);
                vram_write(ATTRIBUTE_TABLE);
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A9F1    ; LDA array_lo (ATTRIBUTE_TABLE)
                A285    ; LDX array_hi
                20B885  ; JSR pushax
                A200    ; LDX #$00
                A940    ; LDA #$40
                20A285  ; JSR pusha
                A900    ; LDA #$00
                20A285  ; JSR pusha
                A955    ; LDA #$55
                A931    ; LDA #$31 (PALETTE address lo)
                A286    ; LDX #$86 (PALETTE address hi)
                202B82  ; JSR pal_bg
                A220    ; LDX #$20
                A900    ; LDA #$00
                20D483  ; JSR vram_adr
                A916    ; LDA #$16
                20A285  ; JSR pusha
                A203    ; LDX #$03
                A9C0    ; LDA #$C0
                20DF83  ; JSR vram_fill
                A9F1    ; LDA array_lo (ATTRIBUTE_TABLE)
                A285    ; LDX array_hi
                20B885  ; JSR pushax
                A200    ; LDX #$00
                A940    ; LDA #$40
                204F83  ; JSR vram_write
                208982  ; JSR ppu_on_all
                4C4085  ; JMP loop
                """);
    }

    [Fact]
    public void ArrayIndexers_i4()
    {
        AssertProgram(
            csharpSource:
                """
                int[] data = new int[4] { 0x12345678, 0x11111111, 0x22222222, 0x33333333 };
                data[0] = 0x55;
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A904    ; LDA #$04
                207185  ; JSR pusha
                A900    ; LDA #$00
                207185  ; JSR pusha
                A955    ; LDA #$55
                208982  ; JSR ppu_on_all
                4C0F85  ; JMP $850F
                """);
    }

    [Fact]
    public void EnumVariable()
    {
        // Enums compile to plain integer IL — no special transpiler support needed
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x30);
            GameState state = GameState.Playing;
            if (state == GameState.Playing)
                pal_col(1, 0x14);
            if (state == GameState.GameOver)
                pal_col(2, 0x27);
            ppu_on_all();
            while (true) ;

            enum GameState : byte { Title, Playing, GameOver }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        // Verify key patterns: store enum value, compare, branch
        var hex = Convert.ToHexString(bytes);
        // GameState.Playing = 1, stored with LDA #$01 (A901)
        Assert.Contains("A901", hex);
        // CMP #$01 (C901) for == Playing
        Assert.Contains("C901", hex);
        // CMP #$02 (C902) for == GameOver
        Assert.Contains("C902", hex);
    }

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
    public void StructFieldAccess()
    {
        // Struct with byte fields — stfld/ldfld map to STA/LDA on zero page
        var bytes = GetProgramBytes(
            """
            Point p;
            p.X = 2;
            p.Y = 0x14;
            pal_col(0, p.X);
            pal_col(1, p.Y);
            ppu_on_all();
            while (true) ;

            struct Point { public byte X; public byte Y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$02 for p.X = 2
        Assert.Contains("A902", hex);
        // LDA #$14 for p.Y = 0x14
        Assert.Contains("A914", hex);
        // STA to zero page for stfld (85 = STA zero page)
        Assert.Contains("85", hex);
    }

    [Fact]
    public void StructFieldArithmetic()
    {
        // Read struct fields, do arithmetic, use result
        var bytes = GetProgramBytes(
            """
            Vec2 p;
            p.X = 5;
            p.Y = 3;
            byte sum = (byte)(p.X + p.Y);
            pal_col(0, sum);
            ppu_on_all();
            while (true) ;

            struct Vec2 { public byte X; public byte Y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$05 for p.X = 5, LDA #$03 for p.Y = 3
        Assert.Contains("A905", hex);
        Assert.Contains("A903", hex);
        // CLC + ADC pattern for add (18 = CLC, 65/6D = ADC)
        Assert.Contains("18", hex);
    }

    [Fact]
    public void FunctionReturnConstant()
    {
        // Simplest case: function returns a constant byte
        var bytes = GetProgramBytes(
            """
            pal_col(0, get_value());
            ppu_on_all();
            while (true) ;

            static byte get_value() => 5;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Main block should contain JSR to get_value and JSR to pal_col
        // LDA #$00 for pal_col first arg
        Assert.Contains("A900", hex);
        // Multiple JSR calls (20 = JSR opcode)
        Assert.True(hex.Split("20").Length >= 3, "Expected at least 2 JSR calls in main block");
    }

    [Fact]
    public void FunctionReturnParameter()
    {
        // Function takes a byte param and returns a computed value
        var bytes = GetProgramBytes(
            """
            pal_col(0, add_one(3));
            ppu_on_all();
            while (true) ;

            static byte add_one(byte x) => (byte)(x + 1);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$03 for the argument to add_one
        Assert.Contains("A903", hex);
    }

    [Fact]
    public void FunctionReturnUsedInExpression()
    {
        // Return value stored to local, then used as argument
        var bytes = GetProgramBytes(
            """
            byte r = get_value();
            pal_col(0, r);
            ppu_on_all();
            while (true) ;

            static byte get_value() => 0x14;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA to local variable (8D = STA absolute, for storing return value)
        Assert.Contains("8D", hex);
    }

    [Fact]
    public void FunctionReturnWithParamUsed()
    {
        // Return value from parameterized function used in pal_col
        // Verifies return value survives incsp1 parameter cleanup
        var bytes = GetProgramBytes(
            """
            pal_col(0, double_it(3));
            ppu_on_all();
            while (true) ;

            static byte double_it(byte x) => (byte)(x + x);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // LDA #$00 for pal_col first arg
        Assert.Contains("A900", hex);
        // LDA #$03 for double_it arg
        Assert.Contains("A903", hex);
        // 4 JSR calls: pusha, double_it, pal_col, ppu_on_all
        int jsrCount = hex.Split("20").Length - 1;
        Assert.True(jsrCount >= 4, $"Expected at least 4 JSR calls, got {jsrCount}");
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
    public void StaticFieldStore()
    {
        // Static field via class — produces stsfld/ldsfld IL
        var bytes = GetProgramBytes(
            """
            State.brightness = 4;
            pal_bright(State.brightness);
            ppu_on_all();
            while (true) ;

            static class State { public static byte brightness; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA absolute for store, LDA absolute for load
        Assert.Contains("8D", hex); // STA abs
        Assert.Contains("AD", hex); // LDA abs
    }

    [Fact]
    public void StaticFieldInLoop()
    {
        // Static field read and written in a loop — like climber.c globals
        var bytes = GetProgramBytes(
            """
            State.counter = 0;
            while (State.counter < 10) {
                pal_col(0, State.counter);
                State.counter = (byte)(State.counter + 1);
            }
            ppu_on_all();
            while (true) ;

            static class State { public static byte counter; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should have STA absolute and LDA absolute for the static field
        Assert.Contains("8D", hex);
        Assert.Contains("AD", hex);
    }

    [Fact]
    public void LocalVarInFunction()
    {
        // Local variable passed to a user function — same as static field version
        var bytes = GetProgramBytes(
            """
            byte brightness = 4;
            apply_bright(brightness);
            ppu_on_all();
            while (true) ;

            static void apply_bright(byte b) { pal_bright(b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should have JSR calls: pusha, apply_bright, ppu_on_all
        int jsrCount = hex.Split("20").Length - 1;
        Assert.True(jsrCount >= 2, $"Expected at least 2 JSR calls, got {jsrCount}");
    }

    [Fact]
    public void LocalVarInLoop()
    {
        // Same logic as StaticFieldInLoop but using locals (already works)
        var bytes = GetProgramBytes(
            """
            byte counter = 0;
            while (counter < 10) {
                pal_col(0, counter);
                counter = (byte)(counter + 1);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("8D", hex); // STA absolute for local
        Assert.Contains("AD", hex); // LDA absolute for local reload
    }

    [Fact]
    public void SbyteNegativeConstant()
    {
        // sbyte -1 should emit LDA #$FF (two's complement)
        var bytes = GetProgramBytes(
            """
            sbyte vel = -1;
            pal_col(0, (byte)vel);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FF", hex); // LDA #$FF (-1 in two's complement)
    }

    [Fact]
    public void SbyteNegativeArithmetic()
    {
        // sbyte arithmetic: -5 + 3 = -2 (0xFE)
        var bytes = GetProgramBytes(
            """
            sbyte x = -5;
            x = (sbyte)(x + 3);
            pal_col(0, (byte)x);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FB", hex); // LDA #$FB (-5 in two's complement)
    }

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
    public void StructArrayConstantIndex()
    {
        // Store and load struct fields via constant array index
        var bytes = GetProgramBytes(
            """
            Actor[] actors = new Actor[4];
            actors[0].x = 10;
            actors[1].y = 20;
            byte val = actors[0].x;
            pal_col(0, val);
            ppu_on_all();
            while (true) ;

            struct Actor { public byte x; public byte y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90A", hex); // LDA #$0A (10)
        Assert.Contains("A914", hex); // LDA #$14 (20)
        Assert.Contains("8D", hex); // STA absolute
        Assert.Contains("AD", hex); // LDA absolute (loading actors[0].x)
    }

    [Fact]
    public void StructArrayFieldReadWrite()
    {
        // Write to one element, read from another, pass to function
        var bytes = GetProgramBytes(
            """
            Actor[] actors = new Actor[2];
            actors[0].x = 42;
            actors[1].x = 99;
            pal_col(0, actors[0].x);
            ppu_on_all();
            while (true) ;

            struct Actor { public byte x; public byte y; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A92A", hex); // LDA #$2A (42)
        Assert.Contains("A963", hex); // LDA #$63 (99)
        Assert.Contains("8D", hex); // STA absolute
        Assert.Contains("AD", hex); // LDA absolute (loading from actors[0].x)
    }

    [Fact]
    public void StructArrayThreeFields()
    {
        // Struct with 3 fields to verify field offset calculation
        var bytes = GetProgramBytes(
            """
            Pos[] positions = new Pos[2];
            positions[0].x = 10;
            positions[0].y = 20;
            positions[0].flags = 3;
            positions[1].x = 40;
            pal_col(0, positions[1].x);
            ppu_on_all();
            while (true) ;

            struct Pos { public byte x; public byte y; public byte flags; }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90A", hex); // LDA #10
        Assert.Contains("A914", hex); // LDA #20
        Assert.Contains("A903", hex); // LDA #3
        Assert.Contains("A928", hex); // LDA #40
    }
}
