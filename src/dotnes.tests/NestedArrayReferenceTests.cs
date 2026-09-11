using Xunit.Abstractions;

namespace dotnes.tests;

public class NestedArrayReferenceTests(ITestOutputHelper output) : ExecutionTests(output)
{
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
