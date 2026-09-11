using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ExpressionRangeTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("(Get() << 9) >> 8")]
    [InlineData("(Get() << 16) >> 8")]
    [InlineData("(Get() << 24) >> 24")]
    [InlineData("(Word() >> 1) + 32769 >> 16")]
    [InlineData("(peek(0x6010) == 0 ? (Word() >> 1) + 32769 : 0) >> 16")]
    [InlineData("(peek(0x6010) == 0 ? (sbyte)Get() : Word()) >> 8")]
    public void SourceObservationCannotBeMadeSafeBySyntheticNarrowing(string expression)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte value = (byte)({{expression}});
            poke(0x6000, value);
            while (true) ;
            static byte Get() => 255;
            static ushort Word() => 65535;
            """));
        Assert.Contains("promoted result wider", error.Message);
    }

    [Theory]
    [InlineData("(ushort)(Get() << 9) >> 8", 0xFE)]
    [InlineData("(ushort)(Get() << 16) >> 8", 0)]
    [InlineData("(ushort)(peek(0x6010) == 0 ? (int)(sbyte)Get() : Word()) >> 8", 0xFF)]
    public void OriginalNarrowingRetainsIntentionalWordWrapping(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = (byte)({{expression}});
            poke(0x6000, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Get() => 255;
            static ushort Word() => 65535;
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("(a + b) * 2", 840)]
    [InlineData("a & word", 200)]
    public void WordOperatorsCaptureRealCompoundOperands(string expression, ushort expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte a = peek(0x6010), b = peek(0x6011);
            ushort word = 65535;
            ushort result = (ushort)({{expression}});
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = 200;
                cpu.Memory[0x6011] = 220;
            });
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
