using Xunit.Abstractions;

namespace dotnes.tests;

public class XorLiteralExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("index ^ 0")]
    [InlineData("0 ^ index")]
    public void ZeroXorIndexPreservesTheActualLiteral(string expression)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[0] = 17;
            data[3] = 89;
            byte index = 3;
            byte result = data[{{expression}}];
            poke(0x6000, result);
            poke(0x6001, index);
            byte untouched = data[0];
            poke(0x6002, untouched);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 89, 3, 17 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
