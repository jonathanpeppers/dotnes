using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericMultiplyExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("Get()", 0, 2040)]
    [InlineData("Get()", 7, 2047)]
    [InlineData("peek(0x6012)", 7, 2047)]
    public void PromotedProductSurvivesFollowingCall(string source, int following, int expected)
    {
        var cpu = ExecuteProgram($$"""
            ushort result = (ushort)({{source}} * 8 + Next());
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Get() => 255;
            static byte Next() => {{following}};
            """, cpu => cpu.Memory[0x6012] = 255);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
