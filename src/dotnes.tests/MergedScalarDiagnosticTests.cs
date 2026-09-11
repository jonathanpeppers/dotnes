using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class MergedScalarDiagnosticTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> Expressions()
    {
        foreach (string op in new[] { "+", "-", "*", "/", "%", "&", "|", "^", "<<", ">>" })
            foreach (string plain in new[] { "c", "3", "input" })
            {
                yield return new object[] { $"(flag == 0 ? a : b) {op} {plain}" };
                yield return new object[] { $"{plain} {op} (flag == 0 ? a : b)" };
            }
    }

    [Theory]
    [MemberData(nameof(Expressions))]
    public void UnknownBinaryOperandsAreDiagnosedBeforeLegacyEmission(string expression)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte result = Select(0, 3);
            poke(0x6000, result);
            while (true) ;
            static byte Select(byte flag, byte input)
            {
                byte a = 7;
                byte b = 8;
                byte c = 3;
                return (byte)({{expression}});
            }
            """));
        Assert.Contains("Merged scalar expression operands require typed conditional-value lowering", exception.Message);
    }
}
