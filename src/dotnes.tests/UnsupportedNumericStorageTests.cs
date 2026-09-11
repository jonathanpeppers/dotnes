using System.Reflection.Metadata;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class UnsupportedNumericStorageTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Fact]
    public void UInt32LocalCannotLoseItsHighByteBeforeExplicitWordConversion()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            uint value = 300;
            poke(0x6020, (byte)value);
            byte high = (byte)((ushort)value >> 8);
            poke(0x6000, high);
            while (true);
            """));
        Assert.Contains("Local 0", error.Message);
        Assert.Contains("UInt32", error.Message);
        Assert.Contains("explicitly supported storage type", error.Message);
    }

    [Theory]
    [InlineData("uint", "UInt32")]
    [InlineData("long", "Int64")]
    [InlineData("ulong", "UInt64")]
    [InlineData("float", "Single")]
    [InlineData("double", "Double")]
    [InlineData("char", "Char")]
    [InlineData("nint", "IntPtr")]
    [InlineData("nuint", "UIntPtr")]
    public void UnsupportedLocalIsRejectedEvenWhenWrittenThroughItsAddress(string type, string decodedType)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            Fill(out {{type}} value);
            byte high = (byte)((ushort)value >> 8);
            poke(0x6000, high);
            while (true);
            static extern void Fill(out {{type}} value);
            """));
        Assert.Contains("Local", error.Message);
        Assert.Contains(decodedType, error.Message);
        Assert.Contains("explicitly supported storage type", error.Message);
    }

    [Fact]
    public void AddressWrittenInt32LocalHasNoCompactRangeProof()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            Fill(out int value);
            byte high = (byte)((ushort)value >> 8);
            poke(0x6000, high);
            while (true);
            static extern void Fill(out int value);
            """));
        Assert.Contains("Int32 local", error.Message);
        Assert.Contains("cannot be proven", error.Message);
    }

    [Fact]
    public void UnusedUnsupportedSignatureSlotDoesNotAllocateStorage()
    {
        ILInstruction[] il = [new(ILOpCode.Ret, 0)];
        var types = new MethodNumericTypes([PrimitiveTypeCode.UInt32], [], PrimitiveTypeCode.Void);
        var ranges = new NumericRangeAnalysis(il, types, new Dictionary<string, MethodNumericTypes>(), new ReflectionCache());
        Assert.Empty(ranges.GetCompactIntLocals("main"));
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
    public void UnsupportedStaticFieldCannotUseImplicitWordStorage(string type, string decodedType)
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes($$"""
            byte high = (byte)((ushort)State.Value >> 8);
            poke(0x6000, high);
            while (true);
            static class State { public static {{type}} Value; }
            """));
        Assert.Contains("Static field 'Value'", error.Message);
        Assert.Contains(decodedType, error.Message);
        Assert.Contains("explicitly supported storage type", error.Message);
    }
}
