using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class StaticArrayBindingReviewTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovenStaticReassignmentIsDiagnosed(bool readAfter)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] data = new byte[8];
            State.Data = data;
            State.Data = null;
            {{(readAfter ? "byte value = State.Data[3]; poke(0x6000, value);" : "")}}
            while (true) ;
            static class State { public static byte[] Data; }
            """));
        Assert.Contains(readAfter ? "array" : "Ldnull", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticReadRequiresAnExecutedBinding(bool conditional)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] data = new byte[8];
            byte flag = 1;
            {{(conditional ? "if (flag == 0) State.Data = data;" : "")}}
            byte value = State.Data[3];
            poke(0x6000, value);
            {{(conditional ? "" : "State.Data = data;")}}
            while (true) ;
            static class State { public static byte[] Data; }
            """));
        Assert.Contains("array", exception.Message);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void DefiniteBindingsThroughHelpersAndBothBranchesRemainSupported(bool conditional, byte flag)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte flag = {{flag}};
            Initialize(flag);
            Use();
            Use();
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Initialize(byte flag)
            {
                byte[] data = new byte[8];
                data[3] = 77;
                data[4] = 99;
                {{(conditional ? "if (flag == 0) State.Data = data; else State.Data = data;" : "State.Data = data;")}}
            }
            static void Use()
            {
                byte value = State.Data[3];
                byte neighbor = State.Data[4];
                poke(0x6000, value);
                poke(0x6001, neighbor);
            }
            static class State { public static byte[] Data; }
            """);
        Assert.Equal(new byte[] { 77, 99 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HelperUseRequiresADefiniteCallerBinding(bool conditional)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte flag = 1;
            {{(conditional ? "if (flag == 0) Initialize();" : "")}}
            Use();
            {{(conditional ? "" : "Initialize();")}}
            while (true) ;
            static void Initialize()
            {
                byte[] data = new byte[8];
                State.Data = data;
            }
            static void Use()
            {
                byte value = State.Data[3];
                poke(0x6000, value);
            }
            static class State { public static byte[] Data; }
            """));
        Assert.Contains("initialized on every path", exception.Message);
    }
}
