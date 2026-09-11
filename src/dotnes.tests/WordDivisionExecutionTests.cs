using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class WordDivisionExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(600, 300)]
    [InlineData(65535, 200)]
    [InlineData(65535, 512)]
    [InlineData(256, 3)]
    [InlineData(65535, 128)]
    [InlineData(65535, 129)]
    [InlineData(65535, 32769)]
    [InlineData(65535, 65535)]
    [InlineData(32768, 65535)]
    [InlineData(0, 300)]
    [InlineData(1, 300)]
    [InlineData(255, 300)]
    public void WordDivisionAndRemainderPreserveAllBits(int dividend, int divisor)
    {
        foreach (string operation in new[] { "/", "%" })
        {
            var cpu = ExecuteProgram($$"""
                ushort value = {{dividend}};
                poke(0x6020, (byte)value);
                int result = value {{operation}} {{divisor}};
                if (peek(0x6010) != 0) result = 1;
                byte low = (byte)result;
                byte high = (byte)(result >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                test_stop(); while (true);
                static extern void test_stop();
                """);
            int expected = operation == "/" ? dividend / divisor : dividend % divisor;
            Assert.Equal((byte)expected, cpu.Memory[0x6000]);
            Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("%")]
    public void RuntimeWordDivisorsAreExplicitlyUnsupported(string operation)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            ushort value = 65535;
            ushort divisor = (ushort)(peek(0x6010) + 1);
            poke(0x6020, (byte)value);
            ushort result = (ushort)(value {{operation}} divisor);
            poke(0x6000, (byte)result);
            while (true);
            """));
        Assert.Contains("constant divisor", error.Message);
    }

    [Theory]
    [InlineData(256, 2, 5)]
    [InlineData(65535, 1, 14)]
    [InlineData(65535, 0, 200)]
    public void WordReturnRemainderUsesBothBytesAndActualParameterFrame(int input, int min, int max)
    {
        var cpu = ExecuteProgram($$"""
            byte result = Ranged({{min}}, {{max}});
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static ushort Get() => {{input}};
            static byte Ranged(byte min, byte max)
            {
                byte range = (byte)(max - min);
                ushort value = Get();
                return (byte)((value % range) + min);
            }
            """);
        Assert.Equal(input % (max - min) + min, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(256, 3)]
    [InlineData(65535, 200)]
    [InlineData(65535, 255)]
    [InlineData(65535, 1)]
    [InlineData(0, 255)]
    public void WordDividendSupportsNonzeroRuntimeByteDivisor(int dividend, int divisor)
    {
        foreach (string operation in new[] { "/", "%" })
        {
            var cpu = ExecuteProgram($$"""
                ushort value = {{dividend}};
                byte divisor = peek(0x6010);
                poke(0x6020, (byte)value);
                ushort result = (ushort)(value {{operation}} divisor);
                byte low = (byte)result;
                byte high = (byte)(result >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                test_stop(); while (true);
                static extern void test_stop();
                """, cpu => cpu.Memory[0x6010] = (byte)divisor);
            int expected = operation == "/" ? dividend / divisor : dividend % divisor;
            Assert.Equal((byte)expected, cpu.Memory[0x6000]);
            Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("%")]
    public void RuntimeZeroDivisorFaultsBeforeProducingAResult(string operation)
    {
        using var transpiler = BuildProgram($$"""
            ushort value = 256;
            byte divisor = peek(0x6010);
            poke(0x6020, (byte)value);
            ushort result = (ushort)(value {{operation}} divisor);
            poke(0x6000, (byte)result);
            while (true);
            """, out var program);
        var bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.Memory[0x6000] = 42;
        for (int i = 0; i < 200; i++)
            cpu.Step();
        ushort fault = cpu.PC;
        Assert.Equal(0x4C, cpu.Memory[fault]);
        cpu.Step();
        Assert.Equal(fault, cpu.PC);
        Assert.Equal(42, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
