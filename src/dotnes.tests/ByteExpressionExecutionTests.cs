using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteExpressionExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(127, 128)]
    [InlineData(255, 255)]
    [InlineData(255, 8)]
    public void InlinedExpressionPreservesByteReturnAndPromotedArithmetic(byte left, byte right)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte a = {{left}}, b = {{right}};
            byte value = Combine(a, b);
            poke(0x6000, value);
            ushort full = (ushort)(a + b);
            poke(0x6001, (byte)full);
            poke(0x6002, (byte)(full >> 8));
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """);
        Assert.Equal(unchecked((byte)(left + (right << 4))), cpu.Memory[0x6000]);
        Assert.Equal(left + right, cpu.Memory[0x6001] | cpu.Memory[0x6002] << 8);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void InlinedExpressionAtLoopEntryPreservesLabelsAndBorrow()
    {
        var cpu = ExecuteProgram(
            """
            byte a = 0, b = 8, result = 0;
            for (byte i = 0; i < 3; i++)
            {
                result = Subtract(a, b);
                a++;
            }
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Subtract(byte a, byte b) => (byte)(a - b);
            """);
        Assert.Equal(250, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void LiveOuterArgumentsKeepTheOriginalCallBoundary()
    {
        var cpu = ExecuteProgram(
            """
            byte a = 255, b = 8;
            poke(0x6000, Combine(a, b));
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """);
        Assert.Equal(127, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ConstantArgumentsFoldWithoutLeavingSpeculativeStackValues()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Combine(255, 8);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """);
        Assert.Equal(127, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void SideEffectingArgumentsRetainEvaluationOrder()
    {
        var cpu = ExecuteProgram(
            """
            byte a = 17;
            byte result = Combine(a++, Next());
            poke(0x6000, result);
            poke(0x6001, a);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next() { poke(0x6002, (byte)(peek(0x6002) + 1)); return 8; }
            static byte Combine(byte a, byte b) => (byte)(a + (b << 4));
            """);
        Assert.Equal(new byte[] { 145, 18, 1 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
