using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericOperandExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
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
