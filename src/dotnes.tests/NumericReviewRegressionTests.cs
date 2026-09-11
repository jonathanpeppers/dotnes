using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericReviewRegressionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("/")]
    [InlineData("%")]
    public void SignedDivisionAndRemainderAreNotEmittedAsUnsigned(string operation)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            sbyte value = (sbyte)peek(0x6010);
            byte result = (byte)(value {{operation}} 3);
            poke(0x6000, result);
            while (true);
            """));
        Assert.Contains("Signed", error.Message);
        Assert.Contains("not supported", error.Message);
    }

    [Theory]
    [InlineData(255, "State.Value + 1", 1, 128)]
    [InlineData(255, "State.Value + 1 + 256", 1, 0)]
    [InlineData(1, "State.Value - 256", 8, 255)]
    [InlineData(1, "0 - State.Value", 8, 255)]
    public void EveryArithmeticStagePreservesRequiredWordBits(int input, string expression, int count, int expected)
    {
        var cpu = ExecuteProgram($$"""
            State.Value = {{input}};
            byte result = (byte)((ushort)({{expression}}) >> {{count}});
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static class State { public static byte Value; }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(128, 22, 255)]
    [InlineData(255, 22, 255)]
    [InlineData(127, 11, 0)]
    public void SignedFieldWideningPreservesSign(int input, int selected, int high)
    {
        var cpu = ExecuteProgram($$"""
            State.Byte = (sbyte)peek(0x6010);
            State.Word = State.Byte;
            byte result = 11;
            if (State.Word < 0) result = 22;
            byte high = (byte)(State.Word >> 8);
            poke(0x6000, result);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static class State { public static sbyte Byte; public static short Word; }
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal(selected, cpu.Memory[0x6000]);
        Assert.Equal(high, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void WordFieldSumCannotLoseCarryBeforeShift()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            State.Left = 65535;
            State.Right = 1;
            byte result = (byte)((State.Left + State.Right) >> 1);
            poke(0x6000, result);
            while (true);
            static class State { public static ushort Left; public static ushort Right; }
            """));
        Assert.Contains("promoted result wider", error.Message);
    }

    [Fact]
    public void CounterInitializerMustHaveProvenIncomingValue()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            int index = peek(0x6010) == 0 ? 0 : 300;
            for (; index < 10; index++)
                poke(0x6020, (byte)index);
            while (true);
            """));
        Assert.Contains("Int32 local", error.Message);
    }

    [Fact]
    public void GeneratedSpillsCannotAuthorizeSeventeenBitTruncation()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            ushort left = 65535;
            ushort right = 1;
            poke(0x6020, (byte)left);
            poke(0x6021, (byte)right);
            int sum = left + right;
            poke(0x6022, 42);
            byte high = (byte)(sum >> 16);
            poke(0x6000, high);
            while (true);
            """));
        Assert.Contains("promoted result wider", error.Message);
    }

    [Theory]
    [InlineData(200, 220, 210)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 255, 127)]
    public void ByteSumPreservesCarryBeforeDivision(int left, int right, int expected)
    {
        var cpu = ExecuteProgram("""
            byte left = peek(0x6010);
            byte right = peek(0x6011);
            poke(0x6020, left);
            poke(0x6021, right);
            byte result = (byte)((left + right) / 2);
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

    [Fact]
    public void UnknownWordOperandProducesActionableDiagnostic()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            ushort left = 300;
            poke(0x6020, (byte)left);
            ushort result = (ushort)(left + (peek(0x6010) == 0 ? 1 : 2));
            poke(0x6000, (byte)result);
            while (true);
            """));
        Assert.Contains("merged operand", error.Message);
        Assert.Contains("explicitly typed", error.Message);
    }

    [Theory]
    [InlineData(0, 301)]
    [InlineData(1, 302)]
    public void ConditionalWordStoresPreserveSupportedArithmetic(int condition, int expected)
    {
        var cpu = ExecuteProgram("""
            ushort left = 300;
            ushort right;
            if (peek(0x6010) == 0) right = 1;
            else right = 2;
            poke(0x6020, (byte)left);
            poke(0x6021, (byte)right);
            ushort sum = (ushort)(left + right);
            byte low = (byte)sum;
            byte high = (byte)(sum >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = (byte)condition);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 0)]
    [InlineData(255, 0)]
    [InlineData(200, 1)]
    public void ByteParametersEstablishBoundedIntStorage(int input, int replace)
    {
        var cpu = ExecuteProgram($$"""
            Copy({{input}});
            test_stop(); while (true);
            static extern void test_stop();
            static void Copy(byte input)
            {
                int value = input;
                if (peek(0x6010) != 0)
                    value = 1;
                poke(0x6000, (byte)value);
                poke(0x6001, (byte)value);
            }
            """, cpu => cpu.Memory[0x6010] = (byte)replace);
        Assert.Equal(replace != 0 ? 1 : input, cpu.Memory[0x6000]);
        Assert.Equal(cpu.Memory[0x6000], cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
