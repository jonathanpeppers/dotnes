using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ArrayReviewTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void ComputedReadCanForwardACapturedCallersContext()
    {
        var cpu = ExecuteProgram(
            """
            byte captured = 7;
            Outer(3);
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Outer(byte index)
            {
                byte c = captured;
                poke(0x6001, c);
                byte[] data = new byte[8];
                data[2] = 44;
                Consume(data[(byte)(index - 1)]);
            }
            void Consume(byte value)
            {
                byte c = captured;
                poke(0x6002, c);
                byte v = value;
                poke(0x6000, v);
            }
            """);
        Assert.Equal(new byte[] { 44, 7, 7 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputedReadCanBePassedToCapturedScalarHelper(bool earlierArgument)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte captured = 7;
            byte[] data = new byte[8];
            data[2] = 44;
            byte index = 3;
            byte frame = 0;
            while (frame < 2)
            {
                Consume({{(earlierArgument ? "88, " : "")}}data[(byte)(index - 1)]);
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            void Consume({{(earlierArgument ? "byte first, " : "")}}byte value)
            {
                byte c = captured;
                poke(0x6001, c);
                byte v = value;
                poke(0x6000, v);
                {{(earlierArgument ? "byte a = first; poke(0x6002, a);" : "")}}
            }
            """);
        Assert.Equal(new byte[] { 44, 7, (byte)(earlierArgument ? 88 : 0) }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("data[index] += (byte)(value + 1);", 33, 11, 3)]
    [InlineData("data[index] += value++;", 32, 12, 3)]
    [InlineData("data[index] += (byte)(value ^ y);", 50, 11, 3)]
    [InlineData("data[index++]++;", 22, 11, 4)]
    public void CompoundUpdatesPreserveCompleteEvaluation(string assignment, byte expected, byte value, byte index)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 21;
            data[4] = 99;
            byte index = 3;
            byte value = 11;
            byte y = 22;
            byte frame = 0;
            while (frame < 1)
            {
                {{assignment}}
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            poke(0x6002, value);
            poke(0x6003, index);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { expected, 99, value, index }, cpu.Memory[0x6000..0x6004]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("index - 1", 44)]
    [InlineData("index ^ 1", 44)]
    [InlineData("index >> 1", 33)]
    [InlineData("index++ - 1", 44)]
    public void ComputedReadsUseTheComputedIndex(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[1] = 33;
            data[2] = 44;
            byte index = 3;
            byte result = 0;
            byte frame = 0;
            while (frame < 1)
            {
                result = data[(byte)({{expression}})];
                frame++;
            }
            poke(0x6000, result);
            poke(0x6001, index);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(expression.Contains("++") ? 4 : 3, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("40 + data[(byte)(index >> 1)]", 73)]
    [InlineData("(byte)(x - y) + data[(byte)((x >> 3) + ((y >> 3) << 4))]", 60)]
    [InlineData("40 + (data[(byte)(index >> 1)] >> 1)", 56)]
    public void ComputedReadKeepsAnOlderLiveOperand(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[32];
            data[1] = 33;
            data[19] = 44;
            byte index = 3;
            byte x = 26;
            byte y = 10;
            byte result = 0;
            byte frame = 0;
            while (frame < 1)
            {
                result = (byte)({{expression}});
                frame++;
            }
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ConditionalCompoundValueIsDiagnosed()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            byte[] data = new byte[8];
            byte index = 3;
            byte flag = (byte)pad_poll(0);
            data[index] += (byte)(flag == 0 ? 11 : 22);
            while (true) ;
            """));
        Assert.Contains("unsupported control flow", exception.Message);
    }

    [Theory]
    [InlineData("data[index]", 21)]
    [InlineData("table[index]", 44)]
    public void DirectArrayCopyRetainsTheReadValue(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            byte[] table = new byte[] { 1, 2, 3, 44 };
            data[3] = 21;
            byte index = 3;
            byte frame = 0;
            while (frame < 1)
            {
                data[index] = {{expression}};
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("x ^ y", 29, 0)]
    [InlineData("State.Value() ^ 3", 8, 1)]
    [InlineData("State.Value() << 1", 22, 1)]
    [InlineData("State.Value() >> 1", 5, 1)]
    [InlineData("2 + (byte)(State.Value() % 6)", 7, 1)]
    public void DynamicIndexStoresKeepAllValueOperators(string expression, byte expected, byte calls)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[4] = 99;
            byte index = 3;
            byte x = 11;
            byte y = 22;
            byte frame = 0;
            while (frame < 1)
            {
                data[index] = (byte)({{expression}});
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            result = State.Calls;
            poke(0x6002, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Calls;
                public static byte Value() { Calls++; return 11; }
            }
            """);
        Assert.Equal(new byte[] { expected, 99, calls }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ArrayUpdatePreservesDynamicHighBits()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 3;
            data[4] = 99;
            byte index = 3;
            byte notify = 2;
            byte frame = 0;
            while (frame < 1)
            {
                data[index] = (byte)((data[index] & 15) | (byte)(notify << 4));
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 35, 99 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ArrayStoreKeepsModuloAndConstantLeftOperand()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            byte index = 3;
            byte frame = 0;
            while (frame < 1)
            {
                data[index] = (byte)(4 + (byte)(State.Value() % 20));
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = State.Calls;
            poke(0x6001, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Calls;
                public static byte Value() { Calls++; return 41; }
            }
            """);
        Assert.Equal(new byte[] { 5, 1 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void SameArrayDifferentIndexUpdateReadsItsSource()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 21;
            data[4] = 75;
            byte target = 3;
            byte source = 4;
            byte frame = 0;
            while (frame < 1)
            {
                data[target] = (byte)(data[source] + 1);
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 76, 75 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("data[index++] = 77;", 77, 88, 4, 11)]
    [InlineData("data[index] = value++;", 11, 12, 3, 12)]
    [InlineData("data[index] = (byte)(value++ + 1);", 12, 12, 3, 12)]
    [InlineData("data[index] = index++;", 3, 88, 4, 11)]
    [InlineData("data[index++] = value++;", 11, 88, 4, 12)]
    public void PostfixMutationsHappenOnceInOrder(string assignment, byte first, byte second, byte index, byte value)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[5] = 99;
            byte index = 3;
            byte value = 11;
            byte frame = 0;
            while (frame < 1)
            {
                {{assignment}}
                frame++;
            }
            {{(assignment.Contains("index++") ? "data[index] = 88;" : "data[4] = value;")}}
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            result = data[5];
            poke(0x6002, result);
            poke(0x6003, index);
            poke(0x6004, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { first, second, 99, index, value }, cpu.Memory[0x6000..0x6005]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void PostfixIndexInsideArithmeticKeepsItsMutation()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 99;
            byte index = 3;
            byte frame = 0;
            while (frame < 1)
            {
                data[(byte)(index++ + 1)] = 77;
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = data[4];
            poke(0x6001, result);
            poke(0x6002, index);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(new byte[] { 99, 77, 4 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void CapturedArrayHelperIsDiagnosedBeforeLowering()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            byte captured = 43;
            byte[] data = new byte[8];
            Set(data);
            while (true) ;
            void Set(byte[] data) { data[3] = captured; }
            """));
        Assert.Contains("scalar parameters", exception.Message);
    }

    [Fact]
    public void SignedByteArgumentKeepsItsBitRepresentation()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            Set(data, -1);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Set(byte[] data, sbyte value)
            {
                data[3] = (byte)value;
                byte result = data[3];
                poke(0x6000, result);
            }
            """);
        Assert.Equal(255, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("3", "x + y", 0)]
    [InlineData("index", "State.First() + State.Second()", 2)]
    [InlineData("3", "State.First() + State.Second()", 2)]
    public void StoreKeepsTheCompleteValueExpression(string index, string value, byte calls)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            byte x = 11;
            byte y = 22;
            byte index = 3;
            byte frame = 0;
            while (frame < 1)
            {
                data[{{index}}] = (byte)({{value}});
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            result = State.Calls;
            poke(0x6001, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static byte Calls;
                public static byte First() { Calls++; return 11; }
                public static byte Second() { Calls++; return 22; }
            }
            """);
        Assert.Equal(33, cpu.Memory[0x6000]);
        Assert.Equal(calls, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void OptimizedAllocationKeepsItsIdentity()
    {
        var cpu = ExecuteProgram(
            """
            byte[] actors = new byte[8];
            actors[3] = 75;
            Update(actors, 3);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Update(byte[] actors, byte index)
            {
                actors[index]++;
                byte value = actors[index];
                poke(0x6000, value);
            }
            """);
        Assert.Equal(76, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ConditionalArrayAliasIsDiagnosed()
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            while (true) ;
            static void Set(byte[] a, byte[] b, byte flag)
            {
                byte[] alias = a;
                if (flag != 0)
                    alias = b;
                alias[3] = 77;
            }
            """));
        Assert.Contains("array alias", exception.Message);
    }

    [Theory]
    [InlineData("ram, table")]
    [InlineData("table, ram")]
    public void MixedRamAndRomArgumentsAreDiagnosed(string arrays)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] ram = new byte[8];
            ram[3] = 21;
            byte[] table = new byte[] { 1, 2, 3, 44 };
            byte frame = 0;
            while (frame < 1)
            {
                Copy({{arrays}}, 3);
                frame++;
            }
            while (true) ;
            static void Copy(byte[] target, byte[] source, byte index)
            {
                target[index] = source[index];
            }
            """));
        Assert.Contains("RAM and read-only ROM", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IdenticalAliasesAcrossBranchesKeepTheirIdentity(byte flag)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] data = new byte[8];
            data[3] = 21;
            Set(data, {{flag}});
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Set(byte[] data, byte flag)
            {
                byte[] alias = data;
                if (flag != 0)
                    alias = data;
                alias[3]++;
                byte result = data[3];
                poke(0x6000, result);
            }
            """);
        Assert.Equal(22, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ExistingRomOnlyHelperIsNotRejected()
    {
        var bytes = GetProgramBytes(
            """
            byte[] colors = new byte[] { 15, 1, 17, 33 };
            Setup(colors);
            while (true) ;
            static void Setup(byte[] colors) { pal_bg(colors); }
            """);
        Assert.NotEmpty(bytes);
    }

    [Theory]
    [InlineData("ushort")]
    [InlineData("int")]
    public void UnsupportedScalarWidthsInArrayHelpersAreDiagnosed(string type)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] data = new byte[8];
            Set(data, 0x1234);
            while (true) ;
            static void Set(byte[] data, {{type}} value)
            {
                data[3] = (byte)(value >> 8);
            }
            """));
        Assert.Contains("scalar", exception.Message);
    }

    [Theory]
    [InlineData("3", "flag == 0 ? (byte)11 : (byte)22")]
    [InlineData("index", "(byte)((flag == 0 ? 11 : 22) + 5)")]
    public void ConditionalStoreValueIsDiagnosed(string index, string value)
    {
        var exception = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte[] data = new byte[8];
            byte flag = (byte)pad_poll(0);
            byte index = 3;
            data[{{index}}] = {{value}};
            while (true) ;
            """));
        Assert.Contains("unsupported control flow", exception.Message);
    }

    [Fact]
    public void ScalarCallBeforeArrayAccessKeepsArgumentFrame()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[8];
            data[3] = 21;
            byte frame = 0;
            while (frame < 2)
            {
                Set(data, 3);
                frame++;
            }
            byte result = data[3];
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static void Set(byte[] data, byte index)
            {
                Ignore(index, index);
                data[index] = 77;
            }
            static void Ignore(byte x, byte y) { }
            """);
        Assert.Equal(77, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
