using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class StaticArrayAliasTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IdenticalStaticBindingsAcrossBranchesRemainValid(byte flag)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 21;
            State.Data = data;
            byte branch = {{flag}};
            if (branch != 0)
                State.Data = data;
            else
                State.Data = State.Data;
            Update(State.Data);
            byte value = data[3];
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte[] data) { data[3]++; }
            static class State { public static byte[] Data; }
            """);
        Assert.Equal(22, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void StaticAliasCannotCaptureAHelperFramePointer()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            byte[] data = new byte[8];
            Capture(data);
            while (true) ;
            static void Capture(byte[] data) { State.Data = data; }
            static class State { public static byte[] Data; }
            """));
        Assert.Contains("static array alias", exception.Message);
        Assert.Contains("helper parameter", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedStaticAliasSharesCallerArrayAcrossHelpers(bool implicitField)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 21;
            data[4] = 99;
            State.Data = data;
            State.Alias = State.Data;
            {{(implicitField ? "Update(); Update();" : "Update(State.Data); Update(State.Alias);")}}
            byte a = data[3];
            byte b = State.Alias[3];
            byte c = data[4];
            poke(0x6000, a);
            poke(0x6001, b);
            poke(0x6002, c);
            test_stop();
            while (true) ;
            static extern void test_stop();
            {{(implicitField ? "static void Update() { State.Data[3]++; }" : "static void Update(byte[] data) { data[3]++; }")}}
            static class State { public static byte[] Data; public static byte[] Alias; }
            """);
        Assert.Equal(new byte[] { 23, 23, 99 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("State.Data = second;")]
    [InlineData("State.Data = new byte[8];")]
    [InlineData("if (flag != 0) State.Data = second;")]
    public void DifferingStaticAliasReassignmentIsDiagnosed(string assignment)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] first = new byte[8];
            byte[] second = new byte[8];
            byte flag = (byte)pad_poll(0);
            State.Data = first;
            {{assignment}}
            Update(State.Data);
            while (true) ;
            static void Update(byte[] data) { data[3]++; }
            static class State { public static byte[] Data; }
            """));
        Assert.Contains("array alias", exception.Message);
    }
}
