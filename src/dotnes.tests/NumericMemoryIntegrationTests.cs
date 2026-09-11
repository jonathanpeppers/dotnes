using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

public class NumericMemoryIntegrationTests(ITestOutputHelper output) : ExecutionTests(output)
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(127, false)]
    [InlineData(128, false)]
    [InlineData(255, false)]
    [InlineData(0, true)]
    [InlineData(127, true)]
    [InlineData(128, true)]
    [InlineData(255, true)]
    public void WordConversionAcrossCallUsesSourceSignedness(byte input, bool signed)
    {
        string value = signed ? "(sbyte)Get()" : "Get()";
        var cpu = ExecuteProgram($$"""
            short value = (short)({{value}});
            Ignore();
            poke(0x6000, (byte)value);
            poke(0x6001, (byte)(value >> 8));
            test_stop(); while (true);
            static extern void test_stop();
            static byte Get() => peek(0x6010);
            static void Ignore() => poke(0x60F0, 1);
            """, cpu => cpu.Memory[0x6010] = input);
        short expected = signed ? (sbyte)input : input;
        Assert.Equal((byte)expected, cpu.Memory[0x6000]);
        Assert.Equal((byte)(expected >> 8), cpu.Memory[0x6001]);
        Assert.Equal(1, cpu.Memory[0x60F0]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void SharedAddressUsesOriginalCompactIntProof(byte offset)
    {
        var cpu = ExecuteProgram("""
            int address = 0x6000 + peek(0x6010);
            poke(0x6020, (byte)address);
            ushort shared;
            poke(shared = (ushort)address, (byte)(peek(shared) + 1));
            test_stop(); while (true);
            static extern void test_stop();
            """, cpu =>
            {
                cpu.Memory[0x6010] = offset;
                cpu.Memory[0x6000 + offset] = 10;
            });
        Assert.Equal(offset, cpu.Memory[0x6020]);
        Assert.Equal(11, cpu.Memory[0x6000 + offset]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void SharedAddressSpillCannotAuthorizeAWiderSourceShift()
    {
        var error = Assert.Throws<TranspileException>(() => GetProgramBytes("""
            ushort shared;
            poke(shared = (ushort)((Get255() << 9) >> 8), peek(shared));
            while (true);
            static byte Get255() => peek(0x6010);
            """));
        Assert.Contains("promoted result wider", error.Message);
    }
}
