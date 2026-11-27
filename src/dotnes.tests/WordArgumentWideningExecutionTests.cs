using Xunit.Abstractions;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Byte-sized values passed to a 16-bit (A:X) parameter must clear or sign-extend X
/// (dotnes#642). Tests first make X nonzero, so a missing widening leaves a stale high byte.
/// </summary>
public class WordArgumentWideningExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    const ushort PpuAddr = 0x2006;
    const int ScrollX = 0x0C;
    const int PpuCtrlVar1 = 0x0F;

    Cpu6502 Execute(string body, Action<Cpu6502>? initialize = null, List<byte>? ppuAddrWrites = null) =>
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
                        ppuAddrWrites?.Add(value);
                    cpu.Memory[address] = value;
                };
                initialize?.Invoke(cpu);
            });

    [Theory]
    [InlineData("0x0000", 0x00, 0x00)]
    [InlineData("0x40", 0x00, 0x40)]
    [InlineData("lo", 0x00, 0x37)]
    [InlineData("0x0100", 0x01, 0x00)]
    public void VramAdrWritesFullAddress(string argument, byte hi, byte lo)
    {
        var writes = new List<byte>();
        Execute($"vram_adr(0x2400); byte lo = peek(0x6000); vram_adr({argument});",
            cpu => cpu.Memory[0x6000] = 0x37, writes);
        Assert.Equal(new byte[] { 0x24, 0x00, hi, lo }, writes.ToArray()[^4..]);
    }

    [Fact]
    public void SetScrollXZeroClearsNametableBit()
    {
        var cpu = Execute("set_scroll_x(0x0101); set_scroll_x(0);");
        Assert.Equal(0, cpu.Memory[ScrollX]);
        Assert.Equal(0, cpu.Memory[PpuCtrlVar1] & 0x01);
    }

    [Fact]
    public void OneVramBufferByteAddressClearsHighByte()
    {
        var cpu = Execute("one_vram_buffer(1, 0x2345); one_vram_buffer(2, 0x40);");
        Assert.Equal(new byte[] { 0x23, 0x45, 1, 0x00, 0x40, 2, 0xFF }, cpu.Memory[0x0100..0x0107]);
    }

    [Theory]
    [InlineData("multi_vram_buffer_horz", "NTADR_A(2, 2)", 0x60, 0x42)]
    [InlineData("multi_vram_buffer_vert", "NTADR_A(2, 2)", 0xA0, 0x42)]
    [InlineData("multi_vram_buffer_horz", "NTADR_A(x, 2)", 0x60, 0x45)]
    [InlineData("multi_vram_buffer_horz", "0x40", 0x40, 0x40)]
    public void MultiVramBufferPushesDataAndLength(string method, string address, byte hi, byte lo)
    {
        var cpu = Execute(
            $$"""
            byte[] data = new byte[] { 1, 2, 3, 4 };
            byte x = peek(0x6000);
            byte length = peek(0x6001);
            {{method}}(data, length, {{address}});
            """,
            cpu =>
            {
                cpu.Memory[0x6000] = 5;
                cpu.Memory[0x6001] = 4;
            });
        Assert.Equal(new byte[] { hi, lo, 4, 1, 2, 3, 4, 0xFF }, cpu.Memory[0x0100..0x0108]);
        Assert.Equal(Cpu6502.SoftwareStackTop, cpu.SoftwareStackPointer);
    }

    [Theory]
    [InlineData("ushort", "0x40", 0, 0x40, 0x00)]
    [InlineData("ushort", "0", 0, 0x00, 0x00)]
    [InlineData("short", "(sbyte)-2", 0, 0xFE, 0xFF)]
    [InlineData("short", "(sbyte)5", 0, 0x05, 0x00)]
    [InlineData("short", "peek(0x6010) == 0 ? (sbyte)-2 : (sbyte)5", 0, 0xFE, 0xFF)]
    [InlineData("short", "peek(0x6010) == 0 ? (sbyte)-2 : (sbyte)5", 1, 0x05, 0x00)]
    public void ExternWordTailWidensNarrowArgument(string type, string argument, byte input, byte lo, byte hi)
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
        cpu.Memory[0x6010] = input;
        cpu.RunUntil(0x7FF0);
        Assert.Equal(new[] { lo, hi }, cpu.Memory[0x6000..0x6002]);
    }
}