using Xunit.Abstractions;

namespace dotnes.tests;

public class ClosureNumericExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("short", -32768)]
    [InlineData("short", -300)]
    [InlineData("short", -1)]
    [InlineData("short", 255)]
    [InlineData("short", 300)]
    [InlineData("ushort", 300)]
    [InlineData("ushort", 65535)]
    public void CapturedWordLiteralRetainsBothBytes(string type, int input)
    {
        var cpu = ExecuteProgram($$"""
            {{type}} value = {{input}};
            {{type}} result = Read();
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            {{type}} Read() => value;
            """);
        Assert.Equal(unchecked((byte)input), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)(input >> 8)), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(42, false)]
    [InlineData(128, false)]
    [InlineData(255, false)]
    [InlineData(128, true)]
    [InlineData(255, true)]
    public void CapturedWordStoresTheActualRuntimeByte(int input, bool signed)
    {
        string type = signed ? "sbyte" : "byte";
        var cpu = ExecuteProgram($$"""
            {{type}} source = ({{type}})peek(0x6010);
            poke(0x6020, (byte)source);
            short value = source;
            short result = Read();
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            short Read() => value;
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal(input, cpu.Memory[0x6020]);
        Assert.Equal(input, cpu.Memory[0x6000]);
        Assert.Equal(signed && input >= 128 ? 0xFF : 0, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("short", -300, "byte", 255)]
    [InlineData("sbyte", -1, "byte", 1)]
    [InlineData("short", 300, "short", -300)]
    [InlineData("ushort", 65535, "byte", 1)]
    public void TwoCapturedOperandsRetainTheirDistinctWidths(string leftType, int left, string rightType, int right)
    {
        var cpu = ExecuteProgram($$"""
            {{leftType}} first = {{left}};
            {{rightType}} second = {{right}};
            short sum = Sum();
            byte low = (byte)sum;
            byte high = (byte)(sum >> 8);
            byte less = (byte)(Less() ? 1 : 0);
            poke(0x6000, low);
            poke(0x6001, high);
            poke(0x6002, less);
            test_stop(); while (true);
            static extern void test_stop();
            short Sum() => (short)(first + second);
            bool Less() => first < second;
            """);
        Assert.Equal(unchecked((byte)(left + right)), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)((left + right) >> 8)), cpu.Memory[0x6001]);
        Assert.Equal(left < right ? 1 : 0, cpu.Memory[0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(-128)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(127)]
    public void CapturedSignedByteRetainsTypeThroughNumericConsumers(int input)
    {
        foreach (string expression in new[] { "value", "(ushort)value", "(short)(value + 1)" })
        {
            string returnType = expression == "(ushort)value" ? "ushort" : "short";
            var cpu = ExecuteProgram($$"""
                sbyte value = {{input}};
                {{returnType}} result = Read();
                byte low = (byte)result;
                byte high = (byte)(result >> 8);
                byte negative = (byte)(IsNegative() ? 1 : 0);
                poke(0x6000, low);
                poke(0x6001, high);
                poke(0x6002, negative);
                test_stop(); while (true);
                static extern void test_stop();
                {{returnType}} Read() => {{expression}};
                bool IsNegative() => value < 0;
                """);
            int expected = expression.Contains("+") ? input + 1 : input;
            Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
            Assert.Equal(unchecked((byte)(expected >> 8)), cpu.Memory[0x6001]);
            Assert.Equal(input < 0 ? 1 : 0, cpu.Memory[0x6002]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData(-32768)]
    [InlineData(-300)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32767)]
    public void CapturedSignedWordShiftIsArithmetic(int input)
    {
        var cpu = ExecuteProgram($$"""
            short value = {{input}};
            short result = Read();
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            short Read() => (short)(value >> 8);
            """);
        Assert.Equal(unchecked((byte)(input >> 8)), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)(input >> 16)), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
