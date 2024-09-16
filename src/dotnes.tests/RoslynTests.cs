﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace dotnes.tests;

public class RoslynTests
{
    readonly MemoryStream _stream = new();
    readonly ILogger _logger;

    public RoslynTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    void AssertProgram(string csharpSource, string expectedAssembly)
    {
        _stream.SetLength(0);

        // Implicit global using
        csharpSource = $"using static NES.NESLib;{Environment.NewLine}{csharpSource}";

        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NESLib).Assembly.Location)
        };

        var compilation = CSharpCompilation
            .Create(
                "hello.dll",
                [syntaxTree],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Debug,
                    deterministic: true));

        var emitResults = compilation.Emit(_stream);
        if (!emitResults.Success)
        {
            Assert.Fail(string.Join(Environment.NewLine, emitResults.Diagnostics.Select(d => d.GetMessage())));
        }

        _stream.Seek(0, SeekOrigin.Begin);

        using var il = new RoslynTranspiler(_stream, _logger);
        il.Write(new MemoryStream());
        AssertEx.Equal(Utilities.ToByteArray(expectedAssembly), il.ToArray());
    }

    /// <summary>
    /// Class for overriding SecondPass()
    /// </summary>
    class RoslynTranspiler : Transpiler
    {
        readonly MemoryStream _stream = new();
        readonly IL2NESWriter _writer;

        public RoslynTranspiler(Stream stream, ILogger logger)
            : base(stream, [new AssemblyReader(new StreamReader(Utilities.GetResource("chr_generic.s")))])
        {
            _writer = new(_stream, leaveOpen: true, logger);
        }

        protected override void SecondPass(ushort sizeOfMain, IL2NESWriter _)
        {
            // Still call base if we ever want to check binary at the end
            base.SecondPass(sizeOfMain, _);

            foreach (var instruction in ReadStaticVoidMain())
            {
                if (instruction.Integer != null)
                {
                    _writer.Write(instruction.OpCode, instruction.Integer.Value, sizeOfMain);
                }
                else if (instruction.String != null)
                {
                    _writer.Write(instruction.OpCode, instruction.String, sizeOfMain);
                }
                else if (instruction.Bytes != null)
                {
                    _writer.Write(instruction.OpCode, instruction.Bytes.Value, sizeOfMain);
                }
                else
                {
                    _writer.Write(instruction.OpCode, sizeOfMain);
                }
            }

            _writer.Flush();
        }

        public byte[] ToArray() => _stream.ToArray();
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
                20AA85  ; JSR pusha
                A902    ; LDA #$02
                203E82  ; JSR pal_col
                A901    ; LDA #$01
                20AA85  ; JSR pusha
                A914    ; LDA #$14
                203E82  ; JSR pal_col
                A902    ; LDA #$02
                20AA85  ; JSR pusha
                A920    ; LDA #$20
                203E82  ; JSR pal_col
                A903    ; LDA #$03
                20AA85  ; JSR pusha
                A930    ; LDA #$30
                203E82  ; JSR pal_col
                A220    ; LDX #$20
                A942    ; LDA #$42
                20D483  ; JSR vram_adr
                A9F1    ; LDA #$F1
                A285    ; LDX #$85
                20C085  ; JSR pushax
                A200    ; LDX #$00
                A90C    ; LDA #$0C
                204F83  ; JSR vram_write
                2089A9  ; JSR ppu_on_all
                018D    ; ???
                2403A9  ; ???
                22A286  ; ???
                4C4885  ; JMP $8548
                """);
    }
}