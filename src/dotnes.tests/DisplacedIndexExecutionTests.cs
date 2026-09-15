using Xunit.Abstractions;

namespace dotnes.tests;

public class DisplacedIndexExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("8 + index", 255, 263)]
    [InlineData("index + 8", 255, 263)]
    [InlineData("index - 8", 255, 247)]
    [InlineData("index - 8", 8, 0)]
    public void ConstantDisplacementRetainsAddressCarry(string expression, byte input, int target)
    {
        string source = $$"""
            byte[] data = new byte[280];
            for (byte i = 0; i < 2; i++) data[i] = 0;
            data[{{target}}] = 73;
            byte index = peek(0x6001);
            byte value = data[{{expression}}];
            poke(0x6000, value);
            poke(0x6002, value);
            data[0] = value;
            test_stop(); while (true) ;
            static extern void test_stop();
            """;
        using var transpiler = BuildProgram(source, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        byte[] bytes = program.ToBytes();
        var cpu = new Cpu6502(bytes, program.BaseAddress, program.GetLabels()["main"]);
        cpu.Memory[0x6001] = input;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(73, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ExplicitByteConversionStillWrapsTheIndex()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[280];
            data[7] = 31;
            data[263] = 73;
            byte index = peek(0x6001);
            byte value = data[(byte)(index + 8)];
            poke(0x6000, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6001] = 255);
        Assert.Equal(31, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
