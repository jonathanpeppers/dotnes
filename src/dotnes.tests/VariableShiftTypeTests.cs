using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class VariableShiftTypeTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData("sbyte", "field")]
    [InlineData("sbyte", "local")]
    [InlineData("sbyte", "parameter")]
    [InlineData("short", "field")]
    [InlineData("short", "local")]
    [InlineData("short", "parameter")]
    [InlineData("int", "field")]
    [InlineData("int", "local")]
    [InlineData("int", "parameter")]
    [InlineData("uint", "field")]
    [InlineData("uint", "local")]
    [InlineData("uint", "parameter")]
    [InlineData("long", "field")]
    [InlineData("long", "local")]
    [InlineData("long", "parameter")]
    [InlineData("ulong", "field")]
    [InlineData("ulong", "local")]
    [InlineData("ulong", "parameter")]
    [InlineData("ushort", "parameter")]
    public void SignedAndUnsupportedWideSourcesAreDiagnosed(string type, string source)
    {
        string body = source switch
        {
            "field" => "byte result = (byte)(State.Value >> State.Count); poke(0x6000, result);",
            "local" => $$"""
                {{type}} value = State.Value;
                byte frame = 0;
                while (frame < 1)
                {
                    byte result = (byte)(value >> State.Count);
                    poke(0x6000, result);
                    frame++;
                }
                """,
            "parameter" => "byte result = Shift(State.Value, State.Count); poke(0x6000, result);",
            _ => throw new ArgumentException("Unexpected source kind.", nameof(source)),
        };
        string method = source == "parameter"
            ? $"static byte Shift({type} value, byte count) => (byte)(value >> count);" : "";
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            State.Count = 1;
            {{body}}
            while (true);
            {{method}}
            static class State { public static {{type}} Value; public static byte Count; }
            """));
        Assert.True(error.Message.Contains("Variable shifts") || error.Message.Contains("Int32 local")
            || error.Message.Contains("Parameter") || error.Message.Contains("unsupported primitive type"), error.Message);
    }

    [Theory]
    [InlineData("sbyte")]
    [InlineData("short")]
    public void ExplicitUnsignedWordConversionPreservesSignedSourceBits(string type)
    {
        var cpu = ExecuteProgram($$"""
            State.Value = -128;
            State.Count = 1;
            byte result = (byte)((ushort)State.Value >> State.Count);
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static class State { public static {{type}} Value; public static byte Count; }
            """);
        Assert.Equal(0xC0, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData(0, 1, 127)]
    [InlineData(256, 8, 1)]
    public void BoundedIntLocalUsesItsProvenUnsignedRange(int addition, int count, int expected)
    {
        var cpu = ExecuteProgram($$"""
            State.Count = {{count}};
            int value = peek(0x6010) + {{addition}};
            poke(0x6030, (byte)value);
            byte result = (byte)(value >> State.Count);
            poke(0x6000, result);
            test_stop(); while (true);
            static extern void test_stop();
            static class State { public static byte Count; }
            """, cpu => cpu.Memory[0x6010] = 255);
        Assert.Equal(expected, cpu.Memory[0x6000]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Fact]
    public void MissingSourceMetadataIsNotAssumedUnsigned()
    {
        using var stream = new MemoryStream();
        using var writer = new IL2NESWriter(stream);
        writer.Instructions =
        [
            new(ILOpCode.Ldsfld, 0, String: "Value"),
            new(ILOpCode.Ldsfld, 5, String: "Count"),
            new(ILOpCode.Ldc_i4_s, 10, 31),
            new(ILOpCode.And, 12),
            new(ILOpCode.Shr, 13),
            new(ILOpCode.Conv_u1, 14),
        ];
        writer.Index = 1;
        writer.Stack.Push(0);
        var error = Assert.Throws<TranspileException>(() => writer.Write(writer.Instructions[1], "Count"));
        Assert.Contains("source type 'unknown'", error.Message);
    }

    [Fact]
    public void ConflictingFieldDeclarationsAreNotAssumedUnsigned()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            Unsigned.Value = 128;
            Signed.Value = -128;
            State.Count = 1;
            byte result = (byte)(Unsigned.Value >> State.Count);
            poke(0x6000, result);
            while (true);
            static class Unsigned { public static byte Value; }
            static class Signed { public static sbyte Value; }
            static class State { public static byte Count; }
            """));
        Assert.Contains("source type 'unknown'", error.Message);
    }
}
