using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("8 + index")]
    [InlineData("index + 8")]
    [InlineData("11 - index")]
    [InlineData("offset + index")]
    public void DirectIndexedReadMatchesExplicitFullWidthStorage(string expression)
    {
        string Source(bool explicitIndex) => $$"""
            byte[] data = new byte[32];
            data[8] = 41;
            data[11] = 73;
            byte index = 3, offset = 8;
            byte value = 0;
            byte frame = 0;
            while (frame < 2)
            {
                {{(explicitIndex ? $"int fullIndex = {expression}; value = data[fullIndex];" : $"value = data[{expression}];")}}
                poke(0x6000, value);
                frame++;
            }
            test_stop(); while (true) ;
            static extern void test_stop();
            """;
        var direct = ExecuteProgram(Source(false));
        var explicitStorage = ExecuteProgram(Source(true));
        Assert.Equal(expression == "11 - index" ? 41 : 73, direct.Memory[0x6000]);
        Assert.Equal(explicitStorage.Memory[0x6000], direct.Memory[0x6000]);
        Assert.True(direct.InstructionCount <= explicitStorage.InstructionCount,
            $"Direct: {direct.InstructionCount}; explicit storage: {explicitStorage.InstructionCount}");
        Assert.Equal(Cpu6502.SoftwareStackTop, direct.SoftwareStackPointer);
    }

    [Fact]
    public void DirectIndexedReadStoresThePromotedCarry()
    {
        using var transpiler = BuildProgram(
            """
            byte[] data = new byte[32];
            byte index = 255;
            byte value = data[8 + index];
            poke(0x6000, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, out var program);
        const ushort stop = 0x7FF0;
        program.DefineExternalLabel("_test_stop", stop);
        var highStore = Assert.Single(program.GetBlock("main")!.InstructionsWithLabels,
            item => item.Instruction.Opcode == Opcode.STX && item.Instruction.Mode == AddressMode.Absolute);
        ushort highAddress = Assert.IsType<AbsoluteOperand>(highStore.Instruction.Operand).Address;
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, program.GetLabels()["main"]);
        cpu.RunUntil(stop);
        // Observe the generated index storage, not the out-of-bounds array value.
        Assert.Equal(263, cpu.Memory[highAddress - 1] | cpu.Memory[highAddress] << 8);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("State.Index", 3, false)]
    [InlineData("(byte)(State.Index + 1)", 4, false)]
    [InlineData("(byte)(prefix + State.Index)", 4, false)]
    [InlineData("State.Index", 3, true)]
    [InlineData("(byte)(State.Index + 1)", 4, true)]
    [InlineData("(byte)(prefix + State.Index)", 4, true)]
    public void StaticTargetIndexIsCapturedBeforeValueCall(string index, int target, bool helper)
    {
        string store = $"byte prefix = 1; data[{index}] = State.GetValue();";
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 99;
            data[4] = 99;
            data[5] = 99;
            State.Index = 3;
            State.Calls = 0;
            {{(helper ? "Update(data);" : store)}}
            byte first = data[3];
            byte second = data[4];
            byte third = data[5];
            byte after = State.Index;
            byte calls = State.Calls;
            poke(0x6000, first);
            poke(0x6001, second);
            poke(0x6002, third);
            poke(0x6003, after);
            poke(0x6004, calls);
            test_stop();
            while (true) ;
            static extern void test_stop();
            {{(helper ? "static void Update(byte[] data) { " + store + " }" : "")}}
            static class State
            {
                public static byte Index;
                public static byte Calls;
                public static byte GetValue()
                {
                    Index = 5;
                    Calls++;
                    return 91;
                }
            }
            """);
        Assert.Equal(new byte[] { (byte)(target == 3 ? 91 : 99), (byte)(target == 4 ? 91 : 99), 99, 5, 1 },
            cpu.Memory[0x6000..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

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
