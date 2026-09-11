using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class EnumNumericStorageTests(ITestOutputHelper output) : ExecutionTests(output)
{
    public static IEnumerable<object[]> SupportedValues()
    {
        foreach (var (type, value) in new[]
        {
            ("byte", 255), ("sbyte", -128), ("sbyte", -1),
            ("short", -32768), ("short", -300), ("short", 32767),
            ("ushort", 0x1234), ("ushort", 65535),
        })
            foreach (bool runtime in new[] { false, true })
                yield return [type, value, runtime];
    }

    public static IEnumerable<object[]> CapturedValues() =>
        SupportedValues().Where(row => row[2] is false || row[0] is "short" or "ushort");

    [Theory]
    [MemberData(nameof(SupportedValues))]
    public void StaticEnumUsesItsUnderlyingWidthWithoutOverlappingNeighbor(string type, int value, bool runtime)
    {
        string initializer = runtime ? "(Value)ReadWord()" : $"(Value)({value})";
        var cpu = ExecuteProgram($$"""
            State.AValue = {{initializer}};
            State.BNeighbor = 90;
            ushort result = Read();
            byte neighbor = State.BNeighbor;
            poke(0x6000, (byte)result);
            poke(0x6001, (byte)(result >> 8));
            poke(0x6002, neighbor);
            test_stop(); while (true);
            static extern void test_stop();
            static ushort ReadWord() => {{unchecked((ushort)value)}};
            static ushort Read() => unchecked((ushort)State.AValue);
            enum Value : {{type}} { Zero }
            static class State { public static Value AValue; public static byte BNeighbor; }
            """);
        AssertResult(cpu, value);
    }

    [Theory]
    [MemberData(nameof(CapturedValues))]
    public void CapturedEnumUsesItsUnderlyingWidthWithoutOverlappingNeighbor(string type, int value, bool runtime)
    {
        string setup = runtime ? $"{type} source = ReadSource(); poke(0x6030, (byte)source);" : "";
        string initializer = runtime ? "(Value)source" : $"(Value)({value})";
        var cpu = ExecuteProgram($$"""
            {{setup}}
            Value value = {{initializer}};
            byte zNeighbor = 90;
            ushort result = Read();
            byte savedNeighbor = ReadNeighbor();
            poke(0x6000, (byte)result);
            poke(0x6001, (byte)(result >> 8));
            poke(0x6002, savedNeighbor);
            test_stop(); while (true);
            static extern void test_stop();
            static {{type}} ReadSource() => {{value}};
            ushort Read() => unchecked((ushort)value);
            byte ReadNeighbor() => zNeighbor;
            enum Value : {{type}} { Zero }
            """);
        AssertResult(cpu, value);
    }

    static void AssertResult(Cpu6502 cpu, int value)
    {
        Assert.Equal(unchecked((byte)value), cpu.Memory[0x6000]);
        Assert.Equal(unchecked((byte)(value >> 8)), cpu.Memory[0x6001]);
        Assert.Equal(90, cpu.Memory[0x6002]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData("long", "Int64")]
    [InlineData("ulong", "UInt64")]
    public void UnsupportedEnumUnderlyingTypesCannotBypassStorageValidation(string type, string decodedType)
    {
        foreach (var (body, declaration, storage) in new[]
        {
            ("Fill(out Value value); poke(0x6000, (byte)value);", "static extern void Fill(out Value value);", "Local"),
            ("poke(0x6000, (byte)State.Value);", "static class State { public static Value Value; }", "Static field 'Value'"),
            ("Fill(out Value value); poke(0x6000, Read());",
                "static extern void Fill(out Value value); byte Read() => (byte)value;", "Captured variable 'value'"),
        })
        {
            var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
                {{body}}
                while (true);
                {{declaration}}
                enum Value : {{type}} { Zero }
                """));
            Assert.Contains(storage, error.Message);
            Assert.Contains(decodedType, error.Message);
            Assert.Contains("explicitly supported storage type", error.Message);
        }
    }
}
