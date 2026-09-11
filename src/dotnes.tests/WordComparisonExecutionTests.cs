using Xunit.Abstractions;

namespace dotnes.tests;

public class WordComparisonExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> Comparisons()
    {
        foreach (var (leftType, left, rightType, right) in new (string, int, string, int)[]
        {
            ("ushort", 300, "constant", 256),
            ("ushort", 256, "byte", 255),
            ("byte", 255, "ushort", 256),
            ("ushort", 65535, "constant", 32768),
            ("ushort", 0, "constant", 256),
            ("ushort", 65535, "constant", 65535),
            ("ushort", 0, "constant", 0),
            ("constant", 256, "ushort", 300),
            ("ushort", 300, "ushort", 299),
            ("byte", 255, "constant", 256),
            ("constant", 256, "byte", 255),
        })
        foreach (string operation in new[] { "<", "<=", ">", ">=", "==", "!=" })
        foreach (bool value in new[] { false, true })
            yield return [leftType, left, rightType, right, operation, value];
    }

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void WordAndMixedComparisonsRetainBothBytes(string leftType, int left,
        string rightType, int right, string operation, bool value)
    {
        string declareLeft = leftType == "constant" ? $"const int left = {left};"
            : $"{leftType} left = GetLeft(); poke(0x6030, (byte)left);";
        string declareRight = rightType == "constant" ? $"const int right = {right};"
            : $"{rightType} right = GetRight(); poke(0x6031, (byte)right);";
        string use = value
            ? $"bool selected = left {operation} right; Ignore(); byte result = (byte)(selected ? 1 : 0);"
            : $"byte result = 0; if (left {operation} right) result = 1; Ignore();";
        var cpu = ExecuteProgram($$"""
            {{declareLeft}}
            {{declareRight}}
            {{use}}
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static void Ignore() => poke(0x6032, 1);
            {{(leftType == "constant" ? "" : $"static {leftType} GetLeft() => {left};")}}
            {{(rightType == "constant" ? "" : $"static {rightType} GetRight() => {right};")}}
            """);
        bool expected = Compare(left, right, operation);
        Assert.Equal(expected ? 1 : 0, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    public void WordCallComparisonDoesNotReplaceTheReturnedValueWithItsConstant(int input)
    {
        foreach (string operation in new[] { "<", "<=", ">", ">=", "==", "!=" })
        {
            var cpu = ExecuteProgram($$"""
                bool selected = Get() {{operation}} 256;
                Ignore();
                byte result = (byte)(selected ? 1 : 0);
                poke(0x6000, result);
                test_stop(); while (true);
                static extern void test_stop();
                static void Ignore() => poke(0x6032, 1);
                static ushort Get() => {{input}};
                """);
            Assert.Equal(Compare(input, 256, operation) ? 1 : 0, cpu.Memory[0x6000]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    static bool Compare(int left, int right, string operation) => operation switch
    {
        "<" => left < right,
        "<=" => left <= right,
        ">" => left > right,
        ">=" => left >= right,
        "==" => left == right,
        "!=" => left != right,
        _ => throw new ArgumentException("Unexpected comparison", nameof(operation)),
    };
}
