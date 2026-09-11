using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericOperandExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(200, 220, 210)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 255, 127)]
    public void ByteSumIsPromotedBeforeRightShift(byte left, byte right, byte expected)
    {
        var cpu = ExecuteProgram("""
            byte left = peek(0x6010);
            poke(0x6020, left);
            byte right = peek(0x6011);
            poke(0x6021, right);
            byte middle = (byte)((left + right) >> 1);
            poke(0x6000, middle);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = left;
                cpu.Memory[0x6011] = right;
            });
        Assert.Equal(expected, cpu.Memory[0x6000]);
    }

    [Theory]
    [InlineData("short", -32768, "short", 32767)]
    [InlineData("short", 32767, "short", -32768)]
    [InlineData("ushort", 65535, "short", -1)]
    [InlineData("short", -1, "ushort", 65535)]
    [InlineData("ushort", 256, "ushort", 255)]
    [InlineData("ushort", 65535, "ushort", 65535)]
    public void WordComparisonsPromoteBeforeComparing(string leftType, int left, string rightType, int right)
    {
        string[] operators = ["<", "<=", ">", ">=", "==", "!="];
        bool[] expected = [left < right, left <= right, left > right, left >= right, left == right, left != right];
        for (int i = 0; i < operators.Length; i++)
        {
            var cpu = ExecuteProgram($$"""
                {{leftType}} left = {{left}};
                {{rightType}} right = {{right}};
                byte selected = 42;
                byte iteration = 0;
                while (iteration < 1)
                {
                    left = ({{leftType}})(left + 1);
                    left = ({{leftType}})(left - 1);
                    right = ({{rightType}})(right + 1);
                    right = ({{rightType}})(right - 1);
                    if (left {{operators[i]}} right) selected = 76;
                    iteration++;
                }
                poke(0x6000, selected);
                test_stop(); while (true);
                static extern void test_stop();
                """);
            Assert.Equal(expected[i] ? 76 : 42, cpu.Memory[0x6000]);
        }
    }

    [Theory]
    [InlineData("ushort", 255, "+", 1, 256)]
    [InlineData("ushort", 256, "-", 1, 255)]
    [InlineData("ushort", 65535, "+", 1, 0)]
    [InlineData("ushort", 0, "-", 1, 65535)]
    [InlineData("short", -32768, "-", 1, 32767)]
    [InlineData("short", 32767, "+", 1, -32768)]
    public void WordArithmeticPropagatesCarryAndBorrow(string type, int initial, string operation, int input, int expected)
    {
        var cpu = ExecuteProgram($$"""
            {{type}} value = {{initial}};
            byte iteration = 0;
            while (iteration < 1)
            {
                byte delta = peek(0x6010);
                poke(0x6020, delta);
                value = ({{type}})(value {{operation}} delta);
                byte low = (byte)value;
                byte high = (byte)(value >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                iteration++;
            }
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
    }

    [Theory]
    [InlineData(0x6000, 1, false)]
    [InlineData(0x60FA, 20, false)]
    [InlineData(0x60FA, 20, true)]
    public void WordAddressAdditionPreservesCarryAndStack(int address, int input, bool materialized)
    {
        string calculation = materialized
            ? $"ushort value = {address}; value = (ushort)(value + index);"
            : $"ushort value = (ushort)({address} + index);";
        var cpu = ExecuteProgram($$"""
            Probe({{input}});
            Probe({{input}});
            test_stop(); while (true);
            static extern void test_stop();
            static void Probe(byte index)
            {
                {{calculation}}
                byte low = (byte)value;
                byte high = (byte)(value >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
            }
            """);
        Assert.Equal((byte)(address + input), cpu.Memory[0x6000]);
        Assert.Equal((byte)((address + input) >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(-128, 127)]
    [InlineData(127, -128)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, -1)]
    public void SignedByteComparisonsUsePromotedValues(int left, int right)
    {
        string[] operators = ["<", "<=", ">", ">=", "==", "!="];
        bool[] expected = [left < right, left <= right, left > right, left >= right, left == right, left != right];
        for (int i = 0; i < operators.Length; i++)
        {
            var cpu = ExecuteProgram($$"""
                sbyte left = (sbyte)peek(0x6010);
                poke(0x6020, (byte)left);
                sbyte right = (sbyte)peek(0x6011);
                poke(0x6021, (byte)right);
                byte selected = 42;
                if (left {{operators[i]}} right) selected = 76;
                poke(0x6000, selected);
                test_stop(); while (true);
                static extern void test_stop();
                """, cpu =>
                {
                    cpu.Memory[0x6010] = (byte)left;
                    cpu.Memory[0x6011] = (byte)right;
                });
            Assert.Equal(expected[i] ? 76 : 42, cpu.Memory[0x6000]);
        }
    }
}
