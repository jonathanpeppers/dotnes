using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Byte-sized values passed to a 16-bit (A:X) parameter must clear X.
/// Each test primes X with a nonzero value via a preceding 16-bit call so a
/// missing widening shows up as a stale high byte (same bug class as dotnes#642).
/// </summary>
public class WordArgumentWideningExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    const ushort PpuAddr = 0x2006;
    const int ScrollX = 0x0C;
    const int PpuCtrlVar1 = 0x0F;

    Cpu6502 ExecuteCapturingPpuAddr(string body, List<byte> ppuAddrWrites, Action<Cpu6502>? initialize = null) =>
        ExecuteProgram(
            $$"""
            {{body}}
            test_stop();
            while (true) ;
            static extern void test_stop();
            """,
            cpu =>
            {
                cpu.WriteBus = (address, value) =>
                {
                    if (address == PpuAddr)
                        ppuAddrWrites.Add(value);
                    cpu.Memory[address] = value;
                };
                initialize?.Invoke(cpu);
            });

    [Fact]
    public void VramAdrZeroClearsHighByte()
    {
        var writes = new List<byte>();
        ExecuteCapturingPpuAddr("vram_adr(0x2400); vram_adr(0x0000);", writes);
        Assert.Equal(new byte[] { 0x24, 0x00, 0x00, 0x00 }, writes.ToArray()[^4..]);
    }

    [Fact]
    public void VramAdrByteConstantClearsHighByte()
    {
        var writes = new List<byte>();
        ExecuteCapturingPpuAddr("vram_adr(0x2400); vram_adr(0x40);", writes);
        Assert.Equal(new byte[] { 0x24, 0x00, 0x00, 0x40 }, writes.ToArray()[^4..]);
    }

    [Fact]
    public void VramAdrByteLocalClearsHighByte()
    {
        var writes = new List<byte>();
        ExecuteCapturingPpuAddr(
            "vram_adr(0x2400); byte lo = peek(0x6000); vram_adr(lo);", writes,
            cpu => cpu.Memory[0x6000] = 0x37);
        Assert.Equal(new byte[] { 0x24, 0x00, 0x00, 0x37 }, writes.ToArray()[^4..]);
    }

    [Fact]
    public void VramAdrWordValuesPreserved()
    {
        var writes = new List<byte>();
        ExecuteCapturingPpuAddr("vram_adr(0x2400); vram_adr(0x0100);", writes);
        Assert.Equal(new byte[] { 0x24, 0x00, 0x01, 0x00 }, writes.ToArray()[^4..]);
    }

    [Fact]
    public void SetScrollXZeroClearsNametableBit()
    {
        var cpu = ExecuteCapturingPpuAddr("set_scroll_x(0x0101); set_scroll_x(0);", []);
        Assert.Equal(0, cpu.Memory[ScrollX]);
        Assert.Equal(0, cpu.Memory[PpuCtrlVar1] & 0x01);
    }

    [Fact]
    public void OneVramBufferByteAddressClearsHighByte()
    {
        var cpu = ExecuteCapturingPpuAddr("one_vram_buffer(1, 0x2345); one_vram_buffer(2, 0x40);", []);
        Assert.Equal(new byte[] { 0x23, 0x45, 1, 0x00, 0x40, 2, 0xFF }, cpu.Memory[0x0100..0x0107]);
    }

    [Theory]
    [InlineData("ushort", "0x40", 0x40, 0x00)]
    [InlineData("ushort", "0", 0x00, 0x00)]
    [InlineData("short", "(sbyte)-2", 0xFE, 0xFF)]
    [InlineData("short", "(sbyte)5", 0x05, 0x00)]
    public void ExternWordTailWidensNarrowArgument(string type, string argument, byte lo, byte hi)
    {
        using var transpiler = BuildProgram(
            $$"""
            vram_adr(0x2400);
            Store({{argument}});
            test_stop(); while (true) ;
            static extern void test_stop();
            static extern void Store({{type}} value);
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        program.CreateBlock("_Store").Emit(STA_abs(0x6000)).Emit(STX_abs(0x6001)).Emit(RTS());
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, program.GetLabels()["main"]);
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new[] { lo, hi }, cpu.Memory[0x6000..0x6002]);
    }

    [Theory]
    [InlineData(0, 0xFE, 0xFF)]
    [InlineData(1, 0x05, 0x00)]
    public void ExternWordTailPreservesConditionalSignedArgument(byte flag, byte lo, byte hi)
    {
        using var transpiler = BuildProgram(
            """
            vram_adr(0x2400);
            Store(peek(0x6010) == 0 ? (sbyte)-2 : (sbyte)5);
            test_stop(); while (true) ;
            static extern void test_stop();
            static extern void Store(short value);
            """, out var program);
        program.DefineExternalLabel("_test_stop", 0x7FF0);
        program.CreateBlock("_Store").Emit(STA_abs(0x6000)).Emit(STX_abs(0x6001)).Emit(RTS());
        var cpu = new Cpu6502(program.ToBytes(), program.BaseAddress, program.GetLabels()["main"]);
        cpu.Memory[0x6010] = flag;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new[] { lo, hi }, cpu.Memory[0x6000..0x6002]);
    }
}
