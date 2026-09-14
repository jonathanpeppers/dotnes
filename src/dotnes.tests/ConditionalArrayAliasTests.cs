using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ConditionalArrayAliasTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("new byte[] { 1, 2, 3, 4 }", "new byte[] { 4, 3, 2, 1 }")]
    [InlineData("new byte[8]", "new byte[] { 1, 2, 3, 4 }")]
    [InlineData("new byte[8]", "null")]
    [InlineData("State.First", "State.Second")]
    public void DifferentOrUnprovenLocalReferenceOriginsAreDiagnosed(string first, string second)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] first = {{first}};
            byte[] second = {{second}};
            byte flag = 1;
            byte[] alias = flag != 0 ? first : second;
            byte frame = 0;
            while (frame < 2)
            {
                byte value = alias[3];
                poke(0x6000, value);
                frame++;
            }
            while (true) ;
            static class State { public static byte[] First; public static byte[] Second; }
            """));
        Assert.Contains("array alias", exception.Message);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void NestedSameIdentityJoinsPreserveReadValuesAndConditionEffects(bool rom, bool helper, byte flag)
    {
        string update = """
            byte[] other = first;
            byte[] third = other;
            byte[] alias, retained;
            retained = alias = Choose(flag) != 0 ? (flag == 1 ? first : other) : third;
            byte frame = 0;
            while (frame < 2)
            {
                byte a = alias[3];
                byte b = retained[4];
                poke(0x6000, a);
                poke(0x6001, b);
                frame++;
            }
            """;
        var cpu = ExecuteProgram(
            $$"""
            byte[] first = {{(rom ? "new byte[] { 0, 0, 0, 21, 99 }" : "new byte[8]")}};
            {{(rom ? "" : "first[3] = 21; first[4] = 99;")}}
            byte flag = {{flag}};
            {{(helper ? "Update(first, flag);" : update)}}
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte value) { poke(0x6005, 44); return value; }
            {{(helper ? "static void Update(byte[] first, byte flag) {" + update + "}" : "")}}
            """);
        Assert.Equal(new byte[] { 21, 99 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(44, cpu.Memory[0x6005]);
        Assert.Equal(1, cpu.WrittenAddresses.Count(a => a == 0x6005));
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void DifferentConditionalLocalArrayIdentitiesAreDiagnosed(bool helper, bool chained, bool nested)
    {
        string selection = nested ? "flag != 0 ? (flag == 1 ? first : second) : first" : "flag != 0 ? first : second";
        string update = $$"""
            byte[] alias;
            {{(chained ? "byte[] other; other = alias = " : "alias = ")}}{{selection}};
            byte frame = 0;
            while (frame < 2)
            {
                alias[3] = 77;
                {{(chained ? "other[4] = 99;" : "")}}
                frame++;
            }
            """;
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] first = new byte[8];
            byte[] second = new byte[8];
            byte flag = 1;
            {{(helper ? "Update(first, second, flag);" : update)}}
            while (true) ;
            {{(helper ? "static void Update(byte[] first, byte[] second, byte flag) {" + update + "}" : "")}}
            """));
        Assert.Contains("array alias", exception.Message);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void SameConditionalLocalArrayIdentityRemainsSupported(bool helper, byte flag)
    {
        string update = """
            byte[] other = first;
            byte[] alias = flag != 0 ? first : other;
            byte frame = 0;
            while (frame < 2)
            {
                alias[3] = 77;
                frame++;
            }
            """;
        var cpu = ExecuteProgram(
            $$"""
            byte[] first = new byte[8];
            first[4] = 99;
            byte flag = {{flag}};
            {{(helper ? "Update(first, flag);" : update)}}
            byte result = first[3];
            byte neighbor = first[4];
            poke(0x6000, result);
            poke(0x6001, neighbor);
            test_stop();
            while (true) ;
            static extern void test_stop();
            {{(helper ? "static void Update(byte[] first, byte flag) {" + update + "}" : "")}}
            """);
        Assert.Equal(new byte[] { 77, 99 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
