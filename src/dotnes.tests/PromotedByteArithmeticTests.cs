using Xunit.Abstractions;

namespace dotnes.tests;

public class PromotedByteArithmeticTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    public void ByteOperandsRetainFullPromotedResult(string operation)
    {
        using var transpiler = BuildProgram(
            $$"""
            byte left = peek(0x6010), right = peek(0x6011);
            int result = left {{operation}} right;
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low); poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        byte[] bytes = program.ToBytes();
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        byte[] boundaries = [0, 1, 2, 127, 128, 129, 254, 255];
        foreach (byte left in boundaries)
        foreach (byte right in boundaries)
        {
            var cpu = new Cpu6502(bytes, program.BaseAddress, entry);
            cpu.Memory[0x6010] = left;
            cpu.Memory[0x6011] = right;
            cpu.RunUntil(0x7FF0);
            int expected = operation == "+" ? left + right : left - right;
            Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
            Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
            Assert.Equal(0xFD, cpu.SP);
        }
    }

    [Theory]
    [InlineData("1 + value", 254, 255)]
    [InlineData("1 + value", 255, 256)]
    [InlineData("value + 255", 255, 510)]
    [InlineData("value - 8", 0, -8)]
    [InlineData("8 - value", 255, -247)]
    [InlineData("value - 255", 0, -255)]
    [InlineData("value + 256", 255, 511)]
    public void ConstantsPreserveCarryBorrowAndSignedFallback(string expression, byte input, int expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = peek(0x6010);
            int index = {{expression}};
            byte low = (byte)index, high = (byte)(index >> 8);
            poke(0x6000, low); poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = input);
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
    }

    [Theory]
    [InlineData("sbyte", "byte", 255, 255, "+", 254)]
    [InlineData("sbyte", "sbyte", 128, 127, "-", -255)]
    [InlineData("ushort", "byte", 65535, 1, "+", 0)]
    [InlineData("ushort", "byte", 0, 1, "-", 65535)]
    [InlineData("short", "byte", 32767, 1, "+", -32768)]
    [InlineData("short", "byte", -32768, 1, "-", 32767)]
    public void PureSignedAndWordOperandsKeepGenericEmission(string leftType, string rightType,
        int left, int right, string operation, int expected)
    {
        string source =
            $$"""
            State.Left = unchecked(({{leftType}}){{left}});
            State.Right = unchecked(({{rightType}}){{right}});
            {{leftType}} result = ({{leftType}})(State.Left {{operation}} State.Right);
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low); poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class State
            {
                public static {{leftType}} Left;
                public static {{rightType}} Right;
            }
            """;
        // Signed byte results explicitly wrap at their declared storage boundary.
        if (leftType == "sbyte")
            expected = unchecked((sbyte)expected);
        using var transpiler = BuildProgram(source, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        // Generic A:X capture, not a rejection caused by impure call operands.
        Assert.Contains("85-17-86-19", BitConverter.ToString(program.GetMainBlock()));
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, entry);
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ExplicitByteConversionsAndCompoundResultsKeepTheirMeaning()
    {
        var cpu = ExecuteProgram(
            """
            ushort word = 65535;
            byte right = peek(0x6010);
            ushort result = (ushort)(((byte)word + right) << 4);
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low); poke(0x6001, high);
            byte average = (byte)(((byte)word + right) >> 1);
            poke(0x6002, average);
            test_stop(); while (true) ;
            static extern void test_stop();
            """, cpu => cpu.Memory[0x6010] = 255);
        Assert.Equal(new byte[] { 0xE0, 0x1F, 255 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void StagedArrayIndexAndValueKeepLoopAndEvaluationOrder()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[16];
            byte offset = 0;
            int index = 0;
            while (offset < 4)
            {
                index = 8 + offset;
                byte value = (byte)(data[index] + Next());
                data[index] = value;
                offset++;
            }
            offset = 0;
            while (offset < 4)
            {
                index = 8 + offset;
                byte value = data[index];
                poke((ushort)(0x6000 + offset), value);
                offset++;
            }
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Next() { State.Calls++; return State.Calls; }
            static class State { public static byte Calls; }
            """);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void HelpersAndCapturedLoadsRetainLiveValues()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 255;
            ushort first = Sum(255, 1);
            ushort second = Captured();
            byte firstLow = (byte)first, firstHigh = (byte)(first >> 8);
            byte secondLow = (byte)second, secondHigh = (byte)(second >> 8);
            poke(0x6000, firstLow); poke(0x6001, firstHigh);
            poke(0x6002, secondLow); poke(0x6003, secondHigh);
            test_stop(); while (true) ;
            static extern void test_stop();
            static ushort Sum(byte left, byte right) => (ushort)(left + right);
            ushort Captured() => (ushort)(captured + captured);
            """);
        Assert.Equal(new byte[] { 0, 1, 254, 1 }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    public void DefaultCompilationUsesCompactFullWidthArithmetic(string operation)
    {
        string source =
            $$"""
            byte value = peek(0x6010);
            int index = 8 {{operation}} value;
            byte low = (byte)index, high = (byte)(index >> 8);
            poke(0x6000, low); poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            """;
        using var assembly = CompileAssembly(source);
        var program = NesCompiler.Compile(assembly);
        assembly.Position = 0;
        var explicitDefaults = NesCompiler.Compile(assembly, new CompilationOptions());
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        explicitDefaults.DefineExternalLabel("_test_stop", 0x7FF0);
        Assert.Equal(program.GetMainBlock(), explicitDefaults.GetMainBlock());
        var cpu = ExecuteProgram(source, cpu => cpu.Memory[0x6010] = 255);
        int expected = operation == "+" ? 263 : -247;
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8) }, cpu.Memory[0x6000..0x6002]);
        int byteLimit = operation == "+" ? 62 : 133;
        int instructionLimit = operation == "+" ? 23 : 62;
        Assert.True(program.GetMainBlock().Length <= byteLimit,
            $"Expected at most {byteLimit} bytes, got {program.GetMainBlock().Length}.");
        Assert.True(cpu.InstructionCount <= instructionLimit,
            $"Expected at most {instructionLimit} instructions, got {cpu.InstructionCount}.");
        Assert.Equal(0, cpu.SoftwareStackWrites);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void NativeEffectsAndNestedCallsKeepOperandsAndHelperBarrier()
    {
        using var assembly = CompileAssembly(
            """
            byte left = peek(0x6010);
            ushort result = (ushort)(left + Native());
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low); poke(0x6001, high);
            byte nested = Pair(3, Pair(4, Identity(5)));
            poke(0x6002, nested);
            test_stop(); while (true) ;
            static extern byte Native();
            static extern void test_stop();
            static byte Pair(byte a, byte b) => (byte)(a + b);
            static byte Identity(byte value) => value;
            """);
        using var native = new AssemblyReader(new StringReader(
            """
            .segment "CODE"
            _Native:
                lda #17
                sta $17
                sta $18
                sta $19
                sta $1a
                sta $6010
                jsr Identity
                lda #1
                ldx #$a5
                rts
            """));
        var program = NesCompiler.Compile(assembly,
            new CompilationOptions { OptimizeByteHelpers = true }, [native]);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        Assert.Equal(0x20, program.GetMainBlock("Identity")[0]); // Standard stack-parameter prologue.
        Assert.True(program.Labels.TryResolve("main", out ushort entry));
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, entry);
        cpu.Memory[0x6010] = 255;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new byte[] { 0, 1, 12 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(17, cpu.Memory[0x6010]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0, 256)]
    [InlineData(1, 510)]
    public void ConditionalOperandsAndLiveResultsSurviveCalls(byte condition, ushort expected)
    {
        var cpu = ExecuteProgram(
            """
            byte left = peek(0x6010);
            ushort result = (ushort)(left + (peek(0x6011) == 0 ? One() : Max()));
            byte other = One();
            byte low = (byte)result, high = (byte)(result >> 8);
            poke(0x6000, low); poke(0x6001, high); poke(0x6002, other);
            byte calls = State.Calls;
            poke(0x6003, calls);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte One() { State.Calls++; return 1; }
            static byte Max() { State.Calls++; return 255; }
            static class State { public static byte Calls; }
            """, cpu =>
            {
                cpu.Memory[0x6010] = 255;
                cpu.Memory[0x6011] = condition;
            });
        Assert.Equal(new byte[] { (byte)expected, (byte)(expected >> 8), 1, 2 }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }
}
