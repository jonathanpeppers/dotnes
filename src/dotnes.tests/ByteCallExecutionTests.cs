using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void NestedByteCallKeepsTheEnclosingArgument()
    {
        var cpu = ExecuteProgram(
            """
            Outer(11, Inner(22, 33));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Inner(byte first, byte second) => second;
            static void Outer(byte first, byte second)
            {
                byte a = first;
                byte b = second;
                poke(0x6000, a);
                poke(0x6001, b);
            }
            """);
        Assert.Equal(new byte[] { 11, 33 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ByteReturnReplacesPadPollProvenance()
    {
        var cpu = ExecuteProgram(
            """
            byte buttons = (byte)pad_poll(0);
            byte value = Choose(21, 43);
            byte result = (byte)(value & 15);
            poke(0x6000, result);
            poke(0x6001, buttons);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second) => second;
            """);
        Assert.Equal(11, cpu.Memory[0x6000]);
        Assert.Equal(0, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

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
