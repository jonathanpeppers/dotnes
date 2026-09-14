using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayParameterTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void SingleArrayHelperPrologueIsIncludedWithDecsp4Runtime()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 21;
            oam_spr(1, 2, 3, 0, 0);
            Update(data);
            byte result = data[3];
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte[] data) { data[3]++; }
            """);
        Assert.Equal(22, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("target[index]++;", 24)]
    [InlineData("target[1 + index] += source[index];", 48)]
    public void CompoundUpdatesThroughArrayParameters(string update, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            byte[] delta = new byte[8];
            data[3] = 21;
            data[4] = 21;
            delta[3] = 9;
            byte frame = 0;
            while (frame < 3)
            {
                Helpers.Update(data, delta, 3);
                frame++;
            }
            byte value = data[{{(update.Contains("1 +") ? 4 : 3)}}];
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static void Update(byte[] target, byte[] source, byte index)
                {
                    {{update}}
                }
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void NestedHelpersPreserveAliasesAndCallerLocalsAcrossPages()
    {
        var cpu = ExecuteProgram(
            """
            byte[] first = new byte[256];
            byte[] second = new byte[256];
            first[250] = 20;
            first[251] = 99;
            second[250] = 7;
            byte index = 250;
            byte live = 43;
            byte frame = 0;
            while (frame < 3)
            {
                Helpers.Outer(first, second, index);
                frame++;
            }
            byte value = first[250];
            poke(0x6000, value);
            value = first[251];
            poke(0x6001, value);
            value = second[250];
            poke(0x6002, value);
            poke(0x6003, live);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static void Outer(byte[] first, byte[] second, byte index)
                {
                    byte[] alias = first;
                    Inner(alias, second, index);
                }
                public static void Inner(byte[] target, byte[] source, byte index)
                {
                    byte value = target[index];
                    byte addend = source[index];
                    value = (byte)(value + addend);
                    target[index] = value;
                }
            }
            """);
        Assert.Equal(new byte[] { 41, 99, 7, 43 }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void SameArrayCanBePassedTwice()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 21;
            byte frame = 0;
            while (frame < 2)
            {
                Helpers.Update(data, data);
                frame++;
            }
            byte value = data[3];
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static void Update(byte[] target, byte[] alias)
                {
                    byte value = alias[3];
                    value = (byte)(value + 1);
                    target[3] = value;
                }
            }
            """);
        Assert.Equal(23, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("ushort[]")]
    [InlineData("int[]")]
    [InlineData("byte[][]")]
    [InlineData("byte[,]")]
    public void UnsupportedArrayParameterTypesAreDiagnosed(string type)
    {
        var exception = Assert.Throws<dotnes.ObjectModel.TranspileException>(() => GetProgramBytes(
            $$"""
            while (true) ;
            static class Helpers
            {
                public static byte Read({{type}} data) => 0;
            }
            """));
        Assert.Contains("fixed byte[]", exception.Message);
    }

    [Fact]
    public void ByteArrayParameterCanBeReadAndWritten()
    {
        var cpu = ExecuteProgram(
            """
            byte[] actors = new byte[8];
            actors[0] = 21;
            actors[3] = 75;
            byte frame = 0;
            while (frame < 3)
            {
                Helpers.Update(actors, 3);
                frame++;
            }
            byte value = actors[3];
            poke(0x6000, value);
            value = actors[0];
            poke(0x6001, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static void Update(byte[] actors, byte index)
                {
                    byte position = index;
                    byte value = actors[position];
                    value = (byte)(value + 1);
                    actors[position] = value;
                }
            }
            """);
        Assert.Equal(78, cpu.Memory[0x6000]);
        Assert.Equal(21, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
