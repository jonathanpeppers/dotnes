using Xunit.Abstractions;

namespace dotnes.tests;

/// <summary>
/// Regression tests for dotnes#642: set_vram_update(0) only cleared A,
/// leaving X holding the high byte of the previous buffer pointer.
/// </summary>
public class SetVramUpdateExecutionTests(ITestOutputHelper output) : ExecutionTests(output)
{
    // NAME_UPD_ADR ($04-$05) and NAME_UPD_ENABLE ($06) — see
    // BuiltInSubroutines.SetVramUpdate().
    const int NameUpdAdrLo = 0x04;
    const int NameUpdAdrHi = 0x05;
    const int NameUpdEnable = 0x06;

    // A 25-byte vertical update stream, same shape as game2048's UPD_LOST.
    const string UpdBuffer = """
        byte[] UPD = new byte[25] {
            0xA8, 0xBD, 21,
            0x39,0x2F,0x35,0x00,0x2C,0x2F,0x33,0x34,0x00,0x00,
            0x30,0x32,0x25,0x33,0x33,0x00,0x33,0x34,0x21,0x32,0x34,
            0xFF
        };
        """;

    // Selects the ROM buffer first, so X holds its nonzero high byte like
    // game2048's loss handler does, then runs the given call.
    Cpu6502 ExecuteAfterBuffer(string call) => ExecuteProgram(
        $$"""
        {{UpdBuffer}}
        set_vram_update(UPD);
        {{call}}
        test_stop();
        while (true) ;
        static extern void test_stop();
        """);

    [Fact]
    public void SetVramUpdateZeroClearsPointerAndEnable()
    {
        var cpu = ExecuteAfterBuffer("set_vram_update(0);");
        Assert.Equal(0, cpu.Memory[NameUpdAdrLo]);
        Assert.Equal(0, cpu.Memory[NameUpdAdrHi]);
        Assert.Equal(0, cpu.Memory[NameUpdEnable]);
    }

    [Fact]
    public void SetVramUpdateNonzeroByteAddress()
    {
        var cpu = ExecuteAfterBuffer("set_vram_update(0x40);");
        Assert.Equal(0x40, cpu.Memory[NameUpdAdrLo]);
        Assert.Equal(0, cpu.Memory[NameUpdAdrHi]);
        Assert.Equal(0x40, cpu.Memory[NameUpdEnable]);
    }

    [Fact]
    public void SetVramUpdate0100PreservesHighByte()
    {
        // $0100 has a zero low byte but a nonzero high byte — it must not
        // be treated as "disable updates".
        var cpu = ExecuteAfterBuffer("set_vram_update(0x0100);");
        Assert.Equal(0, cpu.Memory[NameUpdAdrLo]);
        Assert.Equal(1, cpu.Memory[NameUpdAdrHi]);
        Assert.Equal(1, cpu.Memory[NameUpdEnable]);
    }

    [Fact]
    public void SetVramUpdateArrayPointerPreserved()
    {
        var cpu = ExecuteAfterBuffer("");
        Assert.NotEqual(0, cpu.Memory[NameUpdAdrHi]);
        Assert.Equal(cpu.Memory[NameUpdAdrLo] | cpu.Memory[NameUpdAdrHi], cpu.Memory[NameUpdEnable]);
    }
}
