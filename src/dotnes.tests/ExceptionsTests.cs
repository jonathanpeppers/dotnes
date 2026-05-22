using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ExceptionsTests : RoslynTests
{
    public ExceptionsTests(ITestOutputHelper output) : base(output) { }

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
}
