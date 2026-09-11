using Xunit.Abstractions;

namespace dotnes.tests;

public class ExecutionHarnessTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ExecutesEmittedHelperAndBalancesStacks()
    {
        var cpu = ExecuteProgram("""
            byte value = Identity(42);
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Identity(byte value) => value;
            """);
        Assert.Equal(42, cpu.Memory[0x6000]);
        Assert.Equal(0x700, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP); // Only the call to the stop marker remains.
        Assert.True(cpu.InstructionCount > 0);
        Assert.Equal(1, cpu.SoftwareStackWrites);
        Assert.True(cpu.SoftwareStackPointerWrites > 0);
    }
}
