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

    public static IEnumerable<object[]> TrackedDivisorCases()
    {
        foreach (int dividend in new[] { 0, 1, 127, 128, 255 })
            foreach (int divisor in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
                foreach (string destination in new[] { "local", "dynamic-poke", "outer-call" })
                    yield return [dividend, divisor, destination];
    }

    [Theory]
    [MemberData(nameof(TrackedDivisorCases))]
    public void TrackedPowerOfTwoDivisorDoesNotReplaceCapturedDividend(int dividend, int divisor, string destination)
    {
        string store = destination switch
        {
            "dynamic-poke" => "poke((ushort)(0x6000 + peek(0x6012)), (byte)(Read() % divisor));",
            "outer-call" => "Store(42, (byte)(Read() % divisor));",
            _ => "byte result = (byte)(Read() % divisor); poke(0x6000, result);",
        };
        var cpu = ExecuteProgram($$"""
            byte divisor = {{divisor}};
            poke(0x6030, divisor);
            {{store}}
            test_stop(); while (true);
            static extern void test_stop();
            static byte Read() => peek(0x6010);
            static void Store(byte marker, byte result)
            {
                poke(0x6000, result);
                poke(0x6001, marker);
            }
            """, cpu => cpu.Memory[0x6010] = (byte)dividend);
        Assert.Equal((byte)(dividend % divisor), cpu.Memory[0x6000]);
        Assert.Equal((byte)divisor, cpu.Memory[0x6030]);
        if (destination == "outer-call")
            Assert.Equal(42, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(16)]
    public void LoopUpdatedDivisorUsesRuntimeValueInsteadOfTrackedIncrement(byte expectedCount)
    {
        var cpu = ExecuteProgram("""
            byte count = 0;
            for (byte i = 0; i < peek(0x6011); i++)
                count++;
            byte result = (byte)(Read() % count);
            poke(0x6000, result);
            poke(0x6001, count);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Read() => peek(0x6010);
            """, cpu =>
            {
                cpu.Memory[0x6010] = 255;
                cpu.Memory[0x6011] = expectedCount;
            });
        Assert.Equal((byte)(255 % expectedCount), cpu.Memory[0x6000]);
        Assert.Equal(expectedCount, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("ushort", "byte", 300, 3)]
    [InlineData("ushort", "byte", 65535, 255)]
    [InlineData("short", "byte", -300, 3)]
    [InlineData("short", "byte", -32768, 127)]
    [InlineData("ushort", "sbyte", 300, -3)]
    [InlineData("short", "sbyte", -300, -3)]
    [InlineData("ushort", "ushort", 300, 255)]
    [InlineData("short", "short", -300, -3)]
    public void RuntimeWordMultiplicationRetainsExplicitByteWrapping(string leftType, string rightType, int left, int right)
    {
        var cpu = ExecuteProgram($$"""
            {{leftType}} left = Read();
            {{rightType}} right = ({{rightType}})peek(0x6011);
            byte result = (byte)(left * right);
            poke(0x6000, result);
            result = (byte)(right * left);
            poke(0x6001, result);
            test_stop(); while (true);
            static extern void test_stop();
            static {{leftType}} Read() => {{left}};
            """, cpu => cpu.Memory[0x6011] = (byte)right);
        // A word cast from peek zero-extends, unlike an explicit signed-byte cast.
        int actualRight = rightType == "sbyte" ? (sbyte)right : (byte)right;
        Assert.Equal((byte)(left * actualRight), cpu.Memory[0x6000]);
        Assert.Equal((byte)(left * actualRight), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
