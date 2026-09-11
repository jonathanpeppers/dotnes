using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CapturedHelperPushesEveryEarlierComputedArgument(bool forwarded, bool threeArguments)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            {{(forwarded ? "Outer(43, 88);" : $"Consume(Next(43), Next(88){(threeArguments ? ", Next(110)" : "")});")}}
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => (byte)(value + 1);
            {{(forwarded ? $$"""
            void Outer(byte first, byte second)
            {
                Consume(Next(first), Next(second){{(threeArguments ? ", Next(110)" : "")}});
                byte c = captured;
                poke(0x6004, c);
            }
            """ : "")}}
            void Consume(byte a, byte b{{(threeArguments ? ", byte d" : "")}})
            {
                byte av = a, bv = b, c = captured;
                poke(0x6000, av);
                poke(0x6001, bv);
                poke(0x6002, c);
                {{(threeArguments ? "byte dv = d; poke(0x6003, dv);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 89, 7, (byte)(threeArguments ? 111 : 0), (byte)(forwarded ? 7 : 0) },
            cpu.Memory[0x6000..0x6005]);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedCallerForwardsOnlyItsStableContext(bool earlierArgument)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            Outer(43);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next(byte value) => value;
            void Outer(byte value)
            {
                Consume({{(earlierArgument ? "88, " : "")}}(byte)(Next(value) + 1));
                byte c = captured;
                poke(0x6002, c);
            }
            void Consume({{(earlierArgument ? "byte first, " : "")}}byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                byte v = value;
                poke(0x6000, v);
                {{(earlierArgument ? "byte a = first; poke(0x6003, a);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 7, 7, (byte)(earlierArgument ? 88 : 0) }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputedValueCanBePassedToCapturedScalarHelper(bool earlierArgument)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte frame = 0;
            while (frame < 2)
            {
                Consume({{(earlierArgument ? "88, " : "")}}(byte)(Next() + 1));
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 43;
            void Consume({{(earlierArgument ? "byte first, " : "")}}byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                byte v = value;
                poke(0x6000, v);
                {{(earlierArgument ? "byte a = first; poke(0x6002, a);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 7, (byte)(earlierArgument ? 88 : 0) }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ClosureCallDoesNotLeaveAPhantomOperand()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Outer(43);
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Outer(byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                Touch();
                Ignore(value, value);
                byte v = value;
                poke(0x6000, v);
            }
            void Touch()
            {
                byte c = captured;
                poke(0x6002, c);
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(new byte[] { 43, 7, 7 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

}
