using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void IndexIsEvaluatedOnceBeforeSideEffectingValue()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            State.Index = 3;
            State.Calls = 0;
            byte frame = 0;
            while (frame < 2)
            {
                data[State.GetIndex()] = State.GetValue();
                frame++;
            }
            byte value = data[3];
            poke(0x6000, value);
            value = data[5];
            poke(0x6001, value);
            value = State.Calls;
            poke(0x6002, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Index;
                public static byte Calls;
                public static byte GetIndex()
                {
                    Calls++;
                    return Index;
                }
                public static byte GetValue()
                {
                    Index = 5;
                    Calls++;
                    return 91;
                }
            }
            """);
        Assert.Equal(new byte[] { 91, 91, 4 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void DifferentArraysAndOffsetsKeepTheirOwnValues()
    {
        var cpu = ExecuteProgram(
            """
            byte[] first = new byte[8];
            byte[] second = new byte[8];
            first[3] = 17;
            first[5] = 99;
            second[3] = 40;
            second[5] = 23;
            State.Value = 7;
            byte index = 3;
            byte frame = 0;
            while (frame < 2)
            {
                first[index] = (byte)(first[index] + second[index + 2]);
                second[index + 2] = State.Value;
                frame++;
            }
            byte value = first[3];
            poke(0x6000, value);
            value = first[5];
            poke(0x6001, value);
            value = second[3];
            poke(0x6002, value);
            value = second[5];
            poke(0x6003, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Value;
            }
            """);
        Assert.Equal(new byte[] { 47, 99, 40, 7 }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void IndexedReadModifyWrite()
    {
        var cpu = ExecuteProgram(
            """
            byte[] actors = new byte[8];
            actors[0] = 21;
            actors[3] = 75;
            byte index = 0;
            while (index < 1)
            {
                actors[3] = (byte)(actors[3] + 1);
                index++;
            }
            byte value = actors[3];
            poke(0x6000, value);
            value = actors[0];
            poke(0x6001, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(76, cpu.Memory[0x6000]);
        Assert.Equal(21, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("value = data[8 + slot]; value = (byte)(value + 1); data[8 + slot] = value;")]
    [InlineData("data[8 + slot] = (byte)(data[8 + slot] + 1);")]
    [InlineData("data[8 + slot]++;")]
    public void ComputedIndexRepeatedWrites(string update)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[32];
            data[8] = 21;
            data[11] = 75;
            byte slot = 3;
            byte value = 0;
            byte frame = 0;
            while (frame < 3)
            {
                {{update}}
                frame++;
            }
            value = data[11];
            poke(0x6000, value);
            value = data[8];
            poke(0x6001, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(78, cpu.Memory[0x6000]);
        Assert.Equal(21, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1 + index")]
    public void StoresNestedHelperReturnValue(string target)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] state = new byte[8];
            byte index = 3;
            byte frame = 0;
            while (frame < 2)
            {
                state[{{target}}] = ReadData(index);
                frame++;
            }
            byte value = state[{{target}}];
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte ReadData(byte index) => Reader.Read(index);
            static class Reader
            {
                public static byte Read(byte index) => (byte)(index + 7);
            }
            """);
        Assert.Equal(10, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
