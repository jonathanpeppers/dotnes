using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace dotnes.analyzers.tests;

public class NESAnalyzerTests
{
    static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<NESAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            TestState =
            {
                OutputKind = OutputKind.ConsoleApplication,
            },
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    static async Task VerifyLibraryAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<NESAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            TestState =
            {
                OutputKind = OutputKind.DynamicallyLinkedLibrary,
            },
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    static DiagnosticResult Diagnostic(string id) =>
        CSharpAnalyzerVerifier<NESAnalyzer, DefaultVerifier>.Diagnostic(id);

    // ==================== NES001: Infinite loop required ====================

    [Fact]
    public async Task NES001_NoInfiniteLoop_Diagnostic()
    {
        var test = """
            {|#0:pal_col(0, 0x02);|}

            static void pal_col(byte index, byte color) { }
            """;

        var expected = Diagnostic(NESAnalyzer.NES001).WithLocation(0);
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES001_HasInfiniteLoop_NoDiagnostic()
    {
        var test = """
            pal_col(0, 0x02);
            while (true) ;

            static void pal_col(byte index, byte color) { }
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES001_HasInfiniteLoopWithEmptyBlock_NoDiagnostic()
    {
        var test = """
            pal_col(0, 0x02);
            while (true) { }

            static void pal_col(byte index, byte color) { }
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES001_HasInfiniteLoopWithBody_NoDiagnostic()
    {
        // Real NES programs typically have game loop logic inside while (true)
        var test = """
            while (true) { ppu_wait_nmi(); }

            static void ppu_wait_nmi() { }
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES001_InfiniteLoopInsideLocalFunction_NoDiagnostic()
    {
        // Some samples put the infinite loop inside a local function called as the last statement
        var test = """
            setup();
            game_loop();

            static void setup() { }
            static void game_loop()
            {
                byte x = 0;
                while (true) { x = (byte)(x + 1); }
            }
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES001_NoGlobalStatements_NoDiagnostic()
    {
        // Library code (no top-level statements) should not trigger NES001
        var test = """
            namespace MyLib
            {
                public static class Helper
                {
                    public static byte Add(byte a, byte b) => (byte)(a + b);
                }
            }
            """;

        // Static class should not trigger NES002, and NES001 should not fire either
        await VerifyLibraryAsync(test);
    }

    [Fact]
    public async Task NES001_NoGlobalStatements_NonStaticClass_NoDiagnostic()
    {
        // NES001 should not fire for library code, even with a non-static class
        var test = """
            namespace MyLib
            {
                public class {|#0:Helper|}
                {
                    public byte Add(byte a, byte b) => (byte)(a + b);
                }
            }
            """;

        // NES002 fires for the non-static class, but NES001 should not
        var expected = Diagnostic(NESAnalyzer.NES002).WithLocation(0).WithArguments("Helper");
        await VerifyLibraryAsync(test, expected);
    }

    // ==================== NES002: Classes not supported ====================

    [Fact]
    public async Task NES002_ClassDeclaration_Diagnostic()
    {
        // Class declared inside namespace (library context)
        var test = """
            namespace MyGame
            {
                class {|#0:Player|}
                {
                }
            }
            """;

        var expected = Diagnostic(NESAnalyzer.NES002).WithLocation(0).WithArguments("Player");
        await VerifyLibraryAsync(test, expected);
    }

    [Fact]
    public async Task NES002_StaticClassDeclaration_NoDiagnostic()
    {
        // Static classes are just method containers, not objects
        var test = """
            namespace MyGame
            {
                static class Display
                {
                    public static void Setup() { }
                }
            }
            """;

        await VerifyLibraryAsync(test);
    }

    [Fact]
    public async Task NES002_NoClassDeclaration_NoDiagnostic()
    {
        var test = """
            byte x = 0;
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    // ==================== NES003: String manipulation not supported ====================

    [Fact]
    public async Task NES003_StringConcatenation_Diagnostic()
    {
        var test = """
            string a = "hello";
            string b = {|#0:a + " world"|};
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES003).WithLocation(0);
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES003_InterpolatedString_Diagnostic()
    {
        var test = """
            byte x = 5;
            string s = {|#0:$"value: {x}"|};
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES003).WithLocation(0);
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES003_StringLiteral_NoDiagnostic()
    {
        var test = """
            string s = "HELLO";
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES003_StringFormat_Diagnostic()
    {
        var test = """
            byte x = 5;
            string s = {|#0:string.Format("value: {0}", x)|};
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES003).WithLocation(0);
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES003_StringConcat_Diagnostic()
    {
        var test = """
            string a = "hello";
            string b = {|#0:string.Concat(a, " world")|};
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES003).WithLocation(0);
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES003_FormattableStringInvariant_Diagnostic()
    {
        // When the argument is an interpolated string, AnalyzeInterpolatedString already fires NES003
        // so the invocation handler skips the duplicate diagnostic.
        var test = """
            byte x = 5;
            string s = System.FormattableString.Invariant({|#0:$"value: {x}"|});
            while (true) ;
            """;

        await VerifyAsync(test,
            Diagnostic(NESAnalyzer.NES003).WithLocation(0));
    }

    [Fact]
    public async Task NES003_RegularMethodCall_NoDiagnostic()
    {
        var test = """
            setup();
            while (true) ;

            static void setup() { }
            """;

        await VerifyAsync(test);
    }

    // ==================== NES004: Allocation types ====================

    [Fact]
    public async Task NES004_ByteArrayAllocation_NoDiagnostic()
    {
        var test = """
            byte[] data = new byte[16];
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES004_UshortArrayAllocation_NoDiagnostic()
    {
        var test = """
            ushort[] data = new ushort[8];
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES004_UnsupportedAllocation_Diagnostic()
    {
        var test = """
            System.Collections.Generic.List<byte> {|#1:list|} = {|#0:new System.Collections.Generic.List<byte>()|};
            while (true) ;
            """;

        // NES004 for the allocation, NES005 for the variable type
        await VerifyAsync(test,
            Diagnostic(NESAnalyzer.NES004).WithLocation(0),
            Diagnostic(NESAnalyzer.NES005).WithLocation(1).WithArguments("System.Collections.Generic.List<byte>"));
    }

    [Fact]
    public async Task NES004_StructAllocation_NoDiagnostic()
    {
        var test = """
            MyPoint point = new MyPoint();
            while (true) ;

            struct MyPoint
            {
                public byte X;
                public byte Y;
            }
            """;

        await VerifyAsync(test);
    }

    // ==================== NES005: Unsupported types ====================

    [Fact]
    public async Task NES005_ByteVariable_NoDiagnostic()
    {
        var test = """
            byte x = 0;
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES005_SbyteVariable_NoDiagnostic()
    {
        var test = """
            sbyte x = 0;
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES005_UshortVariable_NoDiagnostic()
    {
        var test = """
            ushort x = 0;
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES005_IntVariable_NoDiagnostic()
    {
        // int is commonly used in expressions, allow it
        var test = """
            int x = 0;
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES005_DoubleVariable_Diagnostic()
    {
        var test = """
            double {|#0:x|} = 0;
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES005).WithLocation(0).WithArguments("double");
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES005_LongVariable_Diagnostic()
    {
        var test = """
            long {|#0:x|} = 0;
            while (true) ;
            """;

        var expected = Diagnostic(NESAnalyzer.NES005).WithLocation(0).WithArguments("long");
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES005_UserStruct_NoDiagnostic()
    {
        var test = """
            MyPoint p = default;
            while (true) ;

            struct MyPoint
            {
                public byte X;
                public byte Y;
            }
            """;

        await VerifyAsync(test);
    }

    [Fact]
    public async Task NES005_UnsupportedReturnType_Diagnostic()
    {
        var test = """
            while (true) ;

            static {|#0:double|} Calculate() => 0.0;
            """;

        var expected = Diagnostic(NESAnalyzer.NES005).WithLocation(0).WithArguments("double");
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES005_UnsupportedParameter_Diagnostic()
    {
        var test = """
            while (true) ;

            static void Process({|#0:float value|}) { }
            """;

        var expected = Diagnostic(NESAnalyzer.NES005).WithLocation(0).WithArguments("float");
        await VerifyAsync(test, expected);
    }

    [Fact]
    public async Task NES005_ByteArrayVariable_NoDiagnostic()
    {
        // byte[] variable declarations should not trigger NES005
        var test = """
            byte[] data = new byte[] { 1, 2, 3 };
            while (true) ;
            """;

        await VerifyAsync(test);
    }

    // ==================== NES006: DllImport ====================

    [Fact]
    public async Task NES006_DllImport_Diagnostic()
    {
        var test = """
            using System.Runtime.InteropServices;

            namespace MyApp
            {
                static class NativeMethods
                {
                    {|#0:[DllImport("kernel32")]
                    static extern void Sleep(byte ms);|}
                }
            }
            """;

        // NES006 for DllImport (static class no longer triggers NES002)
        await VerifyLibraryAsync(test,
            Diagnostic(NESAnalyzer.NES006).WithLocation(0));
    }

    [Fact]
    public async Task NES006_StaticExtern_NoDiagnostic()
    {
        var test = """
            while (true) ;

            static extern void music_play(byte song);
            """;

        await VerifyAsync(test);
    }

    // ==================== Mixed scenarios ====================

    [Fact]
    public async Task ValidHelloWorldProgram_NoDiagnostic()
    {
        var test = """
            byte x = 0x02;
            ushort addr = 0x2000;
            while (true) ;
            """;

        await VerifyAsync(test);
    }
}
