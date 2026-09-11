using Xunit.Abstractions;

namespace dotnes.tests;

public class WordShiftExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(3, 0, 2040)]
    [InlineData(3, 7, 2047)]
    [InlineData(4, 0, 4080)]
    public void WordShiftSurvivesFollowingByteCall(int count, int following, int expected)
    {
        var cpu = ExecuteProgram($$"""
            ushort result = (ushort)((Get() << {{count}}) + Next());
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Get() => 255;
            static byte Next() => {{following}};
            """);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
