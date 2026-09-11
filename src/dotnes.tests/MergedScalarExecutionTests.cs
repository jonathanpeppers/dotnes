using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class MergedScalarExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> Expressions()
    {
        foreach (string op in new[] { "+", "-", "&", "|", "^", "<<", ">>" })
            foreach (string plain in new[] { "c", "3", "input" })
            {
                foreach (bool mergedRight in op is "<<" or ">>" ? new[] { false } : new[] { false, true })
                    foreach (byte flag in new byte[] { 0, 1 })
                    {
                        int joined = flag == 0 ? 7 : 8;
                        int left = mergedRight ? 3 : joined;
                        int right = mergedRight ? joined : 3;
                        int expected = op switch
                        {
                            "+" => left + right,
                            "-" => left - right,
                            "&" => left & right,
                            "|" => left | right,
                            "^" => left ^ right,
                            "<<" => left << right,
                            _ => left >> right,
                        };
                        string expression = mergedRight
                            ? $"{plain} {op} (flag == 0 ? a : b)"
                            : $"(flag == 0 ? a : b) {op} {plain}";
                        yield return new object[] { expression, flag, unchecked((byte)expected) };
                    }
            }
    }

    [Theory]
    [MemberData(nameof(Expressions))]
    public void JoinedBinaryOperandsExecuteAfterConditionalLowering(string expression, byte flag, byte expected)
    {
        var cpu = ExecuteProgram(Source(expression, flag));
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("c << (flag == 0 ? a : b)")]
    [InlineData("3 << (flag == 0 ? a : b)")]
    [InlineData("input << (flag == 0 ? a : b)")]
    [InlineData("c >> (flag == 0 ? a : b)")]
    [InlineData("3 >> (flag == 0 ? a : b)")]
    [InlineData("input >> (flag == 0 ? a : b)")]
    public void ConditionalShiftCountsRetainTheUnsupportedShapeDiagnostic(string expression)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(Source(expression, 0)));
        Assert.Contains("Variable shifts require", exception.Message);
        Assert.Contains("More complex shift expressions are not supported", exception.Message);
    }

    static string Source(string expression, byte flag) =>
        $$"""
            byte result = Select({{flag}}, 3);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Select(byte flag, byte input)
            {
                byte a = 7;
                byte b = 8;
                byte c = 3;
                return (byte)({{expression}});
            }
            """;

    [Theory]
    [InlineData(ILOpCode.Add)]
    [InlineData(ILOpCode.Sub)]
    [InlineData(ILOpCode.Mul)]
    [InlineData(ILOpCode.Div)]
    [InlineData(ILOpCode.Div_un)]
    [InlineData(ILOpCode.Rem)]
    [InlineData(ILOpCode.Rem_un)]
    [InlineData(ILOpCode.And)]
    [InlineData(ILOpCode.Or)]
    [InlineData(ILOpCode.Xor)]
    [InlineData(ILOpCode.Shl)]
    [InlineData(ILOpCode.Shr)]
    [InlineData(ILOpCode.Shr_un)]
    public void UnloweredBinaryJoinsAreStillDiagnosedBeforeEmission(ILOpCode op)
    {
        using var stream = new MemoryStream();
        using var writer = new IL2NESWriter(stream)
        {
            Instructions =
            [
                new(ILOpCode.Ldc_i4_0, Offset: 0),
                new(ILOpCode.Brtrue_s, Integer: 3, Offset: 1),
                new(ILOpCode.Ldc_i4_7, Offset: 3),
                new(ILOpCode.Br_s, Integer: 1, Offset: 4),
                new(ILOpCode.Ldc_i4_8, Offset: 6),
                new(ILOpCode.Ldc_i4_3, Offset: 7),
                new(op, Offset: 8),
            ],
            Index = 6,
        };
        var exception = Assert.Throws<TranspileException>(() => writer.Write(writer.Instructions[6]));
        Assert.Contains("Merged scalar expression operands require typed conditional-value lowering", exception.Message);
        Assert.Equal(0, stream.Length);
    }
}
