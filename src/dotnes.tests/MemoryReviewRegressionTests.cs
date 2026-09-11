using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class MemoryReviewRegressionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ByteCallRegistersPushaAlongsideDecsp4()
    {
        const string source = """
            using (var oam = new OamScope())
            {
                oam.spr(10, 20, 1, 0);
            }
            byte result = Add(17, 29);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Add(byte left, byte right) => (byte)(left + right);
            """;
        using (var transpiler = BuildProgram(source, out var program))
        {
            Assert.Contains("decsp4", transpiler.UsedMethods);
            Assert.Contains("pusha", transpiler.UsedMethods);
            Assert.Contains(program.Blocks, block => block.Label == "pusha");
        }
        var cpu = ExecuteProgram(source);
        Assert.Equal(46, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(PrimitiveTypeCode.Int16, ILOpCode.Conv_i2)]
    [InlineData(PrimitiveTypeCode.UInt16, ILOpCode.Conv_u2)]
    public void TypedSpillRetainsItsConversion(PrimitiveTypeCode type, ILOpCode conversion)
    {
        ILInstruction[] instructions =
        [
            new(ILOpCode.Ldarg_0, 0),
            new(ILOpCode.Conv_i2, 1),
            new(ILOpCode.Call, 2, String: "Next"),
            new(ILOpCode.Add, 7),
            new(ILOpCode.Ret, 8),
        ];
        var reflection = new ReflectionCache();
        reflection.RegisterUserMethod("Next", 0, true);
        var analysis = new ILValueAnalysis(instructions, reflection);
        var selected = new HashSet<int> { 1, 2 };
        var rewritten = ILExpressionSpiller.Rewrite(instructions, analysis, selected,
            new HashSet<int> { 1 }, signedWordProducers: type == PrimitiveTypeCode.Int16 ? new HashSet<int> { 1 } : null);
        int store = Array.FindIndex(rewritten, instruction => instruction.GetStlocIndex() != null);
        Assert.Equal(conversion, rewritten[store - 1].OpCode);
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    [InlineData(0x7F)]
    public void SignedWordSurvivesACall(byte input)
    {
        var cpu = ExecuteProgram("""
            short value = (short)((short)(sbyte)peek(0x6010) + Next());
            poke(0x6000, (byte)value);
            poke(0x6001, (byte)(value >> 8));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 0;
            """, cpu => cpu.Memory[0x6010] = input);
        short expected = (sbyte)input;
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public void SignedBitwiseResultIsExtendedWhenCaptured(byte input)
    {
        var cpu = ExecuteProgram("""
            short value = (short)(((sbyte)peek(0x6010) | (sbyte)peek(0x6011)) + Next());
            poke(0x6000, (byte)value);
            poke(0x6001, (byte)(value >> 8));
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 0;
            """, cpu => cpu.Memory[0x6010] = input);
        Assert.Equal(input, cpu.Memory[0x6000]);
        Assert.Equal(0xFF, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void Unsigned32ComparisonStorageIsRejected()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            uint left = peek(0x6010);
            uint right = peek(0x6011);
            poke(0x6020, (byte)left);
            poke(0x6021, (byte)right);
            if (left < right)
                poke(0x6000, 1);
            while (true) ;
            """));
        Assert.Contains("unsupported primitive type UInt32", error.Message);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(255, 0)]
    [InlineData(255, 255)]
    [InlineData(128, 127)]
    public void BoundedIntComparisonsAndArithmeticRetainTheirProvenRange(byte first, byte second)
    {
        var cpu = ExecuteProgram("""
            int left = peek(0x6010);
            int right = peek(0x6011);
            poke(0x6020, (byte)left);
            poke(0x6021, (byte)right);
            byte less = (byte)(left < right ? 1 : 0);
            poke(0x6002, less);
            int result = left + right;
            poke(0x6000, (byte)result);
            poke(0x6001, (byte)(result >> 8));
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = first;
                cpu.Memory[0x6011] = second;
            });
        Assert.Equal(first, cpu.Memory[0x6020]);
        Assert.Equal(second, cpu.Memory[0x6021]);
        Assert.Equal((byte)(first + second), cpu.Memory[0x6000]);
        Assert.Equal((byte)((first + second) >> 8), cpu.Memory[0x6001]);
        Assert.Equal(first < second ? 1 : 0, cpu.Memory[0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void Unsigned32ArithmeticStorageIsRejected()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            uint left = peek(0x6010);
            uint right = peek(0x6011);
            poke(0x6020, (byte)left);
            poke(0x6021, (byte)right);
            uint result = left + right;
            poke(0x6000, (byte)result);
            while (true) ;
            """));
        Assert.Contains("unsupported primitive type UInt32", error.Message);
        Assert.Contains("short", error.Message);
    }

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("uint", "UInt32")]
    public void WideDeclaredValuesCannotBeCapturedAcrossACall(string type, string primitive)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            {{type}} value = Read();
            poke(0x6000, (byte)(value + Next()));
            while (true) ;
            static {{type}} Read() => 123;
            static byte Next() => 1;
            """));
        Assert.Contains($"Return type {primitive}", error.Message);
    }

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("uint", "UInt32")]
    public void WideCallArgumentsCannotAllocateSyntheticWordLocals(string type, string primitive)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            Store(Read(), Next());
            while (true) ;
            static {{type}} Read() => 123;
            static byte Next() => 1;
            static void Store({{type}} value, byte other)
            {
                poke(0x6000, (byte)value);
                poke(0x6001, other);
            }
            """));
        Assert.Contains($"Return type {primitive}", error.Message);
    }

    [Fact]
    public void ConditionalJoinDoesNotRegisterEitherDifferentArmAsAConsumer()
    {
        ILInstruction[] instructions =
        [
            new(ILOpCode.Ldloc_0, 0), new(ILOpCode.Brtrue_s, 1, 3),
            new(ILOpCode.Ldc_i4_7, 3), new(ILOpCode.Br_s, 4, 1),
            new(ILOpCode.Ldc_i4_8, 6), new(ILOpCode.Stloc_1, 7), new(ILOpCode.Ret, 8),
        ];
        var analysis = new ILValueAnalysis(instructions, new ReflectionCache());
        Assert.Equal(new[] { -1 }, analysis.Inputs[5]);
        Assert.Empty(analysis.Consumers[2]);
        Assert.Empty(analysis.Consumers[4]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BranchSelectedValueSurvivesAFollowingCall(byte flag)
    {
        var cpu = ExecuteProgram("""
            byte result = (byte)((peek(0x6010) == 0 ? 7 : 8) + Next());
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Next() => 9;
            """, cpu => cpu.Memory[0x6010] = flag);
        Assert.Equal(flag == 0 ? 16 : 17, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
