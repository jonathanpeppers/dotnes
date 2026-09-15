using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

public class ReorderedArgumentExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ReorderedNamedArgumentsPreserveFrozenConsumerRepro()
    {
        var cpu = ExecuteProgram(
            """
            Operations.Store(second: Operations.Read(2), first: Operations.Read(1));
            byte value = Operations.First;
            poke(0x6000, value);
            value = Operations.Second;
            poke(0x6001, value);
            value = Operations.Count;
            poke(0x6002, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Operations
            {
                public static byte First, Second, Count;
                public static byte Value => 2;
                public static void Store(byte first, byte second, byte unused = 0)
                {
                    First = first;
                    Second = second;
                }
                public static byte Read(byte value) { Count++; return value; }
            }
            """);
        Assert.Equal(new byte[] { 1, 2, 2 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    public static TheoryData<string, byte, byte> ReorderedCalls()
    {
        var data = new TheoryData<string, byte, byte>();
        foreach (string call in new[]
        {
            "Store(second: Read(second), first: Read(first));",
            "byte saved = Read(second); Store(Read(first), saved);",
            "Store(second: Read(second), first: Read(Read(first)));",
            "byte saved = Read(second); Store(Read(Read(first)), saved);",
        })
        foreach (var (first, second) in new (byte, byte)[] { (1, 2), (0, 255), (255, 0), (127, 128) })
            data.Add(call, first, second);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReorderedCalls))]
    public void ReorderedArgumentsKeepValuesOrderAndDefaultSlot(string call, byte first, byte second)
    {
        bool nested = call.Contains("Read(Read(");
        using var transpiler = BuildProgram(
            $$"""
            byte first = {{first}}, second = {{second}};
            {{call}}
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Read(byte value)
            {
                byte count = peek(0x6003);
                poke((ushort)(0x6010 + count), value);
                poke(0x6003, (byte)(count + 1));
                return value;
            }
            static void Store(byte first, byte second, byte unused = 0)
            {
                poke(0x6000, first);
                poke(0x6001, second);
            }
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        bool frameReady = program.Labels.TryResolve("Store:@parameters_ready", out ushort store);
        if (!frameReady)
            Assert.True(program.Labels.TryResolve("Store", out store));
        var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
        cpu.RunUntil(store);
        Assert.Equal(0, cpu.A);
        int frameBytes = frameReady ? 3 : 2;
        Assert.Equal(Cpu6502.SoftwareStackTop - frameBytes, cpu.SoftwareStackPointer);
        Assert.Equal(frameReady ? new byte[] { 0, second, first } : new[] { second, first },
            cpu.Memory[(Cpu6502.SoftwareStackTop - frameBytes)..Cpu6502.SoftwareStackTop]);
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new[] { first, second }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(nested ? 3 : 2, cpu.Memory[0x6003]);
        Assert.Equal(nested ? new[] { second, first, first } : new[] { second, first },
            cpu.Memory[0x6010..(nested ? 0x6013 : 0x6012)]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0x0800)]
    [InlineData(0x0801)]
    [InlineData(0x08FF)]
    public void ReloadedLastArgumentKeepsBalancedFrameAcrossStackWrap(ushort stackTop)
    {
        var cpu = ExecuteProgram(
            """
            byte saved = Read(255);
            Store(Read(0), saved);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Read(byte value) { poke(0x6010, value); return value; }
            static void Store(byte first, byte second)
            {
                poke(0x6000, first);
                poke(0x6001, second);
            }
            """, cpu =>
            {
                cpu.Memory[0x22] = (byte)stackTop;
                cpu.Memory[0x23] = (byte)(stackTop >> 8);
            });
        Assert.Equal(new byte[] { 0, 255 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(stackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("byte saved = Read(second); Store(Read(first), saved, first);")]
    [InlineData("byte a = Read(first), b = Read(second); Store(a, b, first);")]
    public void ReloadedArgumentsInsideCallerKeepParameterOffsets(string call)
    {
        var cpu = ExecuteProgram(
            $$"""
            Forward(17, 255);
            test_stop(); while (true) ;
            static extern void test_stop();
            static void Forward(byte first, byte second) { {{call}} }
            static byte Read(byte value) { poke(0x6010, value); return value; }
            static void Store(byte first, byte second, byte third)
            {
                poke(0x6000, first);
                poke(0x6001, second);
                poke(0x6002, third);
            }
            """);
        Assert.Equal(new byte[] { 17, 255, 17 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCalleeReceivesReloadedBytesAndWordTail(bool wordTail)
    {
        using var transpiler = BuildProgram(
            $$"""
            byte saved = Read(255);
            Store(Read(17), saved, {{(wordTail ? "0x1234" : "0")}});
            test_stop(); while (true) ;
            static extern void test_stop();
            static extern void Store(byte first, byte second, {{(wordTail ? "ushort" : "byte")}} last);
            static byte Read(byte value) { poke(0x6010, value); return value; }
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        var native = program.CreateBlock("_Store");
        native.Emit(STA_abs(0x6002));
        if (wordTail)
            native.Emit(STX_abs(0x6003));
        native.Emit(LDY(0)).Emit(LDA_ind_Y(0x22))
            .Emit(STA_abs(0x6001))
            .Emit(LDY(1)).Emit(LDA_ind_Y(0x22))
            .Emit(STA_abs(0x6000))
            .Emit(JSR("incsp2")).Emit(RTS());
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, program.GetLabels()["main"]);
        cpu.RunUntil(0x7FF0);
        Assert.Equal(wordTail ? new byte[] { 17, 255, 0x34, 0x12 } : new byte[] { 17, 255, 0, 0 },
            cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("byte", "(byte)(Read(255) + 1)", 0)]
    [InlineData("sbyte", "(sbyte)Read(128)", 128)]
    public void ComputedByteArgumentKeepsItsNarrowedValue(string type, string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte saved = Read(127);
            Store({{expression}}, saved);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Read(byte value)
            {
                byte count = peek(0x6010);
                poke(0x6010, (byte)(count + 1));
                return value;
            }
            static void Store({{type}} first, byte second)
            {
                poke(0x6000, (byte)first);
                poke(0x6001, second);
            }
            """);
        Assert.Equal(new byte[] { expected, 127 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(2, cpu.Memory[0x6010]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
