using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericCallReviewTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void SignedBitwiseResultRetainsItsSignAcrossCall(int input)
    {
        var cpu = ExecuteProgram("""
            short value = (short)(((sbyte)peek(0x6010) | (sbyte)peek(0x6011)) + Next());
            byte low = (byte)value;
            byte high = (byte)(value >> 8);
            poke(0x6000, low);
            poke(0x6001, high);
            test_stop(); while (true);
            static extern void test_stop();
            static byte Next() => 0;
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal(input, cpu.Memory[0x6000]);
        Assert.Equal(input >= 128 ? 0xFF : 0, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    public static IEnumerable<object[]> SignedComparisons()
    {
        foreach (int input in new[] { -128, -1, 0, 127 })
            foreach (string operation in new[] { "<", "<=", ">", ">=", "==", "!=" })
                foreach (bool converted in new[] { false, true })
                    foreach (bool value in new[] { false, true })
                        yield return [input, operation, converted, value];
    }

    [Theory]
    [MemberData(nameof(SignedComparisons))]
    public void SignedCallComparisonKeepsItsSign(int input, string operation, bool converted, bool value)
    {
        string operand = converted ? "(sbyte)peek(0x6010)" : "Get()";
        string comparison = $"{operand} {operation} 127";
        string use = value ? $"byte result = (byte)({comparison} ? 1 : 0);"
            : $"byte result = 0; if ({comparison}) result = 1;";
        var cpu = ExecuteProgram($$"""
            {{use}}
            poke(0x6000, result);
            byte calls = State.Calls;
            poke(0x6001, calls);
            test_stop(); while (true);
            static extern void test_stop();
            static sbyte Get()
            {
                State.Calls = (byte)(State.Calls + 1);
                return (sbyte)peek(0x6010);
            }
            static class State { public static byte Calls; }
            """, cpu => cpu.Memory[0x6010] = unchecked((byte)input));
        bool expected = operation switch
        {
            "<" => input < 127,
            "<=" => input <= 127,
            ">" => input > 127,
            ">=" => input >= 127,
            "==" => input == 127,
            _ => input != 127,
        };
        Assert.Equal(expected ? 1 : 0, cpu.Memory[0x6000]);
        Assert.Equal(converted ? 0 : 1, cpu.Memory[0x6001]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("uint", "UInt32")]
    [InlineData("long", "Int64")]
    [InlineData("ulong", "UInt64")]
    [InlineData("float", "Single")]
    [InlineData("double", "Double")]
    [InlineData("char", "Char")]
    [InlineData("nint", "IntPtr")]
    [InlineData("nuint", "UIntPtr")]
    public void UnsupportedReturnSignatureIsRejectedBeforeEmission(string type, string decodedType)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            while (true);
            static class Helpers
            {
                public static {{type}} Value;
                public static {{type}} Get() => Value;
            }
            """));
        Assert.Contains($"Return type {decodedType}", error.Message);
        Assert.Contains("supported return type", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void BooleanReturnKeepsItsExistingByteConvention(int input)
    {
        var cpu = ExecuteProgram("""
            byte result = (byte)(Get() ? 1 : 0);
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static bool Get() => peek(0x6010) != 0;
            """, cpu => cpu.Memory[0x6010] = (byte)input);
        Assert.Equal(input == 0 ? 0 : 1, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("int", "Int32")]
    [InlineData("uint", "UInt32")]
    public void IntReturnCannotBeMisreadAsByteAtItsCaller(string type, string decodedType)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            byte result = (byte)(Get() >> 8);
            poke(0x6000, result);
            while (true);
            static {{type}} Get() => 300;
            """));
        Assert.Contains($"Return type {decodedType}", error.Message);
        Assert.Contains("supported return type", error.Message);
    }
}
