using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class IntegerSemanticsTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(-128, 1)]
    [InlineData(-1, 8)]
    [InlineData(-64, 31)]
    [InlineData(-64, 32)]
    [InlineData(127, 1)]
    public void SignedConstantShiftKeepsSign(int input, int count)
    {
        var cpu = ExecuteProgram($$"""
            byte value = Shift({{input}});
            poke(0x6000, value);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Shift(sbyte value) => (byte)(value >> {{count}});
            """);
        Assert.Equal((byte)(input >> count), cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void FinalByteCastDoesNotPermitTruncatingSeventeenBitSumBeforeShift()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            ushort value = rand16();
            byte middle = (byte)((value + value) >> 1);
            poke(0x6000, middle);
            while (true);
            """));
        Assert.Contains("promoted result wider", error.Message);
        Assert.Contains("lost carry/sign bit", error.Message);
    }

    [Fact]
    public void SignedRuntimeShiftHasExplicitDiagnostic()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            sbyte value = (sbyte)peek(0x6010);
            poke(0x6020, (byte)value);
            byte count = peek(0x6011);
            poke(0x6000, (byte)(value >> count));
            while (true);
            """));
        Assert.Contains("Runtime signed shift counts", error.Message);
    }

    [Theory]
    [InlineData("byte", "ushort", 255, 255)]
    [InlineData("sbyte", "short", -1, 65535)]
    public void WordReturnExtendsByteArgument(string argumentType, string returnType, int input, int expected)
    {
        var cpu = ExecuteProgram($$"""
            {{returnType}} value = Extend({{input}});
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static {{returnType}} Extend({{argumentType}} value) => value;
            """);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void UnsupportedWordArgumentHasCallingConventionDiagnostic()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            poke(0x6000, Read(300));
            while (true);
            static byte Read(ushort value) => (byte)value;
            """));
        Assert.Contains("Parameter 0", error.Message);
        Assert.Contains("byte and sbyte only", error.Message);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(253, 258)]
    public void BoundedIntCounterKeepsIndexSemantics(int start, int end)
    {
        var cpu = ExecuteProgram($$"""
            for (int index = {{start}}; index < {{end}}; index++)
                poke(0x6000, (byte)index);
            test_stop(); while (true);
            static extern void test_stop();
            """);
        Assert.Equal((byte)(end - 1), cpu.Memory[0x6000]);
    }

    [Theory]
    [InlineData(1, -2)]
    [InlineData(0, -3)]
    [InlineData(255, 252)]
    public void ProvenIntSubtractionPreservesSign(int input, int expected)
    {
        var cpu = ExecuteProgram("""
            byte input = peek(0x6010);
            int value = input - 3;
            byte selected = 42;
            if (value < 0) selected = 76;
            poke(0x6000, selected);
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6001, low);
            poke(0x6002, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal(expected < 0 ? 76 : 42, cpu.Memory[0x6000]);
        Assert.Equal((byte)expected, cpu.Memory[0x6001]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6002]);
    }

    [Theory]
    [InlineData("sbyte", "short", 255, 65535)]
    [InlineData("sbyte", "ushort", 128, 65408)]
    [InlineData("byte", "short", 255, 255)]
    [InlineData("byte", "ushort", 255, 255)]
    public void WideningConversionsExtendTheDeclaredSign(string sourceType, string targetType, int input, int expected)
    {
        var cpu = ExecuteProgram($$"""
            {{sourceType}} input = ({{sourceType}})peek(0x6010);
            {{targetType}} value = ({{targetType}})input;
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
    }

    [Theory]
    [InlineData("ushort")]
    [InlineData("short")]
    [InlineData("int")]
    public void RuntimeAccumulatorPreservesCarryAndBorrow(string type)
    {
        string source = $$"""
            poke(0x60E0, 250);
            {{type}} value = 0;
            byte iteration = 0;
            while (iteration < 1)
            {
                value = ({{type}})(value + peek(0x60E0));
                value = ({{type}})(value + 20);
                byte low = (byte)value;
                byte high = (byte)(value >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                value = ({{type}})(value - 30);
                low = (byte)value;
                high = (byte)(value >> 8);
                poke(0x6002, low);
                poke(0x6003, high);
                iteration = (byte)(iteration + 1);
            }
            test_stop(); while (true);
            static extern void test_stop();
            """;
        if (type == "int")
        {
            var error = Assert.Throws<TranspileException>(() => GetProgramBytes(source));
            Assert.Contains("Int32 local", error.Message);
            Assert.Contains("Full 32-bit local arithmetic is not supported", error.Message);
            return;
        }
        var cpu = ExecuteProgram(source);
        Assert.Equal(new byte[] { 14, 1, 240, 0 }, cpu.Memory[0x6000..0x6004]);
    }

    [Fact]
    public void UnboundedIntSubtractionRequiresExplicitSupportedStorage()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            poke(0x60E0, 1);
            int value = 0;
            byte iteration = 0;
            byte selected = 42;
            while (iteration < 1)
            {
                value = value + peek(0x60E0);
                value = value - 3;
                if (value < 0) selected = 76;
                iteration = (byte)(iteration + 1);
            }
            poke(0x6000, selected);
            byte low = (byte)value;
            poke(0x6001, low);
            test_stop(); while (true);
            static extern void test_stop();
            """));
        Assert.Contains("Int32 local", error.Message);
        Assert.Contains("IL_", error.Message);
        Assert.Contains("truncation semantics", error.Message);
    }

    [Fact]
    public void OneUseUshortOperandsPreserveValues()
    {
        var cpu = ExecuteProgram("""
            poke(0x60E0, 250);
            poke(0x60E1, 20);
            ushort sum = 0;
            byte iteration = 0;
            while (iteration < 2)
            {
                byte left = peek(0x60E0);
                byte right = peek(0x60E1);
                sum = left;
                sum = (ushort)(sum + right);
                byte low = (byte)sum;
                byte high = (byte)(sum >> 8);
                poke(0x6000, low);
                poke(0x6001, high);
                iteration = (byte)(iteration + 1);
            }
            test_stop(); while (true);
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 14, 1 }, cpu.Memory[0x6000..0x6002]);
    }

    [Fact]
    public void DeclaredNumericTypesSurviveMetadataParsing()
    {
        using var transpiler = BuildProgram("""
            ushort word = 0;
            short signedWord = 0;
            sbyte signedByte = 0;
            int counter = 0;
            while (true)
            {
                word = rand16();
                signedWord = (short)word;
                signedByte = (sbyte)signedWord;
                counter = peek(0x6010);
                byte result = (byte)(word + signedWord + signedByte + counter);
                poke(0x6030, 42);
                poke(0x6000, result);
            }
            static ushort Identity(byte value) => value;
            static extern short ReadSignedWord();
            """, out _);
        var main = transpiler.NumericTypes["main"];
        Assert.Contains(PrimitiveTypeCode.UInt16, main.Locals);
        Assert.Contains(PrimitiveTypeCode.Int16, main.Locals);
        Assert.Contains(PrimitiveTypeCode.SByte, main.Locals);
        Assert.Contains(PrimitiveTypeCode.Int32, main.Locals);
        Assert.Equal(PrimitiveTypeCode.UInt16, transpiler.NumericTypes["Identity"].ReturnType);
        Assert.Equal(PrimitiveTypeCode.Byte, Assert.Single(transpiler.NumericTypes["Identity"].Parameters));
        Assert.Equal(PrimitiveTypeCode.Int16, transpiler.NumericTypes["ReadSignedWord"].ReturnType);
    }
}
