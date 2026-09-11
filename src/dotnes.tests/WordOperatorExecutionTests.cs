using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class WordOperatorExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("Get255() * 3 + Zero()", 765)]
    [InlineData("Get255() * Get3() + Zero()", 765)]
    [InlineData("(sbyte)Get255() * Get3() + Zero()", 65533)]
    [InlineData("State.Left * State.Right", 65279)]
    [InlineData("(Get255() << 4) | 1", 4081)]
    [InlineData("(Get255() << 4) | Get3()", 4083)]
    [InlineData("(Get255() << 4) ^ 32768", 36848)]
    [InlineData("(Get255() << 8) & 61440", 61440)]
    [InlineData("(Get255() << 8) & State.Right", 256)]
    [InlineData("State.Left ^ State.Right", 65278)]
    public void NativeWordOperatorsPreserveBothBytes(string expression, int expected)
    {
        var cpu = ExecuteProgram($$"""
            State.Left = 65535;
            State.Right = 257;
            ushort value = (ushort)({{expression}});
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Get255() => peek(0x6010);
            static byte Get3() => peek(0x6011);
            static byte Zero() => peek(0x6012);
            static class State { public static ushort Left; public static ushort Right; }
            """, cpu =>
            {
                cpu.Memory[0x6010] = 255;
                cpu.Memory[0x6011] = 3;
            });
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void UntruncatedWordProductCannotLoseItsUpperBits()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            State.Left = 65535;
            State.Right = 65535;
            byte value = (byte)((State.Left * State.Right) >> 16);
            poke(0x6000, value);
            while (true);
            static class State { public static ushort Left; public static ushort Right; }
            """));
        Assert.Contains("promoted result wider", error.Message);
    }

    [Fact]
    public void MixedBitwiseSignednessCannotBeCollapsedBeforeShift()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            State.Signed = 1;
            State.Unsigned = 65535;
            byte value = (byte)((State.Signed ^ State.Unsigned) >> 9);
            poke(0x6000, value);
            while (true);
            static class State { public static sbyte Signed; public static ushort Unsigned; }
            """));
        Assert.Contains("promoted result wider", error.Message);
    }
}
