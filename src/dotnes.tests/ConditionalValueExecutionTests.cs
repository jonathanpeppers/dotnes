using Xunit.Abstractions;

namespace dotnes.tests;

public class ConditionalValueExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false, 0, 24)]
    [InlineData(false, 1, 26)]
    [InlineData(true, 0, 16)]
    [InlineData(true, 1, 18)]
    public void PostfixArmPreservesOriginalAndMutation(bool call, byte flag, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte flag = peek(0x6010);
            byte a = 7, b = 9;
            byte result = {{(call
                ? "Pair(flag == 0 ? a++ : b++, 9)"
                : "(byte)(17 + (flag == 0 ? a++ : b++))")}};
            poke(0x6000, result);
            poke(0x6001, a);
            poke(0x6002, b);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Pair(byte first, byte second) => (byte)(first + second);
            """, initialize: cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(new byte[] { expected, (byte)(flag == 0 ? 8 : 7), (byte)(flag == 0 ? 9 : 10) },
            cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(1, 26)]
    [InlineData(2, 28)]
    public void NestedPostfixArmsPreserveEachSelectedValue(byte flag, byte expected)
    {
        var cpu = ExecuteProgram(
            """
            byte flag = peek(0x6010);
            byte a = 7, b = 9, c = 11;
            byte result = (byte)(17 + (flag == 0 ? a++ : flag == 1 ? b++ : c++));
            poke(0x6000, result);
            poke(0x6001, a);
            poke(0x6002, b);
            poke(0x6003, c);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, initialize: cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(new byte[] { expected, (byte)(flag == 0 ? 8 : 7), (byte)(flag == 1 ? 10 : 9),
            (byte)(flag == 2 ? 12 : 11) }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("(short)-1", "(short)255", 0, 0xFFFF)]
    [InlineData("(short)-1", "(short)255", 1, 0x00FF)]
    [InlineData("(short)-1", "(short)128", 1, 0x0080)]
    [InlineData("(short)-1", "(short)256", 0, 0xFFFF)]
    [InlineData("(short)-1", "(short)256", 1, 0x0100)]
    [InlineData("(short)(sbyte)Get()", "(short)Get()", 0, 0xFFFF)]
    [InlineData("(short)(sbyte)Get()", "(short)Get()", 1, 0x00FF)]
    public void ArmWideningUsesSourceSignedness(string left, string right, byte flag, ushort expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte flag = peek(0x6010);
            short value = flag == 0 ? {{left}} : {{right}};
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Get() => 255;
            """, initialize: cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
