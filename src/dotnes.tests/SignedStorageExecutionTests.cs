using Xunit.Abstractions;

namespace dotnes.tests;

public class SignedStorageExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0x80, 0xFF, 22)]
    [InlineData(0xFF, 0xFF, 22)]
    [InlineData(0x00, 0x00, 11)]
    [InlineData(0x7F, 0x00, 11)]
    public void SignedByteWidensToShort(int input, int high, int selected)
    {
        var cpu = ExecuteProgram("""
            short value = (sbyte)peek(0x6010);
            poke(0x6030, (byte)value);
            byte result = 11;
            if (value < 0) result = 22;
            poke(0x6000, result);
            byte high = (byte)(value >> 8);
            poke(0x6031, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal((byte)input, cpu.Memory[0x6030]);
        Assert.Equal(high, cpu.Memory[0x6031]);
        Assert.Equal(selected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(-32768)]
    [InlineData(-128)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32767)]
    public void SignedWordConstantsKeepBothBytes(int input)
    {
        var cpu = ExecuteProgram($$"""
            short value = {{input}};
            byte iteration = 0;
            while (iteration < 1)
            {
                poke(0x6030, (byte)value);
                byte result = 11;
                if (value < 0) result = 22;
                poke(0x6000, result);
                byte high = (byte)(value >> 8);
                poke(0x6031, high);
                iteration++;
            }
            test_stop(); while (true);
            static extern void test_stop();
            """);
        Assert.Equal((byte)input, cpu.Memory[0x6030]);
        Assert.Equal((byte)(input >> 8), cpu.Memory[0x6031]);
        Assert.Equal(input < 0 ? 22 : 11, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
