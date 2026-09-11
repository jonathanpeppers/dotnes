using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericPowerExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(-128, 4)]
    [InlineData(-8, 4)]
    [InlineData(-7, 4)]
    [InlineData(-1, 4)]
    [InlineData(0, 4)]
    [InlineData(127, 4)]
    [InlineData(-128, 256)]
    [InlineData(-1, 1)]
    public void SignedPowerOfTwoDivisionTruncatesTowardZero(int input, int divisor)
    {
        var cpu = ExecuteProgram($$"""
            short result = Divide({{input}});
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static short Divide(sbyte value) => (short)(value / {{divisor}});
            """);
        int expected = input / divisor;
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
