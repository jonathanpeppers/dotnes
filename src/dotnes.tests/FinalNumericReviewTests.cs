using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class FinalNumericReviewTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(2, 840)]
    [InlineData(3, 1260)]
    [InlineData(257, 42404)]
    public void CompoundWordMultiplyPreservesCarryAndStack(int factor, int expected)
    {
        var cpu = ExecuteProgram($$"""
            byte a = peek(0x6010);
            byte b = peek(0x6011);
            poke(0x6020, a);
            poke(0x6021, b);
            ushort result = (ushort)((a + b) * {{factor}});
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = 200;
                cpu.Memory[0x6011] = 220;
            });
        AssertWord(cpu, expected);
    }

    [Theory]
    [InlineData("a & b", 255, 65535, 255)]
    [InlineData("b & a", 255, 65535, 255)]
    [InlineData("a & b", 128, 257, 0)]
    public void MixedByteWordAndCapturesBothOperands(string expression, int left, int right, int expected)
    {
        var cpu = ExecuteProgram($$"""
            byte a = {{left}};
            ushort b = {{right}};
            poke(0x6020, a);
            poke(0x6021, (byte)b);
            ushort result = (ushort)({{expression}});
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            """);
        AssertWord(cpu, expected);
    }

    [Theory]
    [InlineData(0, -300)]
    [InlineData(1, -300)]
    [InlineData(0, -256)]
    [InlineData(1, -256)]
    [InlineData(0, -32768)]
    [InlineData(1, -32768)]
    public void ConditionalNegativeLiteralKeepsItsHighByte(int selector, int literal)
    {
        var cpu = ExecuteProgram($$"""
            short value = (short)(peek(0x6010) == 0 ? peek(0x6011) : (short){{literal}});
            Ignore();
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static void Ignore() => poke(0x6030, 42);
            """, cpu =>
            {
                cpu.Memory[0x6010] = (byte)selector;
                cpu.Memory[0x6011] = 255;
            });
        AssertWord(cpu, selector == 0 ? 255 : literal);
    }

    [Theory]
    [InlineData(255, 254, 22)]
    [InlineData(128, 128, 22)]
    [InlineData(127, 1, 11)]
    public void SignednessSurvivesMultipleAdditionStages(int left, int right, int expected)
    {
        var cpu = ExecuteProgram("""
            sbyte a = (sbyte)peek(0x6010);
            sbyte b = (sbyte)peek(0x6011);
            poke(0x6020, (byte)a);
            poke(0x6021, (byte)b);
            byte result = 11;
            if (a + b + 1 < 0) result = 22;
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = (byte)left;
                cpu.Memory[0x6011] = (byte)right;
            });
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-32768, -8192)]
    [InlineData(32767, 8191)]
    public void RepeatedSignedShiftsPreserveTheirType(int input, int expected)
    {
        var cpu = ExecuteProgram($$"""
            State.Value = {{input}};
            short value = State.Value;
            poke(0x6020, (byte)value);
            short result = (short)((value >> 1) >> 1);
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static class State { public static short Value; }
            """);
        AssertWord(cpu, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightShiftRangesCannotAuthorizeSeventeenBitTruncation(bool conditional)
    {
        string expression = conditional ? "(peek(0x6010) == 0 ? ((value >> 1) + 32769) : 0)"
            : "((value >> 1) + 32769)";
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            ushort value = 65535;
            poke(0x6020, (byte)value);
            byte result = (byte)({{expression}} >> 16);
            poke(0x6000, result);
            while (true);
            """));
        Assert.Contains("promoted result wider", error.Message);
    }

    [Theory]
    [InlineData(0, 255, -1)]
    [InlineData(0, 128, -128)]
    [InlineData(0, 127, 127)]
    [InlineData(1, 255, 255)]
    public void EachWordReturnArmEstablishesItsHighByte(int selector, int input, int expected)
    {
        var cpu = ExecuteProgram("""
            short value = F(peek(0x6010), (sbyte)peek(0x6011));
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static short F(byte c, sbyte s) => c == 0 ? s : (short)255;
            """, cpu =>
            {
                cpu.Memory[0x6010] = (byte)selector;
                cpu.Memory[0x6011] = (byte)input;
            });
        AssertWord(cpu, expected);
    }

    [Theory]
    [InlineData("sbyte", 1)]
    [InlineData("short", 16)]
    public void SignedLogicalShiftDoesNotIgnoreClrPromotion(string type, int count)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            {{type}} value = (sbyte)peek(0x6010);
            poke(0x6020, (byte)value);
            byte result = (byte)(value >>> {{count}});
            poke(0x6000, result);
            while (true);
            """));
        Assert.Contains("Signed logical right shift", error.Message);
        Assert.Contains("32-bit", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    [InlineData(255)]
    public void PlainPokeArgumentsUseTheActualFrame(int input)
    {
        var cpu = ExecuteProgram($$"""
            Store({{input}});
            StorePair({{input}}, 42);
            test_stop(); while (true);
            static extern void test_stop();
            static void Store(byte value) => poke(0x6000, value);
            static void StorePair(byte left, byte right)
            {
                poke(0x6001, left);
                poke(0x6002, right);
            }
            """);
        Assert.Equal(input, cpu.Memory[0x6000]);
        Assert.Equal(input, cpu.Memory[0x6001]);
        Assert.Equal(42, cpu.Memory[0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    static void AssertWord(Cpu6502 cpu, int expected)
    {
        Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)(expected >> 8)), cpu.Memory[0x6001]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
