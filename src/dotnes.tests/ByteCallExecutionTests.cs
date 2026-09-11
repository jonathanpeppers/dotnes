using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteCallExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ReemittedByteArgumentsRegisterPushaDependency()
    {
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Choose", 2, true);
        ILInstruction[] il =
        [
            new(ILOpCode.Ldc_i4_s, 0, Integer: 21),
            new(ILOpCode.Ldc_i4_s, 1, Integer: 43),
            new(ILOpCode.Call, 2, String: "Choose"),
        ];
        using var writer = new IL2NESWriter(new MemoryStream(), reflectionCache: reflection)
        {
            Instructions = il,
            UsedMethods = new HashSet<string>(),
            UserMethodNames = new HashSet<string> { "Choose" },
            ByteParameterCalls = new Dictionary<string, int> { ["Choose"] = -1 },
        };
        writer.StartBlockBuffering();
        for (int i = 0; i < il.Length; i++)
        {
            writer.Index = i;
            writer.RecordBlockCount(il[i].Offset);
            if (il[i].Integer is int value)
                writer.Write(il[i], value);
            else
                writer.Write(il[i], il[i].String!);
        }
        Assert.Contains("pusha", writer.UsedMethods);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConditionalLeftOperandIsDiagnosedInsteadOfReplacedByAdjacentLoad(byte flag)
    {
        var exception = Assert.Throws<TranspileException>(() => ExecuteProgram(
            $$"""
            byte result = Select({{flag}});
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Select(byte flag)
            {
                byte a = 7;
                byte b = 8;
                byte c = 16;
                return (byte)((flag == 0 ? a : b) | c);
            }
            """));
        Assert.Contains("Merged scalar expression operands require typed conditional-value lowering", exception.Message);
    }

    [Fact]
    public void ByteArgumentsWorkAlongsideDecsp4Runtime()
    {
        var cpu = ExecuteProgram(
            """
            oam_spr(1, 2, 3, 0, 0);
            byte value = Choose(21, 43);
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second) => second;
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ClosureCallDoesNotLeaveAPhantomOperand()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Outer(43);
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Outer(byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                Touch();
                Ignore(value, value);
                byte v = value;
                poke(0x6000, v);
            }
            void Touch()
            {
                byte c = captured;
                poke(0x6002, c);
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(new byte[] { 43, 7, 7 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void NestedByteCallKeepsTheEnclosingArgument()
    {
        var cpu = ExecuteProgram(
            """
            Outer(11, Inner(22, 33));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Inner(byte first, byte second) => second;
            static void Outer(byte first, byte second)
            {
                byte a = first;
                byte b = second;
                poke(0x6000, a);
                poke(0x6001, b);
            }
            """);
        Assert.Equal(new byte[] { 11, 33 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ByteReturnReplacesPadPollProvenance()
    {
        var cpu = ExecuteProgram(
            """
            byte buttons = (byte)pad_poll(0);
            byte value = Choose(21, 43);
            byte result = (byte)(value & 15);
            poke(0x6000, result);
            poke(0x6001, buttons);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Choose(byte first, byte second) => second;
            """);
        Assert.Equal(11, cpu.Memory[0x6000]);
        Assert.Equal(0, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ForwardedByteArgumentsKeepCallerFrame()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Forward(43);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Forward(byte value)
            {
                Ignore(value, value);
                return value;
            }
            static void Ignore(byte first, byte second) { }
            """);
        Assert.Equal(43, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
