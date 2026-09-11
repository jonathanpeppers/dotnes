using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ClosureCallDoesNotLeaveAPhantomOperand()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Outer(43);
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Outer(byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                Touch();
                Ignore(value, value);
                byte v = value;
                poke(0x6000, v);
            }
            void Touch()
            {
                byte c = captured;
                poke(0x6002, c);
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(new byte[] { 43, 7, 7 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

}
