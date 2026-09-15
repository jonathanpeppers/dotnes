using dotnes.ObjectModel;
using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ByteCaptureExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    const string Source = """
        Store(peek(0x6002));
        test_stop(); while (true) ;
        static extern void test_stop();
        static void Store(byte value)
        {
            byte captured = value;
            if (peek(0x6001) != 0) poke(0x6000, captured);
        }
        """;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(17, 1)]
    [InlineData(255, 1)]
    [InlineData(17, 0)]
    public void CapturedLeafKeepsItsExistingStorageAndNeedsNoParameterSlot(byte value, byte enabled)
    {
        using var transpiler = BuildProgram(Source, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var leaf = program.GetBlock("Store")!;
        Assert.DoesNotContain(leaf.InstructionsWithLabels, i => i.Instruction.Opcode == Opcode.JSR);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.Memory[0x6002] = value;
        cpu.Memory[0x6001] = enabled;
        cpu.Memory[Cpu6502.SoftwareStackTop - 1] = 0xCC;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(enabled == 0 ? 0 : value, cpu.Memory[0x6000]);
        Assert.Equal(0xCC, cpu.Memory[Cpu6502.SoftwareStackTop - 1]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void NativeCallerStillSuppliesTheByteInA()
    {
        using var transpiler = BuildProgram(Source, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var native = program.CreateBlock("native_caller");
        native.Emit(LDA(255)).Emit(JSR("Store")).Emit(JMP("_test_stop"));
        byte[] bytes = program.ToBytes();
        var cpu = new Cpu6502(bytes, program.BaseAddress, program.GetBlockAddress(native));
        cpu.Memory[0x6001] = 1;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(255, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Theory]
    [InlineData("observe();", "static extern void observe();")]
    [InlineData("poke(0x6003, peek(0x22));", "")]
    [InlineData("poke(0x6003, peek(0x0822));", "")]
    [InlineData("poke(0x6003, peek(0x0400));", "")]
    [InlineData("poke(0x6003, peek(0x07FF));", "")]
    [InlineData("poke(0x6003, peek(0x0FFF));", "")]
    [InlineData("poke(0x6003, peek(0x17FF));", "")]
    [InlineData("poke(0x6003, peek(0x1FFF));", "")]
    [InlineData("poke(0x07FF, captured);", "")]
    [InlineData("poke(0x0FFF, captured);", "")]
    public void OpaqueCallsAndStackObserversRetainTheirFrames(string operation, string declaration)
    {
        using var transpiler = BuildProgram(
            $$"""
            Store(17);
            test_stop(); while (true) ;
            static extern void test_stop();
            {{declaration}}
            static void Store(byte value)
            {
                byte captured = value;
                {{operation}}
                poke(0x6000, captured);
            }
            """, out var program);
        Assert.Equal(JSR("pusha"), program.GetBlock("Store")![0]);
    }

    [Theory]
    [InlineData(0x0400)]
    [InlineData(0x07FF)]
    public void DirectSoftwareStackReadSeesThePushedParameter(ushort address)
    {
        using var transpiler = BuildProgram(
            $$"""
            Store(17);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Store(byte value)
            {
                byte captured = value;
                poke(0x6000, peek({{address}}));
                poke(0x6001, captured);
            }
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        ushort stack = (ushort)(address + 1);
        cpu.Memory[NESConstants.sp] = (byte)stack;
        cpu.Memory[NESConstants.sp + 1] = (byte)(stack >> 8);
        cpu.Memory[address] = 0xCC;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(17, cpu.Memory[0x6000]);
        Assert.Equal(17, cpu.Memory[0x6001]);
        Assert.Equal(stack, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void RecursiveCapturesStillProduceTheExistingDiagnostic()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildProgram(
            """
            Store(17);
            while (true) ;
            static void Store(byte value)
            {
                byte captured = value;
                if (captured != 0) Store((byte)(captured - 1));
                poke(0x6000, captured);
            }
            """, out _));
        Assert.Contains("Recursive call cycle", error.Message);
    }

    [Fact]
    public void BranchBackToParameterCaptureRetainsTheFrame()
    {
        using var transpiler = BuildProgram(
            """
            Store(17);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Store(byte value)
            {
            again:
                byte captured = value;
                if (peek(0x6001) != 0)
                {
                    poke(0x6001, 0);
                    goto again;
                }
                poke(0x6000, captured);
            }
            """, out var program);
        Assert.Equal(JSR("pusha"), program.GetBlock("Store")![0]);
    }

    [Fact]
    public void NativeInterruptCanUseTheSoftwareStackDuringTheCapturedLeaf()
    {
        using var transpiler = BuildProgram(Source, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var handler = program.CreateBlock("native_interrupt");
        handler.Emit(PHA()).Emit(TXA()).Emit(PHA()).Emit(TYA()).Emit(PHA())
            .Emit(LDA(13)).Emit(JSR("pusha")).Emit(JSR("incsp1"))
            .Emit(PLA()).Emit(TAY()).Emit(PLA()).Emit(TAX()).Emit(PLA())
            .Emit(new Instruction(Opcode.RTI, AddressMode.Implied));
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        ushort interrupt = program.GetBlockAddress(handler);
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.Memory[0x6001] = 1;
        cpu.Memory[0x6002] = 255;
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
    }
}
