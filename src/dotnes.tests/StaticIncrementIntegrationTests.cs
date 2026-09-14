using Xunit.Abstractions;

namespace dotnes.tests;

public class StaticIncrementIntegrationTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StaticIncrementAfterMemoryCallsPreservesArguments(bool captured, bool increment)
    {
        string declaration = captured ? "byte c = 7;" : "";
        string local = captured ? "" : "byte c = 7;";
        string modifier = captured ? "" : "static";
        string update = increment ? "State.Calls++;" : "State.Calls = 1;";
        var cpu = ExecuteProgram($$"""
            {{declaration}}
            byte result = Consume(44, 89);
            poke(0x6000, result);
            byte calls = State.Calls;
            poke(0x6006, calls);
            test_stop(); while (true);
            static extern void test_stop();
            {{modifier}} byte Consume(byte first, byte second)
            {
                byte a = first, b = second;
                {{local}}
                poke(0x6002, a);
                poke(0x6003, b);
                poke(0x6004, c);
                {{update}}
                State.Index = 5;
                return a;
            }
            static class State { public static byte Calls; public static byte Index; }
            """);
        Assert.Equal(44, cpu.Memory[0x6000]);
        Assert.Equal(new byte[] { 44, 89, 7 }, cpu.Memory[0x6002..0x6005]);
        Assert.Equal(1, cpu.Memory[0x6006]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
