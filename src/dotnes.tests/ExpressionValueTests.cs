using Xunit.Abstractions;

namespace dotnes.tests;

public class ExpressionValueTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void DerivedStorePreservesRetainedOriginal()
    {
        var cpu = ExecuteProgram(
            """
            byte value = Helpers.Next();
            byte derived = (byte)(value + 1);
            byte result = (byte)(value + derived);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers { public static byte Next() => 15; }
            """);
        Assert.Equal(31, cpu.Memory[0x6000]);
    }

    [Fact]
    public void VoidConsumerPreservesRetainedOriginal()
    {
        var cpu = ExecuteProgram(
            """
            byte value = Helpers.Next();
            Helpers.Ignore(value);
            poke(0x6000, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte Next() => 37;
                public static void Ignore(byte value) { }
            }
            """);
        Assert.Equal(37, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ForwardedArgumentsRestoreCallerParameterOffsets()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Helpers.Set(37);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                static void Ignore(byte first, byte second) { }
                public static byte Set(byte value)
                {
                    Ignore(value, value);
                    return value;
                }
            }
            """);
        Assert.Equal(37, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0, 42)]
    [InlineData(1, 76)]
    public void DuplicatedValueSurvivesConditional(byte condition, byte expected)
    {
        var cpu = ExecuteProgram(
            """
            byte value = Helpers.Next();
            byte selected = 42;
            if (peek(0x6010) != 0)
                selected = 76;
            poke(0x6000, selected);
            poke(0x6001, value);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte Next() => 93;
            }
            """, initialize: cpu => cpu.Memory[0x6010] = condition);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(93, cpu.Memory[0x6001]);
    }

    [Fact]
    public void WordValueSurvivesByteReturningCall()
    {
        var cpu = ExecuteProgram(
            """
            ushort left = 0x1234;
            byte iteration = 0;
            while (iteration < 1)
            {
                left = (ushort)(left + Helpers.Next());
                iteration++;
            }
            byte low = (byte)left;
            poke(0x6000, low);
            byte high = (byte)(left >> 8);
            poke(0x6001, high);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte Next() => 7;
            }
            """);
        Assert.Equal(new byte[] { 0x3B, 0x12 }, cpu.Memory[0x6000..0x6002]);
    }

    [Theory]
    [InlineData("left + right", 114)]
    [InlineData("right + left", 114)]
    [InlineData("left - right", 88)]
    [InlineData("left & right", 5)]
    [InlineData("left | right", 109)]
    [InlineData("left ^ right", 104)]
    public void RuntimeOperands(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] state = new byte[4];
            state[1] = 101;
            state[2] = 13;
            byte left = 0;
            byte right = 0;
            byte value = 0;
            for (byte frame = 0; frame < 2; frame++)
            {
                left = state[1];
                right = state[2];
                value = (byte)({{expression}});
            }
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("left + (right << 1)", 127)]
    [InlineData("(right << 1) + left", 127)]
    [InlineData("(left - right) + (right << 1)", 114)]
    public void NestedOperands(string expression, byte expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte[] state = new byte[4];
            state[1] = 101;
            state[2] = 13;
            byte left = 0;
            byte right = 0;
            byte value = 0;
            for (byte frame = 0; frame < 2; frame++)
            {
                left = state[1];
                right = state[2];
                value = (byte)({{expression}});
            }
            poke(0x6000, value);
            test_stop();
            while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void LiveValueAcrossCall()
    {
        var cpu = ExecuteProgram(
            """
            byte first = Helpers.Next();
            byte second = Helpers.Next();
            byte third = Helpers.Next();
            byte sum = (byte)(first + second + third);
            poke(0x6000, sum);
            test_stop();
            while (true) ;
            static extern void test_stop();

            static class Helpers
            {
                static byte count;
                public static byte Next()
                {
                    count++;
                    return count;
                }
            }
            """);
        Assert.Equal(6, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("+", 202 + 87)]
    [InlineData("-", 202 - 87)]
    [InlineData("&", 202 & 87)]
    [InlineData("|", 202 | 87)]
    [InlineData("^", 202 ^ 87)]
    public void OneUseConstantsAcrossInitializer(string operation, int expected)
    {
        var cpu = ExecuteProgram(
            $$"""
            byte first = 202;
            byte second = 87;
            byte result = (byte)(first {{operation}} second);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal(unchecked((byte)expected), cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void LocalAndParameterHaveDistinctValues()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Sum(3);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Sum(byte value)
            {
                byte total = 0;
                for (byte i = 0; i < 3; i++)
                    total = (byte)(total + value);
                return total;
            }
            """);
        Assert.Equal(9, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void CallArgumentsEvaluateOnceInOrder()
    {
        var cpu = ExecuteProgram(
            """
            byte result = Helpers.Subtract(Helpers.Next(), Helpers.Next());
            poke(0x6000, result);
            byte count = Helpers.Count;
            poke(0x6001, count);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte Count;
                public static byte Next()
                {
                    Count++;
                    return Count;
                }
                public static byte Subtract(byte left, byte right) => (byte)(left - right);
            }
            """);
        Assert.Equal(255, cpu.Memory[0x6000]);
        Assert.Equal(2, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void OriginalStaticReadPrecedesMutatingCall()
    {
        var cpu = ExecuteProgram(
            """
            Helpers.Value = 40;
            byte result = (byte)(Helpers.Value - Helpers.Change());
            poke(0x6000, result);
            byte changed = Helpers.Value;
            poke(0x6001, changed);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte Value;
                public static byte Change()
                {
                    Value = 90;
                    return 3;
                }
            }
            """);
        Assert.Equal(37, cpu.Memory[0x6000]);
        Assert.Equal(90, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void RepeatedCallsKeepCallerLocalsAndArrays()
    {
        var cpu = ExecuteProgram(
            """
            byte[] neighbors = new byte[8];
            neighbors[0] = 21;
            neighbors[3] = 75;
            byte result = 0;
            for (byte frame = 0; frame < 4; frame++)
            {
                result = Outer(9);
            }
            poke(0x6000, result);
            byte first = neighbors[0];
            poke(0x6001, first);
            byte second = neighbors[3];
            poke(0x6002, second);
            test_stop(); while (true) ;
            static extern void test_stop();
            static byte Outer(byte input)
            {
                byte saved = (byte)(input + 1);
                byte changed = Inner(3);
                return (byte)(saved - changed);
            }
            static byte Inner(byte input)
            {
                byte local = (byte)(input + 2);
                return (byte)(local + local);
            }
            """);
        Assert.Equal(new byte[] { 0, 21, 75 }, cpu.Memory[0x6000..0x6003]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void LocalThenConstantArgumentsPreserveArgumentOrder()
    {
        var cpu = ExecuteProgram(
            """
            byte value = Helpers.Get();
            Helpers.Store(value, 53);
            byte first = Helpers.First;
            poke(0x6000, first);
            byte second = Helpers.Second;
            poke(0x6001, second);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                public static byte First;
                public static byte Second;
                public static byte Get() => 7;
                public static void Store(byte first, byte second)
                {
                    First = first;
                    Second = second;
                }
            }
            """);
        Assert.Equal(new byte[] { 7, 53 }, cpu.Memory[0x6000..0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void ThreeNestedTermsPreserveAllIntermediateValues()
    {
        var cpu = ExecuteProgram(
            """
            byte[] data = new byte[4];
            data[0] = 31;
            data[1] = 7;
            data[2] = 4;
            byte result = 0;
            for (byte i = 0; i < 3; i++)
            {
                byte a = data[0];
                byte b = data[1];
                byte c = data[2];
                result = (byte)((a - (b << 1)) ^ ((c + i) << 2));
            }
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            """);
        Assert.Equal((31 - (7 << 1)) ^ ((4 + 2) << 2), cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void DuplicatedResultSurvivesAnotherInitializer()
    {
        var cpu = ExecuteProgram(
            """
            byte value = Helpers.Next();
            byte original = value;
            byte second = Helpers.Next();
            byte result = (byte)(original + value + second);
            poke(0x6000, result);
            test_stop(); while (true) ;
            static extern void test_stop();
            static class Helpers
            {
                static byte count;
                public static byte Next()
                {
                    count++;
                    return count;
                }
            }
            """);
        Assert.Equal(4, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }
}
