using Xunit.Abstractions;

namespace dotnes.tests;

public class ConstantPokeExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ReloadAfterALocalInitialization()
    {
        var cpu = ExecuteProgram("""
            poke(0x6000, 42);
            byte value = 5;
            poke(0x6001, 42);
            poke(0x6002, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 42, 42, 5 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReloadAcrossAConditional(byte flag)
    {
        var cpu = ExecuteProgram("""
            byte flag = peek(0x6020);
            poke(0x6000, 42);
            if (flag != 0)
                poke(0x6002, 42);
            poke(0x6001, 42);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6020] = flag);
        Assert.Equal(42, cpu.Memory[0x6000]);
        Assert.Equal(42, cpu.Memory[0x6001]);
        Assert.Equal(flag != 0 ? 42 : 0, cpu.Memory[0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PreserveAnEarlierReturn(byte flag)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Probe({{flag}});
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Probe(byte flag)
            {
                if (flag == 0)
                    return peek(0x6010);
                poke(0x6001, 44);
                return 77;
            }
            """, cpu => cpu.Memory[0x6010] = 255);
        Assert.Equal(flag == 0 ? 255 : 77, cpu.Memory[0x6000]);
        Assert.Equal(flag == 0 ? 0 : 44, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
