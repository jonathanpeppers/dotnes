using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class UnaryNumericExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(-32768)]
    [InlineData(-300)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(32767)]
    public void WordUnaryOperationsPreserveBothBytes(int input)
    {
        foreach (string operation in new[] { "-", "~" })
        {
            var cpu = ExecuteProgram($$"""
                short value = Get();
                poke(0x6020, (byte)value);
                short result = (short)({{operation}}value);
                byte low = (byte)result;
                byte high = (byte)(result >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                test_stop(); while (true);
                static extern void test_stop();
                static short Get() => {{input}};
                """);
            int expected = operation == "-" ? -input : ~input;
            Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
            Assert.Equal(unchecked((byte)(expected >> 8)), cpu.Memory[0x6001]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void SignedByteUnaryResultRetainsPromotionAcrossCall(int input)
    {
        foreach (string operation in new[] { "-", "~" })
        {
            var cpu = ExecuteProgram($$"""
                sbyte value = (sbyte)peek(0x6010);
                short result = (short)({{operation}}value + Next());
                byte low = (byte)result;
                byte high = (byte)(result >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                test_stop(); while (true);
                static extern void test_stop();
                static byte Next() => 0;
                """, cpu => cpu.Memory[0x6010] = (byte)input);
            int signed = unchecked((sbyte)input);
            int expected = operation == "-" ? -signed : ~signed;
            Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
            Assert.Equal(unchecked((byte)(expected >> 8)), cpu.Memory[0x6001]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void UnaryResultsKeepTheirSignInComparisons(int input)
    {
        foreach (string type in new[] { "byte", "sbyte" })
        foreach (string operation in new[] { "-", "~" })
        {
            var cpu = ExecuteProgram($$"""
                {{type}} value = ({{type}})peek(0x6010);
                byte result = 0;
                if ({{operation}}value < 0) result = 1;
                poke(0x6000, result);
                test_stop(); while (true);
                static extern void test_stop();
                """, cpu => cpu.Memory[0x6010] = (byte)input);
            int value = type == "sbyte" ? unchecked((sbyte)input) : input;
            int expected = operation == "-" ? -value : ~value;
            Assert.Equal(expected < 0 ? 1 : 0, cpu.Memory[0x6000]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData("-")]
    [InlineData("~")]
    public void UnaryWordDemandReachesItsPromotedOperand(string operation)
    {
        var cpu = ExecuteProgram($$"""
            ushort result = (ushort)({{operation}}(peek(0x6010) << 8));
            byte low = (byte)result;
            byte high = (byte)(result >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = 1);
        int expected = operation == "-" ? -256 : ~256;
        Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)(expected >> 8)), cpu.Memory[0x6001]);
    }

    [Theory]
    [InlineData("ushort", "-", 65535)]
    [InlineData("ushort", "~", 65535)]
    [InlineData("short", "-", -32768)]
    public void UnnarrowedWideUnaryRangeIsDiagnosed(string type, string operation, int value)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            {{type}} value = Get();
            byte high = (byte)(({{operation}}value) >> 8);
            poke(0x6000, high);
            while (true);
            static {{type}} Get() => {{value}};
            """));
        Assert.Contains("promoted result wider", error.Message);
    }
}
