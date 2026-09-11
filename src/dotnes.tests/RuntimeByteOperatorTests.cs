using Xunit.Abstractions;

namespace dotnes.tests;

public class RuntimeByteOperatorTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> UnsignedCases()
    {
        foreach (int left in new[] { 0, 1, 127, 128, 255 })
            foreach (int right in new[] { 0, 1, 127, 128, 255 })
                foreach (string operation in new[] { "*", "/", "%" })
                    if (right != 0 || operation == "*")
                        yield return [left, right, operation];
    }

    [Theory]
    [MemberData(nameof(UnsignedCases))]
    public void RuntimeByteParametersUseActualOperands(int left, int right, string operation)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Calculate(peek(0x6010), peek(0x6011));
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Calculate(byte left, byte right) => (byte)(left {{operation}} right);
            """, cpu =>
            {
                cpu.Memory[0x6010] = (byte)left;
                cpu.Memory[0x6011] = (byte)right;
            });
        int expected = operation switch { "*" => left * right, "/" => left / right, _ => left % right };
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [MemberData(nameof(UnsignedCases))]
    public void ByteCallAndLocalDivisorRetainCapturedOperands(int left, int right, string operation)
    {
        var cpu = ExecuteProgram($$"""
            byte right = peek(0x6011);
            byte result = (byte)(Read() {{operation}} right);
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Read() => peek(0x6010);
            """, cpu =>
            {
                cpu.Memory[0x6010] = (byte)left;
                cpu.Memory[0x6011] = (byte)right;
            });
        int expected = operation switch { "*" => left * right, "/" => left / right, _ => left % right };
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("*", 253)]
    [InlineData("/", 85)]
    [InlineData("%", 0)]
    public void ByteCallResultIsCapturedExactlyOnce(string operation, byte expected)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Calculate(3);
            poke(0x6000, result);
            poke(0x6001, State.Calls);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Calculate(byte right) => (byte)(Read() {{operation}} right);
            static byte Read()
            {
                State.Calls++;
                return peek(0x6010);
            }
            static class State { public static byte Calls; }
            """, cpu => cpu.Memory[0x6010] = 255);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(1, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("sbyte", "byte", -128, 255)]
    [InlineData("sbyte", "byte", -1, 255)]
    [InlineData("byte", "sbyte", 255, -128)]
    [InlineData("sbyte", "sbyte", -128, -128)]
    public void SignedByteMultiplicationRetainsExplicitByteWrapping(string leftType, string rightType, int left, int right)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Calculate({{left}}, {{right}});
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Calculate({{leftType}} left, {{rightType}} right) => (byte)(left * right);
            """);
        Assert.Equal((byte)(left * right), cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
