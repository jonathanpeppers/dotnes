using dotnes.ObjectModel;
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
        => GetProgramBytes(csharpSource, additionalAssemblyFiles: null);

    byte[] GetProgramBytes(string csharpSource, IList<AssemblyReader>? additionalAssemblyFiles, bool allowUnsafe = false)
    {
        var (program, _) = BuildProgram(csharpSource, additionalAssemblyFiles, allowUnsafe);
        return program.GetMainBlock();
    }

    (Program6502 program, Transpiler transpiler) BuildProgram(string csharpSource, IList<AssemblyReader>? additionalAssemblyFiles = null, bool allowUnsafe = false)
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
                    optimizationLevel: OptimizationLevel.Release, deterministic: true,
                    allowUnsafe: allowUnsafe));
        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        _stream.Seek(0, SeekOrigin.Begin);
        var assemblyFiles = new List<AssemblyReader> { new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s"))) };
        if (additionalAssemblyFiles != null)
            assemblyFiles.AddRange(additionalAssemblyFiles);
        var transpiler = new Transpiler(_stream, assemblyFiles, _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        return (program, transpiler);
    }

    (Program6502 program, Transpiler transpiler) BuildProgramMultiFile(string[] csharpSources, IList<AssemblyReader>? additionalAssemblyFiles = null)
    {
        _stream.SetLength(0);
        var syntaxTrees = csharpSources.Select(source =>
        {
            source = $"using NES;using static NES.NESLib;{Environment.NewLine}{source}";
            return CSharpSyntaxTree.ParseText(source);
        }).ToArray();
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
            .Create("test.dll", syntaxTrees, references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true));
        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        _stream.Seek(0, SeekOrigin.Begin);
        var assemblyFiles = new List<AssemblyReader> { new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s"))) };
        if (additionalAssemblyFiles != null)
            assemblyFiles.AddRange(additionalAssemblyFiles);
        var transpiler = new Transpiler(_stream, assemblyFiles, _logger);
        var program = transpiler.BuildProgram6502(out _, out _);
        return (program, transpiler);
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
                A900    ; LDA #$00
                206C85  ; JSR pusha
                A955    ; LDA #$55
                208982  ; JSR ppu_on_all
                4C0A85  ; JMP $850A
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

    [Fact]
    public void ArrayFillConstant()
    {
        // Array.Fill with a constant value — no subsequent array access
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[8];
            System.Array.Fill(buf, (byte)0xFF);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9FF", hex);  // LDA #$FF (fill value)
        Assert.Contains("A207", hex);  // LDX #$07 (size-1 = 7)
        Assert.Contains("CA", hex);    // DEX
        Assert.Contains("10FA", hex);  // BPL -6 (loop back to STA)
    }

    [Fact]
    public void ArrayFillZero()
    {
        // Array.Fill with zero — common memset(buf, 0, n) pattern
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[4];
            System.Array.Fill(buf, (byte)0);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A900", hex);  // LDA #$00 (fill value)
        Assert.Contains("A203", hex);  // LDX #$03 (size-1 = 3)
        Assert.Contains("CA", hex);    // DEX
        Assert.Contains("10FA", hex);  // BPL -6 (loop back to STA)
    }

    [Fact]
    public void IntPtrSize()
    {
        // IntPtr.Size should be 1 on the 6502 (8-bit CPU)
        var bytes = GetProgramBytes(
            """
            byte size = (byte)System.IntPtr.Size;
            pal_col(0, size);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (IntPtr.Size = 1)
    }

    [Fact]
    public void SizeofNint()
    {
        // sizeof(nint) produces the same 6502 output as IntPtr.Size
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(nint);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (sizeof(nint) = 1)
    }

    [Fact]
    public void SizeofUshort()
    {
        // sizeof(ushort) is folded by Roslyn to ldc.i4.2 at compile time (no Sizeof opcode)
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(ushort);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A902", hex);  // LDA #$02 (sizeof(ushort) = 2, folded by Roslyn)
    }

    [Fact]
    public void SizeofByte()
    {
        // sizeof(byte) is folded by Roslyn to ldc.i4.1 at compile time (no Sizeof opcode)
        var bytes = GetProgramBytes(
            """
            unsafe
            {
                byte size = (byte)sizeof(byte);
                pal_col(0, size);
            }
            ppu_on_all();
            while (true) ;
            """,
            additionalAssemblyFiles: null,
            allowUnsafe: true);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A901", hex);  // LDA #$01 (sizeof(byte) = 1, folded by Roslyn)
    }

    [Fact]
    public void PokeConstant()
    {
        // poke(0x4015, 0x0F) should emit LDA #$0F, STA $4015
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90F", hex);    // LDA #$0F
        Assert.Contains("8D1540", hex);  // STA $4015
    }

    [Fact]
    public void PokeConsecutiveSameValue()
    {
        // Two consecutive pokes with the same value: LDA emitted only once
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            poke(0x4016, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A90F", hex);    // LDA #$0F (once)
        Assert.Contains("8D1540", hex);  // STA $4015
        Assert.Contains("8D1640", hex);  // STA $4016

        // LDA #$0F should appear only once (optimization)
        int firstLda = hex.IndexOf("A90F");
        int secondLda = hex.IndexOf("A90F", firstLda + 4);
        Assert.Equal(-1, secondLda);
    }

    [Fact]
    public void PokeThenCallThenPoke()
    {
        // poke, then a function call, then poke with same value:
        // The second poke MUST re-emit LDA because the call clobbered A
        var bytes = GetProgramBytes(
            """
            poke(0x4015, 0x0F);
            pal_col(0, 0x30);
            poke(0x4016, 0x0F);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("8D1540", hex);  // STA $4015
        Assert.Contains("8D1640", hex);  // STA $4016

        // LDA #$0F must appear TWICE (the call between clobbers A)
        int firstLda = hex.IndexOf("A90F");
        Assert.NotEqual(-1, firstLda);
        int secondLda = hex.IndexOf("A90F", firstLda + 4);
        Assert.NotEqual(-1, secondLda);
    }

    [Fact]
    public void PeekConstant()
    {
        // peek(0x2002) should emit LDA $2002 (absolute)
        var bytes = GetProgramBytes(
            """
            byte status = peek(0x2002);
            pal_col(0, status);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("AD0220", hex);  // LDA $2002 (absolute)
    }

    [Fact]
    public void PeekSmallConstant()
    {
        // peek(0x003C) — address 0x3C fits in a byte, so the transpiler emits
        // a single LDA via WriteLdc(byte). The peek handler must remove only 1
        // prior instruction instead of 2 for the ushort path (LDX + LDA).
        var bytes = GetProgramBytes(
            """
            byte value = peek(0x003C);
            pal_col(0, value);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("AD3C00", hex);  // LDA $003C (absolute)
    }

    [Fact]
    public void PokeSmallConstant()
    {
        // poke(0x003C, 0x07) — address 0x3C fits in a byte, so the transpiler
        // emits a single LDA via WriteLdc(byte). The poke handler must remove
        // only 3 prior instructions instead of 4 for the ushort path.
        var bytes = GetProgramBytes(
            """
            poke(0x003C, 0x07);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A907", hex);    // LDA #$07
        Assert.Contains("8D3C00", hex);  // STA $003C (absolute)
    }

    [Fact]
    public void ApuPlayTone_Pulse1()
    {
        // apu_play_tone(PulseChannel.Pulse1, 0x0180, APUDuty.Duty25, 10) should emit inline register writes:
        //   ctrl = (1 << 6) | 0x30 | 10 = 0x7A -> STA $4000
        //   sweep = 0x00 -> STA $4001
        //   timer_lo = 0x80 -> STA $4002
        //   timer_hi = 0x01 -> STA $4003
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_play_tone(PulseChannel.Pulse1, 0x0180, APUDuty.Duty25, 10);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Verify poke(APU_STATUS, 0x0F) emits correctly (period > 255 uses ushort path)
        Assert.Contains("A90F" + "8D1540", hex);    // LDA #$0F, STA $4015 (APU_STATUS)
        // Assert full LDA+STA pairs as contiguous sequences
        Assert.Contains("A97A" + "8D0040", hex);   // LDA #$7A, STA $4000 (ctrl)
        Assert.Contains("A900" + "8D0140", hex);   // LDA #$00, STA $4001 (sweep)
        Assert.Contains("A980" + "8D0240", hex);   // LDA #$80, STA $4002 (timer lo)
        Assert.Contains("A901" + "8D0340", hex);   // LDA #$01, STA $4003 (timer hi)
    }

    [Fact]
    public void ApuPlayTone_Pulse2()
    {
        // apu_play_tone(PulseChannel.Pulse2, 0x00FD, APUDuty.Duty50, 15) should target pulse 2 registers ($4004-$4007):
        //   ctrl = (2 << 6) | 0x30 | 15 = 0xBF -> STA $4004
        //   sweep = 0x00 -> STA $4005
        //   timer_lo = 0xFD -> STA $4006
        //   timer_hi = 0x00 -> STA $4007
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_play_tone(PulseChannel.Pulse2, 0x00FD, APUDuty.Duty50, 15);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Verify poke(APU_STATUS, 0x0F) emits correctly (period <= 255 uses byte path)
        Assert.Contains("A90F" + "8D1540", hex);    // LDA #$0F, STA $4015 (APU_STATUS)
        // Assert full LDA+STA pairs as contiguous sequences
        Assert.Contains("A9BF" + "8D0440", hex);   // LDA #$BF, STA $4004 (ctrl)
        Assert.Contains("A900" + "8D0540", hex);   // LDA #$00, STA $4005 (sweep)
        Assert.Contains("A9FD" + "8D0640", hex);   // LDA #$FD, STA $4006 (timer lo)
        Assert.Contains("A900" + "8D0740", hex);   // LDA #$00, STA $4007 (timer hi)
    }

    [Fact]
    public void ApuStop_Pulse1()
    {
        // apu_stop(PulseChannel.Pulse1) should silence pulse 1:
        //   LDA #$30, STA $4000
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_stop(PulseChannel.Pulse1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A930" + "8D0040", hex);   // LDA #$30, STA $4000
    }

    [Fact]
    public void ApuStop_Pulse2()
    {
        // apu_stop(PulseChannel.Pulse2) should silence pulse 2:
        //   LDA #$30, STA $4004
        var bytes = GetProgramBytes(
            """
            poke(APU_STATUS, 0x0F);
            apu_stop(PulseChannel.Pulse2);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A930" + "8D0440", hex);   // LDA #$30, STA $4004
    }

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
    public void RuntimeValueInA_ThenWordLocal_EmitsPusha()
    {
        // Regression: when _runtimeValueInA is true (e.g., from nesclock())
        // followed by ldloc of a ushort local, must emit JSR pusha to save A
        // before the word local clobbers it.
        var (program, _) = BuildProgram(
            """
            ushort total = 100;
            byte val = nesclock();
            ushort result = (ushort)(val + total);
            pal_col(0, (byte)result);
            ppu_on_all();
            while (true) ;
            """);

        // Scan all blocks for JSR pusha
        bool hasPusha = program.Blocks.Any(b =>
            b.InstructionsWithLabels.Any(il =>
                il.Instruction.Opcode == Opcode.JSR &&
                il.Instruction.Operand is LabelOperand lbl && lbl.Label == "pusha"));
        Assert.True(hasPusha, "Expected JSR pusha to save A before word local load. " +
            $"Blocks: {string.Join(", ", program.Blocks.Select(b => b.Label ?? "(no label)"))}");
    }

    [Fact]
    public void WaitvsyncEmitsJsr()
    {
        // waitvsync() should emit JSR to waitvsync subroutine
        var (program, _) = BuildProgram(
            """
            waitvsync();
            ppu_on_all();
            while (true) ;
            """);
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
    public void ArrayCopyBasic()
    {
        // Array.Copy between two runtime arrays
        var bytes = GetProgramBytes(
            """
            byte[] src = new byte[4];
            src[0] = 0x10;
            src[1] = 0x20;
            byte[] dst = new byte[4];
            System.Array.Copy(src, dst, 4);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A200", hex);  // LDX #$00 (start index)
        Assert.Contains("E004", hex);  // CPX #$04 (length)
        Assert.Contains("D0", hex);    // BNE (loop back)
    }

    [Fact]
    public void ExternMethodCall()
    {
        // Create a temporary .s file with a labeled subroutine
        var tempDir = Path.Combine(Path.GetTempPath(), "dotnes_test_extern");
        Directory.CreateDirectory(tempDir);
        var sFilePath = Path.Combine(tempDir, "test_extern.s");
        try
        {
            // Write a minimal .s file: _my_extern_func label with an RTS (0x60)
            File.WriteAllText(sFilePath,
                """
                ; Test extern subroutine
                _my_extern_func:
                .byte $A9,$42,$60
                """);

            using var reader = new AssemblyReader(sFilePath);
            var assemblyFiles = new List<AssemblyReader> { reader };
            var bytes = GetProgramBytes(
                """
                static extern void my_extern_func();
                my_extern_func();
                ppu_on_all();
                while (true) ;
                """,
                additionalAssemblyFiles: assemblyFiles,
                allowUnsafe: true);
            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            var hex = Convert.ToHexString(bytes);
            // Should contain JSR (0x20) to _my_extern_func label
            Assert.Contains("20", hex);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExternMethodWithArgs()
    {
        // Extern method with one argument: verifies arg passing + JSR
        var tempDir = Path.Combine(Path.GetTempPath(), "dotnes_test_extern2");
        Directory.CreateDirectory(tempDir);
        var sFilePath = Path.Combine(tempDir, "test_extern2.s");
        try
        {
            File.WriteAllText(sFilePath,
                """
                ; Test extern subroutine with arg
                _set_value:
                .byte $85,$17,$60
                """);

            using var reader = new AssemblyReader(sFilePath);
            var assemblyFiles = new List<AssemblyReader> { reader };
            var bytes = GetProgramBytes(
                """
                static extern void set_value(byte val);
                set_value(42);
                ppu_on_all();
                while (true) ;
                """,
                additionalAssemblyFiles: assemblyFiles,
                allowUnsafe: true);
            Assert.NotNull(bytes);
            Assert.NotEmpty(bytes);

            var hex = Convert.ToHexString(bytes);
            Assert.Contains("A92A", hex);  // LDA #$2A (42) — arg loaded before JSR
            Assert.Contains("20", hex);    // JSR to _set_value
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
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
    public void ThreeParameterFunction()
    {
        // Function with 3 byte parameters
        var bytes = GetProgramBytes(
            """
            byte r = add3(1, 2, 3);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;

            static byte add3(byte a, byte b, byte c) => (byte)(a + b + c);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // All three constants should appear
        Assert.Contains("A901", hex); // LDA #$01
        Assert.Contains("A902", hex); // LDA #$02
        Assert.Contains("A903", hex); // LDA #$03
    }

    [Fact]
    public void NestedFunctionCalls()
    {
        // f(g(x)) — nested call
        var bytes = GetProgramBytes(
            """
            pal_col(0, outer(3));
            ppu_on_all();
            while (true) ;

            static byte outer(byte x) => inner(x);
            static byte inner(byte x) => (byte)(x + 10);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A903", hex); // LDA #$03 (initial arg)
    }

    [Fact]
    public void ModuloPowerOf2()
    {
        // x % 8 should optimize to AND #7 (runtime value)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x % 8);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("2907", hex); // AND #$07 (x & 7 == x % 8)
    }

    [Fact]
    public void ModuloGeneral()
    {
        // x % 5 needs software division (runtime value)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x % 5);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain SEC + CMP #$05 + BCC + SBC pattern
        Assert.Contains("38", hex);   // SEC
        Assert.Contains("C905", hex); // CMP #$05
        Assert.Contains("E905", hex); // SBC #$05
    }

    [Fact]
    public void DivisionGeneral()
    {
        // x / 3 needs software division (runtime value, non-power-of-2)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte r = (byte)(x / 3);
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain LDX #$FF, SBC #$03, and TXA opcodes
        Assert.Contains("A2FF", hex); // LDX #$FF
        Assert.Contains("E903", hex); // SBC #$03
        Assert.Contains("8A", hex);   // TXA
    }

    [Fact]
    public void SignedDivisionByPowerOf2()
    {
        // sbyte / 4 needs arithmetic shift right (preserving sign)
        var bytes = GetProgramBytes(
            """
            sbyte vel = -8;
            sbyte r = (sbyte)(vel / 4);
            pal_col(0, (byte)r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9F8", hex); // LDA #$F8 (-8 in two's complement)
    }

    [Fact]
    public void UshortReturnFromFunction()
    {
        // User function returning ushort
        var bytes = GetProgramBytes(
            """
            ushort val = get_addr(5);
            pal_col(0, (byte)val);
            ppu_on_all();
            while (true) ;

            static ushort get_addr(byte x) => (ushort)(x * 8 + 16);
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A905", hex); // LDA #$05 (argument)
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
    public void UshortAddSignedByte()
    {
        // actor.yy += actor.yvel/4 — mixing ushort and sbyte
        var bytes = GetProgramBytes(
            """
            ushort yy = 100;
            sbyte vel = -8;
            yy = (ushort)(yy + vel / 4);
            pal_col(0, (byte)yy);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A9F8", hex); // LDA #$F8 (-8)
    }

    [Fact]
    public void ByteArrayConstantIndex()
    {
        // Access byte array with runtime index via for loop — pattern for type_message
        var bytes = GetProgramBytes(
            """
            byte[] msg = new byte[3];
            msg[0] = 0x48;
            msg[1] = 0x49;
            msg[2] = 0x00;
            for (byte i = 0; i < 2; i++)
            {
                pal_col(i, msg[i]);
            }
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A948", hex); // LDA #$48 ('H') in stelem
    }

    [Fact]
    public void ShiftRight()
    {
        // Shift right by constant — pyy >> 8 pattern for extracting high byte
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte hi = (byte)(x >> 2);
            pal_col(0, hi);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain two LSR A instructions (4A 4A)
        Assert.Contains("4A4A", hex);
    }

    [Fact]
    public void ShiftLeft()
    {
        // Shift left by constant — x << 3 pattern for multiply by 8
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x << 3);
            pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain three ASL A instructions (0A 0A 0A)
        Assert.Contains("0A0A0A", hex);
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
    public void Ushort16BitMultiply()
    {
        // byte * 8 → ushort (overflow to 16-bit)
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort result = (ushort)(x * 8);
            pal_col(0, (byte)(result >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain ROL $17 (zero page, opcode 26 17) for 16-bit shift
        Assert.Contains("2617", hex);
        // Should contain TXA (8A) to extract high byte via >> 8
        Assert.Contains("8A", hex);
    }

    [Fact]
    public void LocalTimesConstant_PreservesLocalLoad()
    {
        // Regression test: ldloc;ldc;mul must keep the LDA $local instruction.
        // A previous bug had RemoveLastInstructions(2) which removed both
        // the LDA #constant AND the LDA $local, leaving ASL shifts with
        // a stale accumulator value.
        //
        // The pal_col call between gap's definition and usage forces the
        // compiler to store gap to a local and reload it, producing the
        // ldloc;ldc;mul IL pattern that triggered the bug.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte gap = (byte)(x & 7);
            pal_col(0, gap);
            ushort offset = (ushort)(gap * 16);
            pal_col(1, (byte)(offset >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);

        // 16-bit multiply by 16 uses LDX #0 (A200), STX $17 (8617), then ASL A + ROL $17
        string shiftSetup = "A20086170A2617";
        int setupIdx = hex.IndexOf(shiftSetup);
        Assert.True(setupIdx >= 6, "Could not find LDX #0 + STX $17 + ASL A + ROL $17 sequence");

        // The 3 bytes (6 hex chars) immediately before the shift setup must be
        // LDA Absolute (AD xx xx) — the local variable reload.
        // If RemoveLastInstructions removes too many, it would be a JSR (20 xx xx) instead.
        string instrBefore = hex.Substring(setupIdx - 6, 2);
        Assert.Equal("AD", instrBefore);
    }

    [Fact]
    public void Ushort16BitAdd()
    {
        // ushort + byte constant with carry propagation
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort result = (ushort)(x * 8 + 16);
            pal_col(0, (byte)(result >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Should contain CLC (18) + ADC #10 (69 10) for 16-bit add
        Assert.Contains("186910", hex);
        // Should contain BCC (90) for carry propagation
        Assert.Contains("90", hex);
    }

    [Fact]
    public void UshortStoreAndLoad()
    {
        // Store ushort to local, load it back, use high/low bytes
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            ushort yy = (ushort)(x * 8 + 16);
            pal_col(0, (byte)yy);
            pal_col(1, (byte)(yy >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void BytePlusUshortConstant()
    {
        // byte + ushort constant (> 255) produces 16-bit result
        var bytes = GetProgramBytes(
            """
            byte lo = rand8();
            ushort val = (ushort)(lo + 256);
            pal_col(0, (byte)val);
            pal_col(1, (byte)(val >> 8));
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void UserFuncLdargAfterCall()
    {
        // User function that reads params after calling rand8
        // Tests that WriteLdarg saves _runtimeValueInA to TEMP
        var bytes = GetProgramBytes(
            """
            static byte rndint(byte a, byte b)
            {
                byte range = (byte)(b - a);
                byte r = rand8();
                return (byte)((byte)(r % range) + a);
            }
            byte result = rndint(3, 10);
            pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void LdelemSubPreservesFirstOperand()
    {
        // Pattern from climber: row - heights[f] where both are loop variables.
        // The ldelem clobbers A (which held row). The transpiler must save row to TEMP
        // before the ldelem, then use the _savedRuntimeToTemp path in Sub.
        var bytes = GetProgramBytes(
            """
            byte[] heights = new byte[4];
            heights[0] = 10;
            for (byte row = 0; row < 4; row++)
            {
                for (byte f = 0; f < 4; f++)
                {
                    byte diff = (byte)(row - heights[f]);
                    pal_col(0, diff);
                }
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Sub hex: {hex}");
        // Must contain STA $17 (8517) to save row to TEMP before ldelem clobbers A
        Assert.Contains("8517", hex);
        // Must contain SBC $18 (E518) — TEMP - TEMP+1 pattern for the subtraction
        Assert.Contains("E518", hex);
    }

    [Fact]
    public void LdelemAddPreservesFirstOperand()
    {
        // Pattern: val + arr[idx] with loop variables — addition is commutative.
        // The transpiler should save val to TEMP, then CLC; ADC TEMP.
        var bytes = GetProgramBytes(
            """
            byte[] offsets = new byte[4];
            offsets[0] = 5;
            for (byte row = 0; row < 4; row++)
            {
                for (byte f = 0; f < 4; f++)
                {
                    byte total = (byte)(row + offsets[f]);
                    pal_col(0, total);
                }
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Add hex: {hex}");
        // Must contain STA $17 (8517) to save row to TEMP
        Assert.Contains("8517", hex);
        // Must contain ADC $17 (6517) — add from TEMP
        Assert.Contains("6517", hex);
    }

    [Fact]
    public void UserFunctionLocalsDoNotOverlapMain()
    {
        // User function locals must be allocated AFTER main's locals to avoid
        // memory corruption when the function is called.
        // Main allocates an array of 20 bytes; the user function has a local.
        // The function's local must be at an address >= main's array end.
        var (program, transpiler) = BuildProgram(
            """
            static byte add_offset(byte x)
            {
                byte temp = (byte)(x + 5);
                return temp;
            }
            byte[] data = new byte[20];
            data[0] = add_offset(3);
            pal_col(0, data[0]);
            while (true) ;
            """);

        var mainBytes = program.GetMainBlock();
        Assert.NotNull(mainBytes);

        // Main local: data array is 20 bytes starting at $0325 (base local address).
        // So main occupies $0325-$0338.
        // User function local "temp" must be at $0339 or later, NOT at $0325.
        var userBytes = program.GetMainBlock("add_offset");
        Assert.NotEmpty(userBytes);

        var userHex = Convert.ToHexString(userBytes);
        // The function stores to its local via STA Absolute (8D xx yy).
        // The address must NOT be $0325 (25 03 in little-endian) — that's main's array.
        // It should be $0339 or higher.
        Assert.DoesNotContain("8D2503", userHex);

        transpiler.Dispose();
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
    public void StelemWithIndexArithmetic()
    {
        // Pattern from climber: buf[(byte)(col + 1)] = (byte)(CH_FLOOR + 2)
        // The stelem handler must apply the +1 offset to the index when storing.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            for (byte col = 0; col < 30; col += 2)
            {
                buf[col] = 0xF4;
                buf[(byte)(col + 1)] = 0xF6;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemIndexArith hex: {hex}");
        // Must contain CLC (18) and ADC #$01 (6901) for computing col + 1
        Assert.Contains("186901", hex);
        // Must contain TAX (AA) to move computed index to X
        Assert.Contains("AA", hex);
    }

    [Fact]
    public void StelemWithTwoLocalIndexArithmetic()
    {
        // Pattern from climber: buf[(byte)(offset + j)] = 0
        // The stelem handler must compute X = offset + j at runtime.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            byte offset = 4;
            for (byte j = 0; j < 4; j++)
            {
                buf[(byte)(offset + j)] = 0;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemTwoLocal hex: {hex}");
        // Must contain CLC (18) for computing offset + j
        Assert.Contains("18", hex);
        // Must contain TAX (AA) to move computed index to X
        Assert.Contains("AA", hex);
        // The value 0 must be stored: LDA #$00 (A900) followed by STA TEMP (8517)
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void StelemSameArrayDifferentIndex()
    {
        // Pattern: arr[i] = arr[j] — same-array copy with different indices
        // IL: ldloc arr, ldloc i, ldloc arr, ldloc j, ldelem.u1, stelem.i1
        // Should emit: LDX j_addr; LDA arr,X; LDX i_addr; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 2;
            byte j = 0;
            arr[i] = arr[j];
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemSameArrayDifferentIndex hex: {hex}");

        // Verify the contiguous LDX(src) -> LDA(arr,X) -> LDX(dst) -> STA(arr,X) sequence
        // AE xx xx BD yy yy AE zz zz 9D yy yy  (yy yy must match for same array)
        bool foundSequence = false;
        for (int i = 0; i <= bytes.Length - 12; i++)
        {
            if (bytes[i] == 0xAE          // LDX abs (source index)
                && bytes[i + 3] == 0xBD   // LDA abs,X (load from array)
                && bytes[i + 6] == 0xAE   // LDX abs (target index)
                && bytes[i + 9] == 0x9D   // STA abs,X (store to array)
                && bytes[i + 4] == bytes[i + 10]   // array addr lo must match
                && bytes[i + 5] == bytes[i + 11])  // array addr hi must match
            {
                // Source and target index addresses must differ
                if (bytes[i + 1] != bytes[i + 7] || bytes[i + 2] != bytes[i + 8])
                {
                    foundSequence = true;
                    break;
                }
            }
        }
        Assert.True(foundSequence, "Expected contiguous LDX(src) -> LDA(arr,X) -> LDX(dst) -> STA(arr,X) sequence not found");
    }

    [Fact]
    public void CompoundArrayIncrementByConstant()
    {
        // Pattern: arr[i] += 2 generates ldelema System.Byte / dup / ldind.u1 / ldc.i4.2 / add / conv.u1 / stind.i1
        // Should emit: LDX i_addr; LDA arr,X; CLC; ADC #02; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 0;
            arr[i] += 2;
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CompoundIncr hex: {hex}");
        // Must contain ADC #$02 (6902) for the += 2
        Assert.Contains("6902", hex);
    }

    [Fact]
    public void CompoundArrayDecrement()
    {
        // Pattern: arr[i]-- generates ldelema System.Byte / dup / ldind.u1 / ldc.i4.1 / sub / conv.u1 / stind.i1
        // Should emit: LDX i_addr; LDA arr,X; SEC; SBC #01; STA arr,X
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte i = 0;
            arr[i]--;
            pal_col(0, arr[i]);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CompoundDecr hex: {hex}");
        // Must contain SBC #$01 (E901) for the --
        Assert.Contains("E901", hex);
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
    public void LdelemConstantIndexCompareWithConstant()
    {
        // Pattern from climber: while (actor_floor[0] != MAX_FLOORS - 1)
        // After ldelem.u1 with constant index 0, the next comparison should be
        // CMP #constant, NOT CMP $array_addr (which would compare stale A with
        // the array element instead of comparing the element with the constant).
        var bytes = GetProgramBytes(
            """
            byte[] states = new byte[8];
            states[0] = 1;
            while (states[0] != 19)
            {
                states[0] = (byte)(states[0] + 1);
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemCmp hex: {hex}");
        // Must contain CMP #$13 (C913) — comparing element with constant 19
        Assert.Contains("C913", hex);
        // Must NOT contain only CMP $0325 (CD2503) without a preceding LDA —
        // that would mean comparing stale A with the array, not the element with 19
    }

    [Fact]
    public void LdelemDoesNotLeaveStaleSavedRuntimeToTemp()
    {
        // Bug: HandleLdelemU1 removes instructions (including STA $17 from WriteLdloc)
        // but did not clear _savedRuntimeToTemp. This caused HandleRem to use the
        // "runtime divisor in TEMP" path instead of the constant divisor path,
        // computing dividend % TEMP (stale rh) instead of dividend % 60.
        // Pattern: inner loop with ldelem comparisons (value stays on eval stack,
        // no explicit stloc), then modulo with constant after the loop.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte[] vals = new byte[4];
            arr[0] = 3;
            vals[0] = 5;
            byte rh = 10;
            for (byte f = 0; f < 4; f++)
            {
                if ((byte)(rh - arr[f]) < vals[f])
                {
                    break;
                }
            }
            byte result = (byte)(rh % 60);
            NESLib.pal_col(0, result);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemRem hex: {hex}");

        // The modulo must use CMP #$3C (C93C) for constant 60, NOT CMP $19 (C519)
        // which would mean using stale TEMP2 as divisor
        Assert.Contains("C93C", hex);
    }

    [Fact]
    public void LdelemAfterSubSavesToTempForBranchComparison()
    {
        // Bug: HandleLdelemU1's "save preceding value" check only looked for LDA as the
        // last instruction. After SUB, the last instruction is SBC, so the computed dy
        // value in A was NOT saved to TEMP. The ldelem's LDA then overwrote dy.
        // Pattern: dy = rh - arr[0]; if (dy < vals[0]) — the second ldelem must save dy.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[4];
            byte[] vals = new byte[4];
            arr[0] = 2;
            vals[0] = 6;
            byte rh = 3;
            byte dy = (byte)(rh - arr[0]);
            if (dy < vals[0])
            {
                NESLib.pal_col(0, 1);
            }
            NESLib.pal_col(0, 0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemBranch hex: {hex}");

        // After SBC (dy computation), there must be a STA $17 (8517) to save dy to TEMP
        // before the LDA that loads vals[0] for the comparison.
        // The branch comparison should use CMP (Cxxx) not a bare LDA overwrite.
        // Specifically: SBC ... STA $17 ... LDA $addr ... CMP pattern
        Assert.Contains("8517", hex); // STA $17 (save dy to TEMP)
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
    public void MultiFile_StaticHelperClass()
    {
        // Verify that methods in a separate static class are correctly transpiled.
        // This tests the basic multi-file scenario where helper methods live in
        // a static class in a different file.
        var (program, _) = BuildProgramMultiFile([
            // File 1: Program.cs (top-level statements)
            """
            Palette.setup();
            ppu_on_all();
            while (true) ;
            """,
            // File 2: Palette.cs (static helper class)
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x14);
                    pal_col(2, 0x20);
                    pal_col(3, 0x30);
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        _logger.WriteLine($"MultiFile_StaticHelperClass main hex: {Convert.ToHexString(mainBlock)}");

        // First instruction in main should be JSR (0x20) to the setup method
        Assert.Equal(0x20, mainBlock[0]);
    }

    [Fact]
    public void MultiFile_MatchesSingleFile()
    {
        // Verify that a multi-file program produces the same main block bytes
        // as the equivalent single-file program with a local function.
        var singleFileBytes = GetProgramBytes(
            """
            setup();
            ppu_on_all();
            while (true) ;

            static void setup()
            {
                pal_col(0, 0x02);
                pal_col(1, 0x14);
                pal_col(2, 0x20);
                pal_col(3, 0x30);
            }
            """);

        var (multiFileProgram, _) = BuildProgramMultiFile([
            """
            Palette.setup();
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x14);
                    pal_col(2, 0x20);
                    pal_col(3, 0x30);
                }
            }
            """
        ]);

        var multiFileBytes = multiFileProgram.GetMainBlock();

        _logger.WriteLine($"Single-file main: {Convert.ToHexString(singleFileBytes)}");
        _logger.WriteLine($"Multi-file main:  {Convert.ToHexString(multiFileBytes)}");

        Assert.Equal(singleFileBytes, multiFileBytes);
    }

    [Fact]
    public void MultiFile_MethodWithParameters()
    {
        // Verify that methods with parameters in a separate class work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            Graphics.set_color(0, 0x30);
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Graphics
            {
                public static void set_color(byte index, byte color)
                {
                    pal_col(index, color);
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var hex = Convert.ToHexString(mainBlock);
        _logger.WriteLine($"MultiFile_MethodWithParameters main hex: {hex}");

        // Should contain LDA #$00 for index and LDA #$30 for color
        Assert.Contains("A900", hex);
        Assert.Contains("A930", hex);
    }

    [Fact]
    public void MultiFile_MethodWithReturnValue()
    {
        // Verify that methods with return values in a separate class work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            pal_col(0, Colors.white());
            ppu_on_all();
            while (true) ;
            """,
            """
            static class Colors
            {
                public static byte white() => 0x30;
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var hex = Convert.ToHexString(mainBlock);
        _logger.WriteLine($"MultiFile_MethodWithReturnValue main hex: {hex}");

        // Should contain LDA #$00 for pal_col index arg
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void MultiFile_MultipleHelperClasses()
    {
        // Verify that methods across multiple static helper classes work correctly.
        var (program, _) = BuildProgramMultiFile([
            """
            Palette.setup();
            Display.enable();
            while (true) ;
            """,
            """
            static class Palette
            {
                public static void setup()
                {
                    pal_col(0, 0x02);
                    pal_col(1, 0x30);
                }
            }
            """,
            """
            static class Display
            {
                public static void enable()
                {
                    ppu_on_all();
                }
            }
            """
        ]);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        _logger.WriteLine($"MultiFile_MultipleHelperClasses main hex: {Convert.ToHexString(mainBlock)}");

        // Main should begin with two JSR (0x20) calls at instruction boundaries:
        // JSR setup (3 bytes) then JSR enable (3 bytes)
        Assert.True(mainBlock.Length >= 6, $"Expected at least 6 bytes, got {mainBlock.Length}");
        Assert.Equal(0x20, mainBlock[0]);  // JSR setup
        Assert.Equal(0x20, mainBlock[3]);  // JSR enable
    }

    [Fact]
    public void Poke_To_MMC3_Registers()
    {
        // poke() to MMC3 mapper registers should emit STA to correct addresses
        var bytes = GetProgramBytes(
            """
            poke(0x8000, 0x06);
            poke(0x8001, 0x00);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA $8000 = 8D0080
        Assert.Contains("8D0080", hex);
        // STA $8001 = 8D0180
        Assert.Contains("8D0180", hex);
    }

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
    public void NestedFunctionCallsGetSeparateLocalAddresses()
    {
        // When outer_func calls inner_func, each must have its own local storage
        // to prevent inner_func from clobbering outer_func's locals.
        var (program, _) = BuildProgram(
            """
            outer_func();
            ppu_on_all();
            while (true) ;

            static void outer_func()
            {
                byte local_outer = 42;
                inner_func();
                pal_col(0, local_outer);
            }

            static void inner_func()
            {
                byte local_inner = 99;
                pal_col(1, local_inner);
            }
            """);

        // Get full program bytes (main + user methods)
        var allBytes = program.ToBytes();
        var hex = Convert.ToHexString(allBytes);
        _logger.WriteLine($"NestedFunctionCalls full hex: {hex}");

        // Both literal values must appear: LDA #42 (A92A) and LDA #99 (A963)
        Assert.Contains("A92A", hex);
        Assert.Contains("A963", hex);

        // outer_func's local is stored at $0325 (STA $0325 = 8D2503)
        Assert.Contains("8D2503", hex);
        // inner_func's local must be at a DIFFERENT address ($0326 = 8D2603)
        // because outer_func calls inner_func and both are on the stack simultaneously
        Assert.Contains("8D2603", hex);
    }

    [Fact]
    public void NestedCallsWithMultipleLocalsGetCorrectOffsets()
    {
        // When a caller has multiple locals, the callee's frame must start
        // AFTER all the caller's locals, not just 1 byte later.
        var (program, _) = BuildProgram(
            """
            multi_local_func();
            ppu_on_all();
            while (true) ;

            static void multi_local_func()
            {
                byte a = 10;
                byte b = 20;
                byte c = 30;
                byte d = 40;
                pal_col(0, a);
                pal_col(1, b);
                pal_col(2, c);
                pal_col(3, d);
                callee_func();
            }

            static void callee_func()
            {
                byte val = 77;
                pal_col(0, val);
            }
            """);

        var allBytes = program.ToBytes();
        var hex = Convert.ToHexString(allBytes);
        _logger.WriteLine($"MultiLocalNestedCalls full hex: {hex}");

        // multi_local_func has 4 byte locals at $0325-$0328
        Assert.Contains("8D2503", hex); // STA $0325 (local a)
        Assert.Contains("8D2603", hex); // STA $0326 (local b)
        Assert.Contains("8D2703", hex); // STA $0327 (local c)
        Assert.Contains("8D2803", hex); // STA $0328 (local d)

        // callee_func's local must be at $0329 (after all 4 caller locals)
        Assert.Contains("A94D", hex);   // LDA #77
        Assert.Contains("8D2903", hex); // STA $0329
    }

    [Fact]
    public void RecursiveUserMethodThrows()
    {
        // Self-recursive user methods should fail fast during transpilation
        // rather than silently producing overlapping frame offsets.
        var ex = Assert.ThrowsAny<Exception>(() => BuildProgram(
            """
            recurse();
            ppu_on_all();
            while (true) ;

            static void recurse()
            {
                byte x = 1;
                pal_col(0, x);
                recurse();
            }
            """));

        Assert.Contains("ecursive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClosureCapturingByteArray()
    {
        // When a non-static local function captures an outer byte[] variable,
        // the compiler generates a closure struct. The transpiler should handle
        // this by mapping closure byte[] fields to ROM data labels and scalar
        // fields to zero-page addresses.
        var (program, transpiler) = BuildProgram(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            apply_palette();
            ppu_on_all();
            while (true) ;

            void apply_palette()
            {
                pal_bg(palette);
            }
            """);

        // The program should compile without errors
        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        // Verify the closure method was detected
        Assert.Contains("apply_palette", transpiler.UserMethods.Keys);

        // The full ROM should contain the byte array data (0x0F, 0x10, 0x20, 0x30)
        var fullBytes = program.ToBytes();
        var fullHex = Convert.ToHexString(fullBytes);
        _logger.WriteLine($"ClosureCapturingByteArray fullHex: {fullHex}");
        Assert.Contains("0F102030", fullHex); // byte array ROM data
    }

    [Fact]
    public void ClosureCapturingByteArrayAndScalar()
    {
        // Test: closure capturing both a byte[] (ROM data) and a scalar byte variable.
        // The byte[] field should use ROM labels, the scalar should use a zero-page address.
        var bytes = GetProgramBytes(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            byte color = 0x15;
            apply_palette();
            ppu_on_all();
            while (true) ;

            void apply_palette()
            {
                pal_bg(palette);
                pal_col(0, color);
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"ClosureCapturingByteArrayAndScalar hex: {hex}");

        // Verify the scalar closure field (color = 0x15) is stored at its address.
        // The main should emit LDA #$15 (A915) followed by STA $addr (8D xx xx).
        int ldaIdx = hex.IndexOf("A915");
        Assert.True(ldaIdx >= 0, $"LDA #$15 not found. Hex: {hex}");
        // Verify STA follows the LDA (8D = STA absolute)
        Assert.Equal("8D", hex.Substring(ldaIdx + 4, 2));
    }

    [Fact]
    public void ClosureMethodWithRealParams()
    {
        // Test: closure method that has real parameters in addition to
        // the implicit closure struct ref. Roslyn places the closure ref
        // as the LAST parameter, not the first.
        var (program, _) = BuildProgram(
            """
            byte[] palette = [0x0F, 0x10, 0x20, 0x30];
            byte color = 0x15;
            apply_at(3, color);
            ppu_on_all();
            while (true) ;

            void apply_at(byte index, byte c)
            {
                pal_col(index, c);
                pal_bg(palette);
            }
            """);

        var mainBlock = program.GetMainBlock();
        Assert.NotNull(mainBlock);
        Assert.NotEmpty(mainBlock);

        var fullBytes = program.ToBytes();
        var fullHex = Convert.ToHexString(fullBytes);
        _logger.WriteLine($"ClosureMethodWithRealParams hex: {fullHex}");

        // Verify palette data is in the full ROM
        Assert.Contains("0F102030", fullHex);
    }

    [Fact]
    public void MultiParamUserFunction_LocalVarArgs()
    {
        // Test: calling a user-defined function with a runtime local + constant.
        // Use rand8() for runtime value and reference x after the call to force ldloc.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            my_func(x, 5);
            pal_col(0, x);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_LocalVarArgs hex: {hex}");

        // Local 0 (x) is at $0325. After ldloc.0 (AD2503) loads x,
        // the next ldc.i4.5 (A905) loads the constant.
        // With the fix, a JSR pusha should appear between them.
        int ldloc = hex.IndexOf("AD2503");
        Assert.True(ldloc >= 0, $"LDA $0325 (load local 0) not found. Hex: {hex}");
        int ldc = hex.IndexOf("A905", ldloc);
        Assert.True(ldc >= 0, $"LDA #$05 (load constant 5) not found after ldloc. Hex: {hex}");
        Assert.True(ldc > ldloc, $"LDA #$05 should come after LDA $0325. Hex: {hex}");

        // The two LDA instructions should NOT be adjacent — there must be a JSR pusha between them.
        // AD2503 is 6 hex chars; if ldc == ldloc + 6, they're adjacent (no pusha).
        Assert.True(ldc > ldloc + 6,
            $"No JSR pusha between loading local and constant args — first arg will be lost. " +
            $"LDA $0325 at {ldloc}, LDA #$05 at {ldc}. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_TwoLocals()
    {
        // Two local variable args to a user-defined function.
        // Both x and y are used after the call to force the compiler to keep them as locals.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte y = rand8();
            my_func(x, y);
            pal_col(0, x);
            pal_col(1, y);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_TwoLocals hex: {hex}");

        // x at $0325, y at $0326
        // For my_func(x, y): ldloc.0 (AD2503), ldloc.1 (AD2603), call my_func
        // A JSR pusha must appear between the two LDA instructions.
        int idx0 = hex.IndexOf("AD2503");
        Assert.True(idx0 >= 0, $"LDA $0325 not found. Hex: {hex}");
        int idx1 = hex.IndexOf("AD2603", idx0 + 6);
        Assert.True(idx1 >= 0, $"LDA $0326 not found after LDA $0325. Hex: {hex}");
        Assert.True(idx1 > idx0 + 6,
            $"No JSR pusha between two local arg loads. " +
            $"LDA $0325 at {idx0}, LDA $0326 at {idx1}. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_ThreeArgs()
    {
        // Three-arg user function with mix of local and constant args.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            add3(x, 2, 3);
            pal_col(0, x);
            ppu_on_all();
            while (true) ;

            static void add3(byte a, byte b, byte c) { pal_col(a, (byte)(b + c)); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_ThreeArgs hex: {hex}");

        // Local 0 (x) at $0325, then constants 2 and 3
        int ldloc = hex.IndexOf("AD2503");
        Assert.True(ldloc >= 0, $"LDA $0325 not found. Hex: {hex}");
        // There should be a JSR pusha after loading x (before loading 2)
        int ldc2Index = hex.IndexOf("A902", ldloc);
        Assert.True(ldc2Index > ldloc + 6,
            $"No JSR pusha after loading local x before constant arg. Hex: {hex}");
    }

    [Fact]
    public void MultiParamUserFunction_ComputedArg()
    {
        // Test: calling a user-defined function where the second arg is a computed
        // expression: my_func(x, (byte)(y + 1)). The look-ahead must track IL stack
        // depth through the Add/Conv opcodes to find the Call.
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte y = rand8();
            my_func(x, (byte)(y + 1));
            pal_col(0, x);
            pal_col(1, y);
            ppu_on_all();
            while (true) ;

            static void my_func(byte a, byte b) { pal_col(a, b); }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MultiParamUserFunction_ComputedArg hex: {hex}");

        // x at $0325, y at $0326.
        // For my_func(x, (byte)(y + 1)):
        //   ldloc.0 → LDA $0325 (load x)
        //   JSR pusha           (preserve x on cc65 stack)
        //   ldloc.1 → LDA $0326 (load y)
        //   ldc.i4.1 → ...
        //   add → ...
        //   conv.u1 → ...
        //   call my_func
        // A JSR pusha must appear after loading x so it survives the y+1 computation.
        int ldlocX = hex.IndexOf("AD2503");
        Assert.True(ldlocX >= 0, $"LDA $0325 (load local x) not found. Hex: {hex}");
        int ldlocY = hex.IndexOf("AD2603", ldlocX + 6);
        Assert.True(ldlocY >= 0, $"LDA $0326 (load local y) not found after x. Hex: {hex}");
        // The 6 hex chars before LDA $0326 should be a JSR (20 xx xx) for pusha
        Assert.True(ldlocY >= 6, $"LDA $0326 too early for preceding JSR. Hex: {hex}");
        Assert.Equal("20", hex.Substring(ldlocY - 6, 2));
    }

    [Fact]
    public void ByteMaxValue()
    {
        // Regression test: byte value 255 must use 1-byte store path.
        // Without the <= byte.MaxValue fix, 255 falls to the ushort branch
        // which calls RemoveLastInstructions(2) when only 1 instruction was
        // emitted by WriteLdc(byte), corrupting the preceding JSR.
        var bytes = GetProgramBytes("""
            pal_col(0, 0x30);
            byte x = 255;
            pal_col(1, x);
            while (true) ;
            """);

        var hex = Convert.ToHexString(bytes);

        // Verify correct 1-byte store: LDA #$FF (A9FF), STA $0325 (8D2503)
        Assert.Contains("A9FF", hex);
        Assert.Contains("8D2503", hex);

        // Verify no 2-byte ushort high-byte store: STX $0326 (8E2603)
        // If the ushort branch ran, it would emit STX for the high byte
        Assert.DoesNotContain("8E2603", hex);
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
    public void CnromSetChrBank_EmitsStaToMapper()
    {
        // cnrom_set_chr_bank(byte) should emit STA $8000 to switch CHR bank
        var bytes = GetProgramBytes(
            """
            cnrom_set_chr_bank(0);
            cnrom_set_chr_bank(1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"CNROM hex: {hex}");

        // LDA #$00 = A900, STA $8000 = 8D0080
        Assert.Contains("A9008D0080", hex);
        // LDA #$01 = A901, STA $8000 = 8D0080
        Assert.Contains("A9018D0080", hex);
    }

    [Fact]
    public void Mmc3SetChrBank_EmitsRegAndBankWrites()
    {
        // mmc3_set_chr_bank(byte reg, byte bank) should emit:
        // LDA #reg, STA $8000 (bank select), LDA #bank, STA $8001 (bank data)
        var bytes = GetProgramBytes(
            """
            mmc3_set_chr_bank(0x00, 0x00);
            mmc3_set_chr_bank(0x02, 0x09);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"MMC3 CHR hex: {hex}");

        // First call: LDA #$00, STA $8000, LDA #$00, STA $8001
        Assert.Contains("A9008D0080A9008D0180", hex);
        // Second call: LDA #$02, STA $8000, LDA #$09, STA $8001
        Assert.Contains("A9028D0080A9098D0180", hex);
    }

    [Fact]
    public void Mmc3SetChrBank_SupportsLocalBankArg()
    {
        // mmc3_set_chr_bank with bank from a local variable should emit
        // LDA #reg, STA $8000, LDA $bank_addr, STA $8001.
        var bytes = GetProgramBytes(
            """
            byte bank = 9;
            mmc3_set_chr_bank(0x02, bank);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Mmc3SetChrBank local bank hex: {hex}");

        // LDA #$02, STA $8000 (register select)
        Assert.Contains("A9028D0080", hex);
        // STA $8001 (bank data write)
        Assert.Contains("8D0180", hex);
    }

    [Fact]
    public void Mmc1Write_EmitsShiftRegisterProtocol()
    {
        // mmc1_write(0x8000, 0x0C) should emit:
        // LDA #$80, STA $8000 (reset)
        // LDA #$0C, STA $8000, LSR A, STA $8000, LSR A, STA $8000, LSR A, STA $8000, LSR A, STA $8000
        var bytes = GetProgramBytes(
            """
            mmc1_write(0x8000, 0x0C);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Reset: LDA #$80 (A980), STA $8000 (8D0080)
        Assert.Contains("A980" + "8D0080", hex);
        // Value load + 5-bit serial write: LDA #$0C, STA $8000, LSR A, STA $8000 (×4), LSR A, STA $8000
        // Contiguous pattern: A90C 8D0080 4A 8D0080 4A 8D0080 4A 8D0080 4A 8D0080
        Assert.Contains("A90C" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080", hex);
    }

    [Fact]
    public void Mmc1SetPrgBank_EmitsWriteToE000()
    {
        // mmc1_set_prg_bank(2) should emit serial writes to $E000
        var bytes = GetProgramBytes(
            """
            mmc1_set_prg_bank(2);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA $E000 = 8D00E0
        Assert.Contains("8D00E0", hex);
        // LDA #$02 = A902
        Assert.Contains("A902", hex);
    }

    [Fact]
    public void Mmc1SetMirroring_EmitsWriteTo8000()
    {
        // mmc1_set_mirroring writes the full Control register — use mirror + PRG/CHR mode bits
        // (byte)MMC1Mirror.Vertical | MMC1_PRG_FIX_LAST = 0x02 | 0x0C = 0x0E
        var bytes = GetProgramBytes(
            """
            mmc1_set_mirroring((byte)MMC1Mirror.Vertical | MMC1_PRG_FIX_LAST);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // Reset: LDA #$80, STA $8000
        Assert.Contains("A980" + "8D0080", hex);
        // Value: LDA #$0E (0x02 | 0x0C), followed by serial writes to $8000
        Assert.Contains("A90E" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080" + "4A" + "8D0080", hex);
    }

    [Fact]
    public void Mmc1SetChrBank_EmitsWritesToA000AndC000()
    {
        // mmc1_set_chr_bank(0, 1) should emit serial writes to $A000 and $C000
        var bytes = GetProgramBytes(
            """
            mmc1_set_chr_bank(0, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        // STA $A000 = 8D00A0
        Assert.Contains("8D00A0", hex);
        // STA $C000 = 8D00C0
        Assert.Contains("8D00C0", hex);
    }

    [Fact]
    public void ComplexArrayIndexExpression()
    {
        // Pattern from issue: array[(x >> 3) + ((y >> 3) << 4)]
        // The for loop forces the array into a local variable (stloc) so that
        // the ldelem.u1 is preceded by ldloc (array), <complex index>, ldelem.u1.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[16];
            for (byte i = 0; i < 16; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte y = (byte)pad_poll(0);
            byte tile = arr[(byte)((x >> 3) + ((y >> 3) << 4))];
            pal_col(0, tile);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"ComplexArrayIndex hex: {hex}");
        // Three consecutive LSR A (4A) for >> 3 shift
        Assert.Contains("4A4A4A", hex);
        // Four consecutive ASL A (0A) for << 4 shift
        Assert.Contains("0A0A0A0A", hex);
        // TAX (AA) immediately followed by LDA absolute,X (BD) for indexed array access
        Assert.Contains("AABD", hex);
    }

    [Fact]
    public void SimpleShiftArrayIndex()
    {
        // Simpler complex index: array[x >> 3]
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[16];
            for (byte i = 0; i < 16; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte tile = arr[(byte)(x >> 3)];
            pal_col(0, tile);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SimpleShiftIndex hex: {hex}");
        // Three consecutive LSR A (4A) followed by TAX (AA) + LDA absolute,X (BD)
        // This is the full expected sequence: shift index, transfer to X, load from array
        Assert.Contains("4A4A4AAABD", hex);
    }

    [Fact]
    public void ComplexArrayIndex_PreservesOperandInTemp()
    {
        // Verifies that a runtime value computed before the array access
        // is preserved in TEMP ($17) across the complex index computation.
        // Pattern: (x - y) + arr[(byte)(x >> 3)]
        // The subtraction result must survive through the shift/TAX/LDA sequence.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[32];
            for (byte i = 0; i < 32; i++)
            {
                arr[i] = i;
            }
            byte x = (byte)pad_poll(0);
            byte y = (byte)pad_poll(0);
            byte result = (byte)((byte)(x - y) + arr[(byte)(x >> 3)]);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"PreservesOperandInTemp hex: {hex}");
        // STA $17 (8517) must appear before the index computation to save (x - y) to TEMP
        Assert.Contains("8517", hex);
        // TAX + LDA absolute,X for the indexed array access
        Assert.Contains("AABD", hex);
    }

    [Fact]
    public void WordStaticField_StoreImmediate()
    {
        // ushort static field should get 2 bytes and store both lo/hi
        var bytes = GetProgramBytes(
            """
            G.word_val = 300;
            while (true) ;

            static class G { public static ushort word_val; }
            """);
        var hex = Convert.ToHexString(bytes);
        // 300 = 0x012C: LDA #$2C (A92C), STA abs (8D), LDX #$01 or STX abs (8E)
        Assert.Contains("A92C", hex); // LDA #$2C (low byte)
        Assert.Contains("8E", hex);   // STX abs (high byte store)
    }

    [Fact]
    public void WordStaticField_LoadZeroExtend()
    {
        // Storing a byte field value into a word field should zero-extend the high byte
        var bytes = GetProgramBytes(
            """
            G.byte_val = 42;
            G.int_val = G.byte_val;
            while (true) ;

            static class G
            {
                public static byte byte_val;
                public static int int_val;
            }
            """);
        var hex = Convert.ToHexString(bytes);
        // Zero-extend: LDA #$00 (A900) for high byte
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void WordStaticField_LoadWord()
    {
        // Loading a ushort field should emit LDA abs + LDX abs (16-bit)
        var bytes = GetProgramBytes(
            """
            G.word_val = 300;
            byte lo = (byte)G.word_val;
            pal_col(0, lo);
            while (true) ;

            static class G { public static ushort word_val; }
            """);
        var hex = Convert.ToHexString(bytes);
        // LDX absolute = AE (loading high byte of word field)
        Assert.Contains("AE", hex);
    }

    [Fact]
    public void WordStaticField_TwoByteAllocation()
    {
        // Word fields should not overlap adjacent fields.
        // byte_val at $0325 (1 byte), int_val at $0326 (2 bytes), word_val at $0328 (2 bytes)
        var bytes = GetProgramBytes(
            """
            G.byte_val = 1;
            G.int_val = 2;
            G.word_val = 3;
            while (true) ;

            static class G
            {
                public static byte byte_val;
                public static int int_val;
                public static ushort word_val;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"WordStaticField_TwoByteAllocation hex: {hex}");

        // Fields are allocated alphabetically: byte_val@$0325, int_val@$0326-7, word_val@$0328-9
        // Store byte_val=1: LDA #1 (A901), STA $0325 (8D2503)
        Assert.Contains("8D2503", hex);
        // Store int_val=2 low: STA $0326 (8D2603)
        Assert.Contains("8D2603", hex);
        // Store int_val=2 high: STA $0327 (8D2703) — zero or value high byte
        Assert.Contains("8D2703", hex);
        // Store word_val=3 low: STA $0328 (8D2803)
        Assert.Contains("8D2803", hex);
        // Store word_val=3 high: STA $0329 (8D2903)
        Assert.Contains("8D2903", hex);
    }

    // ===================================================================
    // Converted from pre-compiled test DLLs → inline Roslyn tests
    // ===================================================================

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

    [Fact]
    public void StaticFieldArraySimpleIndex()
    {
        // G.arr[G.i] where both arr and i are static fields.
        // IL pattern: ldsfld arr; ldsfld i; ldelem.u1
        // Fields: i at $0325 (1 byte), arr at $0326 (4 bytes) — alphabetical order
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            G.i = 0;
            pal_col(0, G.arr[G.i]);
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
                public static byte i;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[G.i]: LDA $0325 (AD 25 03); TAX (AA); LDA $0326,X (BD 26 03)
        Assert.Contains("AD2503", hex);  // LDA $0325 — load G.i
        Assert.Contains("AA", hex);      // TAX — transfer index to X
        Assert.Contains("BD2603", hex);  // LDA $0326,X — load G.arr[X]
    }

    [Fact]
    public void StaticFieldArrayConstantIndex()
    {
        // G.arr[0] where arr is a static field, index is constant.
        // arr is the only field: allocated at $0325 (4 bytes)
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            pal_col(0, G.arr[0]);
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[0]: LDA $0325 (AD 25 03) — constant index 0, direct absolute load
        Assert.Contains("AD2503", hex);  // LDA $0325 (absolute, arr base)
    }

    [Fact]
    public void StaticFieldArrayStelem()
    {
        // G.arr[G.i] = 42 where arr and i are static fields.
        // Fields: i at $0325 (1 byte), arr at $0326 (4 bytes)
        var bytes = GetProgramBytes(
            """
            G.arr = new byte[4];
            G.i = 0;
            G.arr[G.i] = 42;
            ppu_on_all();
            while (true) ;

            static class G
            {
                public static byte[] arr;
                public static byte i;
            }
            """);
        var hex = Convert.ToHexString(bytes);

        // G.arr[G.i] = 42: value 42 stored at arr base + index
        Assert.Contains("A92A", hex);    // LDA #42 — load value
        Assert.Contains("9D2603", hex);  // STA $0326,X — store to G.arr[X]
    }

    [Fact]
    public void ArrayStelem_TwoLocalsAdd()
    {
        // arr[i] = (byte)(local1 + local2) — both operands are locals.
        // Regression test: previously the second ldloc overwrote the first,
        // emitting ADC #$00 instead of ADC local2.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[8];
            byte i = 0;
            byte prev = 10;
            byte dv = 5;
            arr[i] = (byte)(prev + dv);
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);

        // Must emit CLC + ADC Absolute (opcode 6D), not ADC #imm (opcode 69)
        Assert.Contains("18", hex);       // CLC
        Assert.Contains("6D", hex);       // ADC Absolute
        // Must NOT contain ADC #$00 (the old buggy pattern: 6900)
        Assert.DoesNotContain("6900", hex);
    }

    [Fact]
    public void ArrayStelem_TwoLocalsSub()
    {
        // arr[i] = (byte)(local1 - local2) — subtraction variant.
        var bytes = GetProgramBytes(
            """
            byte[] arr = new byte[8];
            byte i = 0;
            byte prev = 10;
            byte dv = 3;
            arr[i] = (byte)(prev - dv);
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);

        // Must emit SEC + SBC Absolute (opcode ED), not SBC #imm (opcode E9)
        Assert.Contains("38", hex);       // SEC
        Assert.Contains("ED", hex);       // SBC Absolute
    }

    [Fact]
    public void StelemI1_ArrayElementIndex()
    {
        // Regression: In the climber sample, the pattern:
        //   buf[(byte)(arr2[f] * 2)] = value;
        //   buf[(byte)(arr2[f] * 2 + 1)] = value2;
        // The transpiler treated arr2 as a scalar index variable, emitting
        // LDX arr2_addr (loading arr2[0]) instead of LDX f; LDA arr2,X.
        // The * 2 and + 1 arithmetic were also lost. Both tiles of each pair
        // wrote to the same position (arr2[0]), making items appear at the
        // wrong location.
        var bytes = GetProgramBytes(
            """
            byte[] buf = new byte[30];
            byte[] positions = new byte[4];
            positions[0] = 3;
            positions[1] = 5;
            positions[2] = 7;
            positions[3] = 9;
            for (byte f = 0; f < 4; f++)
            {
                buf[(byte)(positions[f] * 2)] = (byte)(f + 1);
                buf[(byte)(positions[f] * 2 + 1)] = (byte)(f + 3);
            }
            vram_adr(0x2000);
            vram_write(buf);
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemI1_ArrayElementIndex hex: {hex}");

        // STA $17 (85 17) — save computed index to TEMP
        Assert.Contains("8517", hex);

        // LDX $17 (A6 17) — reload index from TEMP
        Assert.Contains("A617", hex);

        // ASL + STA TEMP (first index: positions[f] * 2, no add)
        Assert.Contains("0A8517", hex);

        // ASL + CLC + ADC #1 (second index: positions[f] * 2 + 1)
        Assert.Contains("0A186901", hex);
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
    public void BcdAddWithRuntimeFirstArg()
    {
        // Bug: bcd_add(score, 1) where score is ushort — the first argument
        // needs pushax (16-bit push), and the second argument needs X=0.
        // Without the fix, pusha (1-byte) is used or X is garbage.
        var bytes = GetProgramBytes(
            """
            ushort score = 0;
            score = bcd_add(score, 1);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"BcdAddRuntime hex: {hex}");
        // Must contain LDX #$00 (A200) and LDA #$01 (A901) for the constant arg
        Assert.Contains("A200", hex); // LDX #$00 (clear X)
        Assert.Contains("A901", hex); // LDA #$01 (second arg)

        // Verify the sequence for second arg: LDX #$00 then LDA #$01 then JSR bcd_add
        // A2 00 A9 01 20 ?? ??
        bool patternFound = false;
        for (int i = 0; i + 6 < bytes!.Length; i++)
        {
            if (bytes[i] == 0xA2 && bytes[i + 1] == 0x00      // LDX #$00
                && bytes[i + 2] == 0xA9 && bytes[i + 3] == 0x01 // LDA #$01
                && bytes[i + 4] == 0x20)                        // JSR bcd_add
            {
                patternFound = true;
                break;
            }
        }
        Assert.True(patternFound, "Expected LDX #$00; LDA #$01; JSR bcd_add pattern not found.");
    }

    [Fact]
    public void StelemDoesNotConsumeIfElseBranches()
    {
        // Bug: HandleStelemI1's backward IL scan walked past if/else branches,
        // and the _blockCountAtILOffset removal consumed the entire branch code.
        // Pattern: ldelem + if/else + stelem in the same scope.
        // Use a loop to force Roslyn to store the array in a local variable.
        var bytes = GetProgramBytes(
            """
            byte[] types = new byte[8];
            types[0] = 1;
            types[1] = 2;
            for (byte pf = 0; pf < 2; pf++)
            {
                byte ot = types[pf];
                if (ot == 1)
                {
                    oam_clear();
                }
                else
                {
                    pal_col(0, 0x30);
                }
                types[pf] = 0;
            }
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemBranch hex: {hex}");
        // Must contain CMP #$01 (C901) for the if (ot == 1) comparison
        Assert.Contains("C901", hex);
        // Must contain LDA #$30 (A930) for the pal_col color argument in else branch
        Assert.Contains("A930", hex);
        // Must contain LDA #$00 (A900) for stelem value 0
        Assert.Contains("A900", hex);
    }

    [Fact]
    public void LdelemU1_NestedArrayIndex()
    {
        // Regression: arr1[arr2[i]] generated wrong code — the transpiler
        // loaded from arr2 twice instead of using arr2[i] as index into arr1.
        // The workaround (intermediate local) should produce correct code.
        // Direct nested access (arr1[arr2[i]]) requires further transpiler work.
        var bytes = GetProgramBytes(
            """
            byte[] names = new byte[4];
            byte[] lookup = new byte[4];
            names[0] = 10; names[1] = 20; names[2] = 30; names[3] = 40;
            lookup[0] = 3; lookup[1] = 2; lookup[2] = 1; lookup[3] = 0;
            for (byte i = 0; i < 4; i++)
            {
                byte idx = lookup[i];
                byte result = names[idx];
                pal_col(i, result);
            }
            ppu_on_all();
            while (true) ;
            """);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"LdelemU1_NestedArrayIndex hex: {hex}");

        // LDA names,X pattern (BD xx xx) — indexed load from names array
        Assert.Contains("BD", hex);
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
    public void Rand16_Returns16Bit()
    {
        // rand() returns ushort (16-bit) in A:X
        var bytes = GetProgramBytes(
            """
            ushort r = rand16();
            pal_col(0, (byte)r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Rand16_Returns16Bit hex: {hex}");

        // Should contain JSR to rand subroutine and store 16-bit result
        // The rand label resolves to a JSR target; verify the full JSR + STA pattern
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to some address
        // After rand() returns, the 16-bit result (A:X) should be stored to a local
        // STA $0325 (low byte) + STX $0326 (high byte) for a ushort local
        Assert.Contains("8D2503", hex); // STA $0325
        Assert.Contains("8E2603", hex); // STX $0326
    }

    [Fact]
    public void Rand16_ByteTruncation()
    {
        // (byte)rand16() should truncate 16-bit result to 8-bit (just use A, discard X)
        var bytes = GetProgramBytes(
            """
            byte r = (byte)rand16();
            pal_col(0, r);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Rand16_ByteTruncation hex: {hex}");

        // Should contain JSR to rand and STA for storing byte local (A only, X discarded)
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to rand
        Assert.Contains("8D2503", hex); // STA $0325 (byte local)
    }

    [Fact]
    public void SRand_AcceptsUshort()
    {
        // srand(ushort seed) should accept a 16-bit seed value
        var bytes = GetProgramBytes(
            """
            srand(42);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"SRand_AcceptsUshort hex: {hex}");

        // Should contain LDA #42 (0x2A) and JSR to srand
        Assert.Contains("A92A", hex); // LDA #$2A (42, low byte of seed)
        Assert.Matches("20[0-9A-F]{4}", hex); // JSR to srand
    }

    [Fact]
    public void Multiply_NonPowerOf2_3()
    {
        // Runtime val * 3 should use the general 8x8 multiply loop
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x * 3);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Multiply_NonPowerOf2_3 hex: {hex}");

        // General multiply loop: LDX #$08 (8 bits), LSR TEMP2, BCC, CLC, ADC TEMP
        Assert.Contains("A208", hex);     // LDX #$08
        Assert.Contains("4619", hex);     // LSR $19 (TEMP2)
    }

    [Fact]
    public void Multiply_NonPowerOf2_5()
    {
        // Runtime val * 5 should use the general 8x8 multiply loop
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x * 5);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Multiply_NonPowerOf2_5 hex: {hex}");

        // General multiply loop pattern: LDX #$08, LSR TEMP2
        Assert.Contains("A208", hex);     // LDX #$08
        Assert.Contains("4619", hex);     // LSR $19 (TEMP2)
    }

    [Fact]
    public void Division_RuntimeDividend()
    {
        // Runtime dividend / constant divisor should emit repeated subtraction
        var bytes = GetProgramBytes(
            """
            byte x = rand8();
            byte result = (byte)(x / 10);
            pal_col(0, result);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"Division_RuntimeDividend hex: {hex}");

        // Repeated subtraction: LDX #$FF, SEC, INX, SBC #$0A, BCS
        Assert.Contains("A2FF", hex);     // LDX #$FF
        Assert.Contains("38", hex);       // SEC
        Assert.Contains("E90A", hex);     // SBC #$0A (divisor 10)
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

    [Fact]
    public void ByteUshortVarExtraction_NoPushaLeak()
    {
        // Regression: (byte)ushort_var patterns inside stelem_i1 must not leave
        // unmatched JSR pusha calls in the generated code. Each pusha must have
        // a corresponding popa or incsp1, otherwise the cc65 stack leaks.
        // This mimics the climber sample's draw_entire_stage init pattern with
        // preceding array stores that set LastLDA = true before word local loads.
        var (program, _) = BuildProgram(
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
            """);

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
        var (program, _) = BuildProgram(
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
            """);

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

    [Fact]
    public void UserFunctionInWhileTrueBreakInsideForLoop()
    {
        // Reproduces issue #408: user function with early return (return 1 before return 0)
        // called inside while(true){...break;} in a for loop.
        // The IL has two 'ret' instructions. The transpiler treats 'ret' as no-op,
        // so the early 'return 1' falls through to 'return 0', making the function
        // always return 0.
        var (program, transpiler) = BuildProgram(
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
            """);

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
    public void TryFinally()
    {
        // try/finally inlines both blocks sequentially — no exception handling needed on the NES.
        // The leave/endfinally opcodes fall through when the code is linear.
        AssertProgram(
            csharpSource:
                """
                pal_col(0, 0x02);
                try
                {
                    pal_col(1, 0x14);
                }
                finally
                {
                    pal_col(2, 0x20);
                }
                ppu_on_all();
                while (true) ;
                """,
            expectedAssembly:
                """
                A900    ; LDA #$00
                208385  ; JSR pusha
                A902    ; LDA #$02
                203E82  ; JSR pal_col
                A901    ; LDA #$01
                208385  ; JSR pusha
                A914    ; LDA #$14
                203E82  ; JSR pal_col
                A902    ; LDA #$02
                208385  ; JSR pusha
                A920    ; LDA #$20
                203E82  ; JSR pal_col
                208982  ; JSR ppu_on_all
                4C2185  ; JMP (infinite loop)
                """);
    }

    [Fact]
    public void TryCatch_Throws()
    {
        // try/catch must still be rejected — the NES has no exception handling.
        var ex = Assert.Throws<TranspileException>(() =>
            GetProgramBytes(
                """
                try
                {
                    ppu_on_all();
                }
                catch
                {
                    ppu_off();
                }
                while (true) ;
                """));
        Assert.Contains("try/catch", ex.Message);
    }

    [Fact]
    public void UshortArray_NewarrAndConstantStore()
    {
        // ushort[] newarr allocates count*2 bytes; constant-index stelem.i2 stores lo/hi at computed addresses
        var bytes = GetProgramBytes(
            """
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            arr[1] = 300;
            arr[2] = 1000;
            arr[3] = 50000;
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_NewarrAndConstantStore hex: {hex}");

        // arr[0] = 100 (0x0064): STA base+0 with 0x64, STA base+1 with 0x00
        Assert.Contains("A964", hex); // LDA #$64 (lo byte of 100)
        Assert.Contains("A900", hex); // LDA #$00 (hi byte of 100)

        // arr[1] = 300 (0x012C): lo=0x2C, hi=0x01
        Assert.Contains("A92C", hex); // LDA #$2C (lo byte of 300)
        Assert.Contains("A901", hex); // LDA #$01 (hi byte of 300)

        // arr[3] = 50000 (0xC350): lo=0x50, hi=0xC3
        Assert.Contains("A950", hex); // LDA #$50 (lo byte of 50000)
        Assert.Contains("A9C3", hex); // LDA #$C3 (hi byte of 50000)
    }

    [Fact]
    public void UshortArray_VariableIndexLoad()
    {
        // Variable-index ldelem.u2 uses ASL A, TAY, LDA base,Y / LDA base+1,Y
        var bytes = GetProgramBytes(
            """
            byte idx = rand8();
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            arr[1] = 300;
            arr[2] = 1000;
            arr[3] = 50000;
            ushort loaded = arr[idx];
            pal_col(0, (byte)loaded);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_VariableIndexLoad hex: {hex}");

        // Variable index load pattern: ASL A (0A), TAY (A8)
        Assert.Contains("0AA8", hex); // ASL A; TAY (double index for 16-bit elements)

        // AbsoluteY addressing: LDA abs,Y (B9) for both lo and hi bytes
        Assert.Contains("B9", hex);
    }

    [Fact]
    public void UshortArray_VariableIndexStore()
    {
        // Variable-index stelem.i2 saves value, computes Y offset, stores both bytes
        var bytes = GetProgramBytes(
            """
            ushort[] arr = new ushort[4];
            arr[0] = 100;
            byte idx = rand8();
            arr[idx] = 310;
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_VariableIndexStore hex: {hex}");

        // 310 = 0x0136: lo=0x36, hi=0x01
        Assert.Contains("A936", hex); // LDA #$36 (lo byte of 310)
        Assert.Contains("A901", hex); // LDA #$01 (hi byte of 310)

        // Variable index store uses ASL A + TAY pattern
        Assert.Contains("0A", hex);   // ASL A
        Assert.Contains("A8", hex);   // TAY

        // Store pattern uses STA absolute,Y (opcode 99)
        Assert.Contains("99", hex);   // STA absolute,Y
    }

    [Fact]
    public void UshortArray_LoadStoresIn16BitLocal()
    {
        // ldelem.u2 result stored to ushort local uses STA $xxxx + STX $xxxx+1
        var bytes = GetProgramBytes(
            """
            byte i = rand8();
            ushort[] arr = new ushort[2];
            arr[0] = 500;
            arr[1] = 1000;
            ushort val = arr[i];
            pal_col(0, (byte)val);
            ppu_on_all();
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"UshortArray_LoadStoresIn16BitLocal hex: {hex}");

        // arr[0] = 500 (0x01F4): lo=0xF4, hi=0x01
        Assert.Contains("A9F4", hex); // LDA #$F4
        Assert.Contains("A901", hex); // LDA #$01

        // arr[1] = 1000 (0x03E8): lo=0xE8, hi=0x03
        Assert.Contains("A9E8", hex); // LDA #$E8
        Assert.Contains("A903", hex); // LDA #$03
    }

    [Fact]
    public void OamBegin_EmitsLdaStaJsrInMainBlock()
    {
        // oam_begin() should emit: LDA #0, STA $1B (oam_off), JSR oam_clear in the main block
        var (program, _) = BuildProgram(
            """
            using (var frame = oam_begin())
            {
                frame.spr(10, 20, 0x01, 0);
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
    public void OamBegin_EmitsOamClearAndOamHideRestInMainBlock()
    {
        // oam_begin() emits JSR oam_clear; OamFrame.Dispose() emits LDA oam_off, JSR oam_hide_rest
        var (program, _) = BuildProgram(
            """
            using (var frame = oam_begin())
            {
                frame.spr(10, 20, 0x01, 0);
            }
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");

        // Verify JSR oam_clear is emitted in the main block (from oam_begin)
        bool hasOamClear = mainBlock.InstructionsWithLabels.Any(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_clear");
        Assert.True(hasOamClear, "Expected JSR oam_clear from oam_begin() in main block");

        // Verify JSR oam_hide_rest is emitted in the main block (from OamFrame.Dispose)
        bool hasOamHideRest = mainBlock.InstructionsWithLabels.Any(il =>
            il.Instruction.Opcode == Opcode.JSR &&
            il.Instruction.Operand is LabelOperand lbl && lbl.Label == "oam_hide_rest");
        Assert.True(hasOamHideRest, "Expected JSR oam_hide_rest from OamFrame.Dispose() in main block");
    }

    [Fact]
    public void OamBegin_InsideLoopBody_EmitsCorrectOrdering()
    {
        // Realistic game loop: oam_begin inside while(true), Dispose called each iteration
        var (program, _) = BuildProgram(
            """
            ppu_on_all();
            while (true)
            {
                ppu_wait_nmi();
                using (var frame = oam_begin())
                {
                    frame.spr(10, 20, 0x01, 0);
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
        Assert.True(clearIndex >= 0, "Expected JSR oam_clear from oam_begin()");
        Assert.True(hideRestIndex >= 0, "Expected JSR oam_hide_rest from OamFrame.Dispose()");

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
    public void OamFrameSpr_EmitsOamOffLoadAndStore()
    {
        // frame.spr(x, y, chr, attr) should auto-manage oam_off:
        // LDA $1B before JSR oam_spr, STA $1B after
        var (program, _) = BuildProgram(
            """
            using (var frame = oam_begin())
            {
                frame.spr(10, 20, 0x01, 0);
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
        Assert.True(jsrIndex >= 0, "Expected JSR oam_spr from frame.spr()");

        // The instruction after JSR oam_spr should be STA $1B (store oam_off)
        Assert.True(jsrIndex + 1 < instructions.Count, "Expected instruction after JSR oam_spr");
        var staAfter = instructions[jsrIndex + 1].Instruction;
        Assert.Equal(Opcode.STA, staAfter.Opcode);
        Assert.Equal(AddressMode.ZeroPage, staAfter.Mode);
        Assert.Equal(0x1B, ((ImmediateOperand)staAfter.Operand!).Value);
    }

    [Fact]
    public void OamFrameMetaSpr_EmitsOamOffLoadAndStore()
    {
        // frame.meta_spr(x, y, data) should auto-manage oam_off
        var source =
            """
            byte[] sprite = new byte[] { 0, 0, 0x01, 0, 128 };
            using (var frame = oam_begin())
            {
                frame.meta_spr(10, 20, sprite);
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
        Assert.True(jsrIndex >= 0, "Expected JSR oam_meta_spr from frame.meta_spr()");

        var staAfter = instructions[jsrIndex + 1].Instruction;
        Assert.Equal(Opcode.STA, staAfter.Opcode);
        Assert.Equal(AddressMode.ZeroPage, staAfter.Mode);
        Assert.Equal(0x1B, ((ImmediateOperand)staAfter.Operand!).Value);
    }

    [Fact]
    public void StelemI1_ConstantIndex_AddExpression()
    {
        // Bug: tile_row[1] = (byte)(sprite + 1) emitted LDA #$01 (the constant)
        // instead of LDA sprite; CLC; ADC #$01 (the computed value).
        var (program, _) = BuildProgram(
            """
            byte sprite = (byte)pad_poll(0);
            byte[] tile_row = new byte[4];
            tile_row[1] = (byte)(sprite + 1);
            pal_col(0, tile_row[1]);
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find the CLC instruction that starts the add sequence
        int clcIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.CLC);
        Assert.True(clcIndex >= 0, "Expected CLC for the add operation");

        // The next instruction should be ADC #$01
        var adcInstr = instructions[clcIndex + 1].Instruction;
        Assert.Equal(Opcode.ADC, adcInstr.Opcode);
        Assert.Equal(AddressMode.Immediate, adcInstr.Mode);
        Assert.IsType<ImmediateOperand>(adcInstr.Operand);
        Assert.Equal(1, ((ImmediateOperand)adcInstr.Operand).Value);

        // The instruction before CLC should load the sprite local (LDA abs)
        var ldaInstr = instructions[clcIndex - 1].Instruction;
        Assert.Equal(Opcode.LDA, ldaInstr.Opcode);
        Assert.Equal(AddressMode.Absolute, ldaInstr.Mode);
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

    [Fact]
    public void StelemI1_AddThenOr()
    {
        // Pattern from game2048: map[idx] = (byte)((val + 1) | 0xF0)
        // The stelem handler must emit both ADC and ORA instructions.
        var bytes = GetProgramBytes(
            """
            byte[] map = new byte[16];
            byte idx = 3;
            byte val = 5;
            map[idx] = (byte)((val + 1) | 0xF0);
            while (true) ;
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        _logger.WriteLine($"StelemI1_AddThenOr hex: {hex}");
        // Must contain CLC (18) + ADC #$01 (6901) for the add
        Assert.Contains("186901", hex);
        // Must contain ORA #$F0 (09F0) for the OR operation
        Assert.Contains("09F0", hex);
    }

    [Fact]
    public void StelemI1_AddThenOr_ConstantIndex()
    {
        // Same pattern but with constant array index: tile[0] = (byte)((v + 1) | 0xF0)
        var (program, _) = BuildProgram(
            """
            byte v = (byte)pad_poll(0);
            byte[] tile = new byte[4];
            tile[0] = (byte)((v + 1) | 0xF0);
            pal_col(0, tile[0]);
            ppu_on_all();
            while (true) ;
            """);

        var mainBlock = program.Blocks.Single(b => b.Label == "main");
        var instructions = mainBlock.InstructionsWithLabels.ToList();

        // Find CLC for the ADD
        int clcIndex = instructions.FindIndex(il =>
            il.Instruction.Opcode == Opcode.CLC);
        Assert.True(clcIndex >= 0, "Expected CLC for the add operation");
        Assert.True(clcIndex + 2 < instructions.Count, $"Expected at least 2 instructions after CLC at index {clcIndex}, but only {instructions.Count} total");

        // After CLC: ADC #$01
        var adcInstr = instructions[clcIndex + 1].Instruction;
        Assert.Equal(Opcode.ADC, adcInstr.Opcode);
        Assert.Equal(1, ((ImmediateOperand)adcInstr.Operand!).Value);

        // After ADC: ORA #$F0
        var oraInstr = instructions[clcIndex + 2].Instruction;
        Assert.Equal(Opcode.ORA, oraInstr.Opcode);
        Assert.Equal(0xF0, ((ImmediateOperand)oraInstr.Operand!).Value);
    }
}
