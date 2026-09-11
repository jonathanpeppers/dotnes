using Xunit.Abstractions;

namespace dotnes.tests;

public class NestedArrayReferenceTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ComputedArrayArgumentsInsideCapturedCompoundCall()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            byte[] data = new byte[8];
            byte[] source = new byte[8];
            source[3] = 44;
            source[4] = 89;
            data[2] = 21;
            data[3] = 66;
            byte i = 2, j = 2;
            data[i] += Consume(source[(byte)(j + 1)], source[(byte)(j + 2)]);
            byte result = data[2];
            byte neighbor = data[3];
            poke(0x6000, result);
            poke(0x6001, neighbor);
            test_stop();
            while (true) ;
            static extern void test_stop();
            byte Consume(byte first, byte second)
            {
                byte a = first, b = second, c = captured;
                poke(0x6002, a);
                poke(0x6003, b);
                poke(0x6004, c);
                return a;
            }
            """);
        Assert.Equal(new byte[] { 65, 66, 44, 89, 7 }, cpu.Memory[0x6000..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedCompoundReferencesSurviveCapturedCall(bool sameArray)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte[] target = new byte[8];
            byte[] source = {{(sameArray ? "target" : "new byte[8]")}};
            target[2] = 21;
            source[3] = 44;
            source[4] = 99;
            byte i = 2, j = 3;
            target[i++] += source[j++] += Consume(7, 8);
            byte outer = target[2];
            byte inner = source[3];
            byte neighbor = source[4];
            poke(0x6000, outer);
            poke(0x6001, inner);
            poke(0x6002, neighbor);
            poke(0x6003, i);
            poke(0x6004, j);
            test_stop();
            while (true) ;
            static extern void test_stop();
            byte Consume(byte first, byte second)
            {
                byte a = first, b = second, c = captured;
                poke(0x6005, a);
                poke(0x6006, b);
                poke(0x6007, c);
                return a;
            }
            """);
        Assert.Equal(new byte[] { 72, 51, 99, 3, 4, 7, 8, 7 }, cpu.Memory[0x6000..0x6008]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void CompoundTargetSurvivesCapturedCallArguments(bool thirdArgument, bool sameArray, bool mutate)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte[] target = new byte[8];
            byte[] source = {{(sameArray ? "target" : "new byte[8]")}};
            target[2] = 21;
            target[3] = 66;
            source[4] = 44;
            source[5] = 89;
            source[6] = 101;
            State.Data = target;
            State.Index = 2;
            byte j = 3;
            target[State.Index++] += Consume(source[(byte)(j + 1)], source[(byte)(j + 2)]{{(thirdArgument ? ", source[(byte)(j + 3)]" : "")}});
            byte result = target[2];
            byte neighbor = target[3];
            byte first = source[4];
            byte second = source[5];
            byte index = State.Index;
            byte calls = State.Calls;
            poke(0x6000, result);
            poke(0x6001, neighbor);
            poke(0x6005, index);
            poke(0x6006, calls);
            poke(0x6007, first);
            poke(0x6008, second);
            test_stop();
            while (true) ;
            static extern void test_stop();
            byte Consume(byte first, byte second{{(thirdArgument ? ", byte third" : "")}})
            {
                byte a = first, b = second, c = captured;
                poke(0x6002, a);
                poke(0x6003, b);
                poke(0x6004, c);
                {{(thirdArgument ? "poke(0x6009, third);" : "")}}
                State.Calls = 1;
                {{(mutate ? "State.Data[2] = 100; State.Index = 5;" : "")}}
                return a;
            }
            static class State
            {
                public static byte[] Data;
                public static byte Index;
                public static byte Calls;
            }
            """);
        Assert.Equal(new byte[] { 65, 66, 44, 89, 7, (byte)(mutate ? 5 : 3), 1, 44, 89 },
            cpu.Memory[0x6000..0x6009]);
        Assert.Equal(thirdArgument ? 101 : 0, cpu.Memory[0x6009]);
        Assert.Equal(1, cpu.WrittenAddresses.Count(a => a == 0x6002));
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompoundTargetSurvivesPostfixArrayRhs(bool helper, bool sameArray)
    {
        string expression = sameArray ? "target[i++] += target[j++]++;" : "target[i++] += source[j++]++;";
        string update = $$"""
            byte i = 2;
            byte j = {{(sameArray ? 2 : 3)}};
            {{expression}}
            poke(0x6004, i);
            poke(0x6005, j);
            """;
        var cpu = ExecuteProgram(
            $$"""
            byte[] target = new byte[8];
            byte[] source = new byte[8];
            target[2] = 21;
            target[3] = 99;
            source[3] = 44;
            source[4] = 77;
            {{(helper ? "Update(target, source);" : update)}}
            byte a = target[2];
            byte b = target[3];
            byte c = source[3];
            byte d = source[4];
            poke(0x6000, a);
            poke(0x6001, b);
            poke(0x6002, c);
            poke(0x6003, d);
            test_stop();
            while (true) ;
            static extern void test_stop();
            {{(helper ? "static void Update(byte[] target, byte[] source) { " + update + " }" : "")}}
            """);
        Assert.Equal(new byte[] { (byte)(sameArray ? 42 : 65), 99, (byte)(sameArray ? 44 : 45), 77, 3, (byte)(sameArray ? 3 : 4) },
            cpu.Memory[0x6000..0x6006]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
