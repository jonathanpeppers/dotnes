using Xunit.Abstractions;

namespace dotnes.tests;

public class ConditionalArithmeticIntegrationTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> ConditionalCases()
    {
        const string conditional = "(flag == 0 ? a : b)";
        foreach (string operation in new[] { "*", "/", "%" })
            foreach (string plain in new[] { "c", "3", "input" })
                foreach (bool joinedLeft in new[] { false, true })
                    foreach (byte flag in new byte[] { 0, 1 })
                    {
                        string expression = joinedLeft ? $"{conditional} {operation} {plain}" : $"{plain} {operation} {conditional}";
                        int selected = flag == 0 ? 7 : 8;
                        int left = joinedLeft ? selected : 3, right = joinedLeft ? 3 : selected;
                        int expected = operation switch { "*" => left * right, "/" => left / right, _ => left % right };
                        yield return [expression, flag, (byte)expected];
                    }
        yield return [$"{conditional} << input", (byte)0, (byte)56];
        yield return [$"{conditional} << input", (byte)1, (byte)64];
    }

    [Theory]
    [MemberData(nameof(ConditionalCases))]
    public void ConditionalOperandPreservesValueAndStack(string expression, byte flag, byte expected)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Select({{flag}}, 3);
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Select(byte flag, byte input)
            {
                byte a = 7, b = 8, c = 3;
                return (byte)({{expression}});
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
