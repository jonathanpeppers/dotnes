using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ForwardedByteArgumentsKeepCallerFrame()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Forward(43);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Forward(byte value)
            {
                Ignore(value, value);
                return value;
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
