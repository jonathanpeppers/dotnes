using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ByteArithmeticTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("value++;")]
    [InlineData("++value;")]
    [InlineData("value = (byte)(value + 1);")]
    public void IncrementWrapDoesNotLoadAdjacentByte(string increment)
    {
        var bytes = GetProgramBytes(
            $$"""
            byte value = 255;
            byte frame = 0;
            while (frame < 1)
            {
                {{increment}}
                frame++;
            }
            if (value != 0) poke(0x6000, 1);
            while (true) ;
            """);
        Assert.DoesNotContain("AE2603", Convert.ToHexString(bytes));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(0)]
    [InlineData(16)]
    public void ConstantMinusLocalIsNotDecrement(int constant)
    {
        var bytes = GetProgramBytes(
            $$"""
            byte value = 1;
            byte frame = 0;
            while (frame < 1)
            {
                value = (byte)({{constant}} - value);
                frame++;
            }
            poke(0x6000, value);
            while (true) ;
            """);
        var hex = Convert.ToHexString(bytes);
        Assert.DoesNotContain("CE2503", hex);
        Assert.Contains("38ED2503", hex);
    }

    [Theory]
    [InlineData("value++;", 255, 0)]
    [InlineData("++value;", 255, 0)]
    [InlineData("value = (byte)(value + 1);", 255, 0)]
    [InlineData("value--;", 0, 255)]
    [InlineData("value = (byte)(value + 2);", 255, 1)]
    [InlineData("value = (byte)(value - 2);", 0, 254)]
    [InlineData("value = (byte)(255 - value);", 1, 254)]
    [InlineData("value = (byte)(0 - value);", 1, 255)]
    [InlineData("value = (byte)(16 - value);", 1, 15)]
    public void ByteArithmeticExecutesAndBalancesStacks(string operation, byte initial, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = {{initial}};
            byte frame = 0;
            byte wrong = 0;
            while (frame < 1)
            {
                {{operation}}
                frame++;
            }
            if (value != {{expected}}) wrong = 1;
            poke(0x6000, wrong);
            poke(0x6001, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(0, cpu.Memory[0x6000]);
        Assert.Equal(expected, cpu.Memory[0x6001]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 255)]
    [InlineData(1, 0)]
    [InlineData(254, 255)]
    [InlineData(255, 0)]
    [InlineData(255, 254)]
    [InlineData(255, 255)]
    public void RuntimeComparisonBoundaries(byte left, byte right)
    {
        var cpu = ExecuteProgram(
            """
            byte left = peek(0x6100);
            byte right = peek(0x6101);
            byte greater = 0;
            byte lessOrEqual = 0;
            byte frame = 0;
            while (frame < 2)
            {
                if (left > right) greater = 1;
                if (left <= right) lessOrEqual = 1;
                frame++;
            }
            poke(0x6000, greater);
            poke(0x6001, lessOrEqual);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            cpu =>
            {
                cpu.Memory[0x6100] = left;
                cpu.Memory[0x6101] = right;
            });
        Assert.Equal(left > right ? 1 : 0, cpu.Memory[0x6000]);
        Assert.Equal(left <= right ? 1 : 0, cpu.Memory[0x6001]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(255)]
    public void VariableShiftsExecuteWithCountMasking(byte count)
    {
        var cpu = ExecuteProgram(
            """
            byte count = peek(0x6100);
            byte frame = 0;
            while (frame < 3)
            {
                byte left = (byte)(1 << count);
                poke(0x6000, left);
                byte right = (byte)(0x80 >> count);
                poke(0x6001, right);
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            cpu => cpu.Memory[0x6100] = count);
        Assert.Equal(unchecked((byte)(1 << count)), cpu.Memory[0x6000]);
        Assert.Equal((byte)(0x80 >> count), cpu.Memory[0x6001]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void BranchUsesReturnedByteNotCleanupFlags(byte value)
    {
        var cpu = ExecuteProgram(
            """
            byte value = peek(0x6100);
            if (Identity(value) != 0) poke(0x6000, 1);
            if (Identity(value) == 0) poke(0x6001, 1);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Identity(byte value) => value;
            """,
            cpu => cpu.Memory[0x6100] = value);
        Assert.Equal(value != 0 ? 1 : 0, cpu.Memory[0x6000]);
        Assert.Equal(value == 0 ? 1 : 0, cpu.Memory[0x6001]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData("checked((byte)(value + 1))")]
    [InlineData("checked((byte)(255 - value))")]
    [InlineData("checked((byte)(1 << value))")]
    public void CheckedByteOperationsRemainUnsupported(string expression)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            byte value = peek(0x6100);
            byte result = {{expression}};
            poke(0x6000, result);
            while (true) ;
            """));
        Assert.Contains("opcode", error.Message);
    }

    static void AssertBalanced(Cpu6502 cpu)
    {
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void ConstantMinusStaticField(byte initial)
    {
        var cpu = ExecuteProgram(
            """
            State.Value = peek(0x6100);
            byte value = (byte)(16 - State.Value);
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static byte Value; }
            """,
            cpu => cpu.Memory[0x6100] = initial);
        Assert.Equal(unchecked((byte)(16 - initial)), cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 1)]
    [InlineData(255, 7)]
    [InlineData(255, 8)]
    [InlineData(255, 31)]
    [InlineData(255, 32)]
    [InlineData(255, 255)]
    public void VariableShiftParameters(byte value, byte count)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte left = Left({{value}}, {{count}});
            poke(0x6000, left);
            byte right = Right({{value}}, {{count}});
            poke(0x6001, right);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Left(byte value, byte count) => (byte)(value << count);
            static byte Right(byte value, byte count) => (byte)(value >> count);
            """);
        Assert.Equal(unchecked((byte)(value << count)), cpu.Memory[0x6000]);
        Assert.Equal((byte)(value >> count), cpu.Memory[0x6001]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(254)]
    [InlineData(255)]
    public void ClimberByteBorrowComparison(byte oldValue)
    {
        var cpu = ExecuteProgram(
            """
            byte[] actor = new byte[1];
            byte oldValue = peek(0x6100);
            byte borrowed = 0;
            for (byte i = 0; i < 1; i++)
            {
                if (actor[i] > oldValue) borrowed = 1;
            }
            poke(0x6000, borrowed);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            cpu =>
            {
                cpu.Memory[0x6100] = oldValue;
                cpu.Memory[0x325] = unchecked((byte)(oldValue - 1));
            });
        Assert.Equal(oldValue == 0 ? 1 : 0, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(255, false)]
    [InlineData(0, true)]
    [InlineData(255, true)]
    public void EarlyReturnFlagsWithShortAndLongBranches(byte input, bool longBranch)
    {
        string padding = longBranch ? string.Concat(Enumerable.Repeat("State.Result = 7;", 60)) : "";
        var cpu = ExecuteProgram(
            $$"""
            State.Input = peek(0x6100);
            if (Predicate(State.Input) != 0)
            {
                {{padding}}
                State.Result = 7;
            }
            else State.Result = 9;
            test_stop();
            while (true) ;
            static extern void test_stop();
            static byte Predicate(byte value)
            {
                if (value < 128) return 0;
                return value;
            }
            static class State { public static byte Input; public static byte Result; }
            """,
            cpu => cpu.Memory[0x6100] = input);
        Assert.Equal(input < 128 ? 9 : 7, cpu.Memory[0x326]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    public void VariableRightShiftKeepsPromotedHighByte(byte count)
    {
        var cpu = ExecuteProgram(
            """
            State.Count = peek(0x6100);
            byte value = (byte)(0x8100 >> State.Count);
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static byte Count; }
            """,
            cpu => cpu.Memory[0x6100] = count);
        Assert.Equal(unchecked((byte)(0x8100 >> count)), cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(255)]
    [InlineData(-1)]
    public void ConstantShiftCountsUseCSharpMask(int count)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte value = peek(0x6100);
            byte frame = 0;
            while (frame < 1)
            {
                byte result = (byte)(value >> {{count}});
                poke(0x6000, result);
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            cpu => cpu.Memory[0x6100] = 128);
        Assert.Equal((byte)(128 >> count), cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(257)]
    [InlineData(65535)]
    public void WordLocalShiftCountUsesItsLowFiveBits(ushort count)
    {
        var cpu = ExecuteProgram(
            $$"""
            State.Count = {{count}};
            ushort count = State.Count;
            byte value = 128;
            byte frame = 0;
            while (frame < 1)
            {
                byte result = (byte)(value >> count);
                poke(0x6000, result);
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static ushort Count; }
            """);
        Assert.Equal((byte)(128 >> count), cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Fact]
    public void UntruncatedPromotedShiftOperandIsRejected()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes(
            """
            State.Value = 65535;
            State.Count = 9;
            byte value = (byte)((State.Value + 1) >> State.Count);
            poke(0x6000, value);
            while (true) ;
            static class State { public static ushort Value; public static ushort Count; }
            """));
        Assert.Contains("needs a promoted result wider", error.Message);
    }

    [Theory]
    [InlineData(65536, 16)]
    [InlineData(0x1000000, 24)]
    public void VariableShiftOfWideConstantIsRejected(int value, byte count)
    {
        Assert.Equal(1, (byte)(value >> count));
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            State.Count = {{count}};
            byte result = (byte)({{value}} >> State.Count);
            poke(0x6000, result);
            while (true) ;
            static class State { public static byte Count; }
            """));
        Assert.Contains("exceeds the maximum supported value", error.Message);
    }

    [Theory]
    [InlineData("State.Value + 1", 65535, 9, 0, false)]
    [InlineData("State.Value + 1", 0x8100, 9, 64, false)]
    [InlineData("State.Value - 1", 256, 1, 127, false)]
    [InlineData("State.Value - 1", 0, 8, 255, false)]
    [InlineData("State.Value + 256", 255, 8, 1, false)]
    [InlineData("State.Value - 256", 1, 8, 255, false)]
    [InlineData("State.Value + 65535", 1, 8, 0, false)]
    [InlineData("State.Value - 65535", 0, 0, 1, false)]
    [InlineData("State.Value + 1", 65535, 9, 0, true)]
    [InlineData("State.Value + 1", 0x8100, 9, 64, true)]
    [InlineData("State.Value - 1", 256, 1, 127, true)]
    [InlineData("State.Value - 1", 0, 8, 255, true)]
    public void ExplicitlyTruncatedShiftOperandRemainsSupported(string expression, ushort input, byte count, byte expected, bool materialize)
    {
        string value = materialize ? "State.Operand" : $"(ushort)({expression})";
        string declaration = materialize ? $"State.Operand = (ushort)({expression});" : "";
        var cpu = ExecuteProgram(
            $$"""
            State.Value = {{input}};
            State.Count = {{count}};
            {{declaration}}
            byte result = (byte)(({{value}}) >> State.Count);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static ushort Value; public static ushort Count; public static ushort Operand; }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData("State.Value + 1", 255, 1, 128)]
    [InlineData("0 - State.Value", 1, 8, 255)]
    [InlineData("(State.Value + 1) + 256", 255, 1, 0)]
    [InlineData("State.Value - 256", 1, 8, 255)]
    public void ByteArithmeticCastToUshortBeforeVariableShiftIsRejected(string expression, byte value, byte count, byte expected)
    {
        int promoted = expression switch
        {
            "State.Value + 1" => value + 1,
            "0 - State.Value" => 0 - value,
            "(State.Value + 1) + 256" => (value + 1) + 256,
            "State.Value - 256" => value - 256,
            _ => throw new ArgumentException("Unexpected test expression.", nameof(expression)),
        };
        Assert.Equal(expected, unchecked((byte)((ushort)promoted >> count)));
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes(
            $$"""
            State.Value = {{value}};
            State.Count = {{count}};
            byte result = (byte)(((ushort)({{expression}})) >> State.Count);
            poke(0x6000, result);
            while (true) ;
            static class State { public static byte Value; public static byte Count; }
            """));
        Assert.Contains("promoted arithmetic expressions", error.Message);
    }

    [Fact]
    public void ByteTruncationBeforeUshortCastAndVariableShiftRemainsSupported()
    {
        var cpu = ExecuteProgram(
            """
            State.Value = 255;
            State.Count = 1;
            byte result = (byte)(((ushort)(byte)(State.Value + 1)) >> State.Count);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static byte Value; public static byte Count; }
            """);
        Assert.Equal(0, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData("value", 0x8100, 1, 128)]
    [InlineData("0x8100", 0x8100, 1, 128)]
    [InlineData("value", 65535, 8, 255)]
    [InlineData("65535", 65535, 8, 255)]
    public void UshortLocalAndConstantCastsBeforeVariableShiftRemainSupported(string expression, ushort input, byte count, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            State.Value = {{input}};
            State.Count = {{count}};
            ushort value = State.Value;
            byte result = (byte)(((ushort){{expression}}) >> State.Count);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static ushort Value; public static byte Count; }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 64)]
    public void VerifiedWordLocalShiftPreservesBranchSelection(byte select, byte expected)
    {
        var cpu = ExecuteProgram(
            """
            State.Value = 65535;
            State.Other = 0x8100;
            State.Count = 9;
            State.Select = peek(0x6100);
            ushort value = State.Value;
            if (State.Select != 0)
                value = State.Other;
            byte result = (byte)(((ushort)(value + 1)) >> State.Count);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static ushort Value;
                public static ushort Other;
                public static byte Count;
                public static byte Select;
            }
            """,
            cpu => cpu.Memory[0x6100] = select);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void VariableShiftPreservesArithmeticFromAlternativeBranch(bool reverse, bool add)
    {
        string choice = reverse
            ? "State.Select == 0 ? State.WordValue : State.ByteValue + 1"
            : "State.Select == 0 ? State.ByteValue + 1 : State.WordValue";
        string value = add ? $"(ushort)(({choice}) + 1)" : $"({choice})";
        for (byte select = 0; select < 2; select++)
        {
            var cpu = ExecuteProgram(
                $$"""
                State.ByteValue = 255;
                State.WordValue = 65535;
                State.Count = 8;
                State.Select = {{select}};
                byte result = (byte)(({{value}}) >> State.Count);
                poke(0x6000, result);
                test_stop();
                while (true) ;
                static extern void test_stop();
                static class State
                {
                    public static byte ByteValue;
                    public static ushort WordValue;
                    public static byte Count;
                    public static byte Select;
                }
                """);
            int selected = (select == 0) == reverse ? 65535 : 256;
            int expected = (add ? unchecked((ushort)(selected + 1)) : selected) >> 8;
            Assert.Equal((byte)expected, cpu.Memory[0x6000]);
            AssertBalanced(cpu);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 64)]
    public void InterleavedAliasShiftRemainsAnEvaluationStackDependency(byte select, byte expected)
    {
        ushort value = select == 0 ? ushort.MaxValue : (ushort)0x8100;
        Assert.Equal(expected, unchecked((byte)((ushort)(value + 1) >> 9)));
        var cpu = ExecuteProgram(
            $$"""
            State.Value = 65535;
            State.Other = 0x8100;
            State.Count = 9;
            State.Select = {{select}};
            ushort value = State.Value;
            if (State.Select != 0)
                value = State.Other;
            ushort alias = value;
            State.Value = 0;
            State.Other = 0;
            byte result = (byte)(((ushort)(alias + 1)) >> State.Count);
            poke(0x6000, result);
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State
            {
                public static ushort Value;
                public static ushort Other;
                public static byte Count;
                public static byte Select;
            }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }

    [Theory]
    [InlineData(0x8100, 256, true, 8, 130)]
    [InlineData(0x8100, 256, false, 8, 128)]
    [InlineData(1, 65535, true, 8, 0)]
    [InlineData(0, 65535, false, 0, 1)]
    [InlineData(65535, 1, true, 9, 0)]
    [InlineData(0, 1, false, 8, 255)]
    [InlineData(0x7FFF, 256, true, 8, 128)]
    [InlineData(0x8000, 256, false, 8, 127)]
    public void WordLocalArithmeticPreservesOperandForWideConstants(ushort input, ushort constant, bool add, byte count, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            State.Value = {{input}};
            State.Count = {{count}};
            ushort value = State.Value;
            byte frame = 0;
            while (frame < 1)
            {
                byte result = (byte)(((ushort)(value {{(add ? "+" : "-")}} {{constant}})) >> State.Count);
                poke(0x6000, result);
                frame++;
            }
            test_stop();
            while (true) ;
            static extern void test_stop();
            static class State { public static ushort Value; public static byte Count; }
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        AssertBalanced(cpu);
    }
}
