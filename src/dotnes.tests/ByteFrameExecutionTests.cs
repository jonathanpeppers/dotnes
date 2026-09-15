using dotnes.ObjectModel;
using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ByteFrameExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0, 13)]
    [InlineData(1, 29)]
    [InlineData(255, 29)]
    public void IncomingLastArgumentHasValueFlags(byte value, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte direct = Choose(17, {{value}});
            byte forwarded = Forward(17, {{value}});
            poke(0x6000, direct);
            poke(0x6001, forwarded);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Forward(byte first, byte last) => Choose(first, last);
            static byte Choose(byte first, byte last) { if (last == 0) return 13; return 29; }
            """);
        Assert.Equal(new[] { expected, expected }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0x0800)]
    [InlineData(0x0801)]
    [InlineData(0x0802)]
    [InlineData(0x0803)]
    [InlineData(0x08FF)]
    public void BatchedFrameKeepsArgumentOrderAndStackWrap(ushort stackTop)
    {
        using var transpiler = BuildProgram(
            """
            byte first = 17, second = 128, third = 255;
            Store(first, second, third);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Store(byte first, byte second, byte third)
            {
                poke(0x6000, first);
                poke(0x6001, second);
                poke(0x6002, third);
            }
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        Assert.True(program.Labels.TryResolve("Store:@parameters_ready", out ushort frame));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.Memory[0x22] = (byte)stackTop;
        cpu.Memory[0x23] = (byte)(stackTop >> 8);
        cpu.RunUntil(frame);
        Assert.Equal(stackTop - 3, cpu.SoftwareStackPointer);
        Assert.Equal(new byte[] { 255, 128, 17 }, cpu.Memory[(stackTop - 3)..stackTop]);
        Assert.Equal(255, cpu.A);
        Assert.Equal(0, cpu.Y);
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new byte[] { 17, 128, 255 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(stackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void GeneratedFrameEntryCannotAliasAUserMethodName()
    {
        var cpu = ExecuteProgram(
            """
            Store(17, 128);
            Store_parameters_ready(43);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Store(byte first, byte last)
            {
                poke(0x6000, first);
                poke(0x6001, last);
            }
            static void Store_parameters_ready(byte value) => poke(0x6002, value);
            """);
        Assert.Equal(new byte[] { 17, 128, 43 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ForwardedParametersKeepTheirOriginalFrameOffsets()
    {
        var cpu = ExecuteProgram(
            """
            Forward(17, 128, 255, 43);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Forward(byte first, byte second, byte third, byte fourth)
            {
                Store(fourth, first, third, second);
                poke(0x6004, first);
            }
            static void Store(byte a, byte b, byte c, byte d)
            {
                poke(0x6000, a);
                poke(0x6001, b);
                poke(0x6002, c);
                poke(0x6003, d);
            }
            """);
        Assert.Equal(new byte[] { 43, 17, 255, 128, 17 }, cpu.Memory[0x6000..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ClosureContextDoesNotOccupyAByteArgumentSlot()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 5, first = 17, last = 128;
            Store(first, last);
            byte result = Read();
            poke(0x6002, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            byte Read() => captured;
            void Store(byte a, byte b)
            {
                poke(0x6000, a);
                poke(0x6001, b);
                captured++;
            }
            """);
        Assert.Equal(new byte[] { 17, 128, 6 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void NativeCallerCanStillUseTheOriginalEntryAndAbi()
    {
        using var transpiler = BuildProgram(
            """
            Store(17, 128, 255);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Store(byte first, byte second, byte third)
            {
                poke(0x6000, first);
                poke(0x6001, second);
                poke(0x6002, third);
            }
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var native = program.CreateBlock("native_caller");
        native.Emit(LDA(17)).Emit(JSR("pusha"))
            .Emit(LDA(128)).Emit(JSR("pusha"))
            .Emit(LDA(255)).Emit(JSR("Store"))
            .Emit(JMP("_test_stop"));
        byte[] bytes = program.ToBytes();
        var cpu = new Cpu6502(bytes, program.BaseAddress, program.GetBlockAddress(native));
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new byte[] { 17, 128, 255 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Fact]
    public void NativeInterruptCanReenterAParameterOnlyCalleeAtEveryInstruction()
    {
        using var transpiler = BuildProgram(
            """
            byte value = Choose(17, 128, 255);
            poke(0x6000, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second, byte third) => third;
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var handler = program.CreateBlock("native_interrupt");
        handler.Emit(PHA()).Emit(TXA()).Emit(PHA()).Emit(TYA()).Emit(PHA())
            .Emit(LDA(1)).Emit(JSR("pusha"))
            .Emit(LDA(2)).Emit(JSR("pusha"))
            .Emit(LDA(3)).Emit(JSR("Choose"))
            .Emit(PLA()).Emit(TAY()).Emit(PLA()).Emit(TAX()).Emit(PLA())
            .Emit(new Instruction(Opcode.RTI, AddressMode.Implied));
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        ushort interrupt = program.GetBlockAddress(handler);
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.Memory[0xFFFA] = (byte)interrupt;
        cpu.Memory[0xFFFB] = (byte)(interrupt >> 8);
        int steps = 0;
        while (cpu.PC != 0x7FF0 && steps++ < 1000)
        {
            ushort pc = cpu.PC;
            ushort sp = cpu.SoftwareStackPointer;
            byte status = cpu.Status;
            cpu.Nmi();
            cpu.RunUntil(program.GetInstructionAddress(handler, handler.Count - 1));
            cpu.Step();
            Assert.Equal(pc, cpu.PC);
            Assert.Equal(sp, cpu.SoftwareStackPointer);
            Assert.Equal(status, cpu.Status);
            cpu.Step();
        }
        Assert.Equal(0x7FF0, cpu.PC);
        Assert.Equal(255, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
