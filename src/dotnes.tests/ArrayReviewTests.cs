using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayReviewTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("3", "x + y", 0)]
    [InlineData("index", "State.First() + State.Second()", 2)]
    [InlineData("3", "State.First() + State.Second()", 2)]
    public void StoreKeepsTheCompleteValueExpression(string index, string value, byte calls)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            byte x = 11;
            byte y = 22;
            byte index = 3;
            byte frame = 0;
            while (frame < 1)
            {
                data[{{index}}] = (byte)({{value}});
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = State.Calls;
            poke(0x6001, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Calls;
                public static byte First() { Calls++; return 11; }
                public static byte Second() { Calls++; return 22; }
            }
            """);
        Assert.Equal(33, cpu.Memory[0x6000]);
        Assert.Equal(calls, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void OptimizedAllocationKeepsItsIdentity()
    {
        var cpu = ExecuteProgram(
            """
            byte[] actors = new byte[8];
            actors[3] = 75;
            Update(actors, 3);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte[] actors, byte index)
            {
                actors[index]++;
                byte value = actors[index];
                poke(0x6000, value);
            }
            """);
        Assert.Equal(76, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ConditionalArrayAliasIsDiagnosed()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            while (true) ;
            static void Set(byte[] a, byte[] b, byte flag)
            {
                byte[] alias = a;
                if (flag != 0)
                    alias = b;
                alias[3] = 77;
            }
            """));
        Assert.Contains("array alias", exception.Message);
    }

    [Theory]
    [InlineData("ram, table")]
    [InlineData("table, ram")]
    public void MixedRamAndRomArgumentsAreDiagnosed(string arrays)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] ram = new byte[8];
            ram[3] = 21;
            byte[] table = new byte[] { 1, 2, 3, 44 };
            byte frame = 0;
            while (frame < 1)
            {
                Copy({{arrays}}, 3);
                frame++;
            }
            while (true) ;
            static void Copy(byte[] target, byte[] source, byte index)
            {
                target[index] = source[index];
            }
            """));
        Assert.Contains("RAM and read-only ROM", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IdenticalAliasesAcrossBranchesKeepTheirIdentity(byte flag)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 21;
            Set(data, {{flag}});
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Set(byte[] data, byte flag)
            {
                byte[] alias = data;
                if (flag != 0)
                    alias = data;
                alias[3]++;
                byte result = data[3];
                poke(0x6000, result);
            }
            """);
        Assert.Equal(22, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ExistingRomOnlyHelperIsNotRejected()
    {
        var bytes = GetProgramBytes(
            """
            byte[] colors = new byte[] { 15, 1, 17, 33 };
            Setup(colors);
            while (true) ;
            static void Setup(byte[] colors) { pal_bg(colors); }
            """);
        Assert.NotEmpty(bytes);
    }

    [Theory]
    [InlineData("ushort")]
    [InlineData("int")]
    public void UnsupportedScalarWidthsInArrayHelpersAreDiagnosed(string type)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] data = new byte[8];
            Set(data, 0x1234);
            while (true) ;
            static void Set(byte[] data, {{type}} value)
            {
                data[3] = (byte)(value >> 8);
            }
            """));
        Assert.Contains("scalar", exception.Message);
    }

    [Fact]
    public void ConditionalStoreValueIsDiagnosed()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            byte[] data = new byte[8];
            byte flag = (byte)pad_poll(0);
            data[3] = flag == 0 ? (byte)11 : (byte)22;
            while (true) ;
            """));
        Assert.Contains("unsupported control flow", exception.Message);
    }

    [Fact]
    public void ScalarCallBeforeArrayAccessKeepsArgumentFrame()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 21;
            byte frame = 0;
            while (frame < 2)
            {
                Set(data, 3);
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Set(byte[] data, byte index)
            {
                Ignore(index, index);
                data[index] = 77;
            }
            static void Ignore(byte x, byte y) { }
            """);
        Assert.Equal(77, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
