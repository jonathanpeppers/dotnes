namespace dotnes.tests;

public class Cpu6502InterruptTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x49)]
    [InlineData(0x86)]
    public void PhpAndPlpPreserveFlagsWithoutPersistentBreakFlag(byte status)
    {
        // Load status via PLP, save it with PHP, clobber every flag, restore it.
        byte[] code = [0xA9, status, 0x48, 0x28, 0x08, 0x18, 0xB8, 0xD8, 0x58, 0xA9, 0, 0x28];
        var cpu = new Cpu6502(code, 0x8000, 0x8000);
        cpu.RunUntil((ushort)(0x8000 + code.Length));
        Assert.Equal((byte)((status & 0xCF) | 0x20), cpu.Status);
        Assert.Equal((byte)(status | 0x30), cpu.Memory[0x1FF]);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x49)]
    [InlineData(0x86)]
    public void NmiAndRtiUseHardwareStackOrderAndRestoreExactPc(byte status)
    {
        var cpu = new Cpu6502([0xA9, status, 0x48, 0x28], 0x8000, 0x8000);
        cpu.RunUntil(0x8004);
        cpu.Memory[0xFFFA] = 0x00;
        cpu.Memory[0xFFFB] = 0x90;
        cpu.Memory[0x9000] = 0x40; // RTI
        byte savedStatus = cpu.Status;
        cpu.Nmi();
        Assert.Equal(0x9000, cpu.PC);
        Assert.Equal(0xFC, cpu.SP);
        Assert.Equal(0x80, cpu.Memory[0x1FF]);
        Assert.Equal(0x04, cpu.Memory[0x1FE]);
        Assert.Equal(savedStatus, cpu.Memory[0x1FD]);
        Assert.True(cpu.InterruptDisable);
        cpu.Step();
        Assert.Equal(0x8004, cpu.PC);
        Assert.Equal(savedStatus, cpu.Status);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Fact]
    public void SeiMasksIrqButNotNmiAndCliEnablesIrq()
    {
        var cpu = new Cpu6502([0x78, 0x58], 0x8000, 0x8000);
        cpu.Memory[0xFFFA] = 0x00;
        cpu.Memory[0xFFFB] = 0x90;
        cpu.Memory[0xFFFE] = 0x00;
        cpu.Memory[0xFFFF] = 0xA0;
        cpu.Memory[0x9000] = 0x40;
        cpu.Memory[0xA000] = 0x40;
        cpu.Step(); // SEI
        Assert.False(cpu.Irq());
        Assert.Equal(0x8001, cpu.PC);
        Assert.Equal(0xFF, cpu.SP);
        cpu.Nmi();
        Assert.Equal(0x9000, cpu.PC);
        cpu.Step();
        Assert.True(cpu.InterruptDisable);
        cpu.Step(); // CLI
        Assert.True(cpu.Irq());
        Assert.Equal(0xA000, cpu.PC);
        cpu.Step();
        Assert.Equal(0x8002, cpu.PC);
        Assert.False(cpu.InterruptDisable);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Fact]
    public void NmiCanInterruptAnIrqWithoutCorruptingItsReturnFrame()
    {
        var cpu = new Cpu6502([], 0x8000, 0x8000);
        cpu.Memory[0xFFFA] = 0x00;
        cpu.Memory[0xFFFB] = 0x90;
        cpu.Memory[0xFFFE] = 0x00;
        cpu.Memory[0xFFFF] = 0xA0;
        cpu.Memory[0x9000] = 0x40;
        cpu.Memory[0xA000] = 0x40;
        Assert.True(cpu.Irq());
        cpu.Nmi();
        Assert.Equal(0xF9, cpu.SP);
        cpu.Step();
        Assert.Equal(0xA000, cpu.PC);
        Assert.True(cpu.InterruptDisable);
        cpu.Step();
        Assert.Equal(0x8000, cpu.PC);
        Assert.False(cpu.InterruptDisable);
        Assert.Equal(0xFF, cpu.SP);
    }

    [Fact]
    public void DecimalFlagDoesNotEnableBcdArithmeticOnNes()
    {
        var cpu = new Cpu6502(Convert.FromHexString("F8A909186901"), 0x8000, 0x8000);
        cpu.RunUntil(0x8006);
        Assert.True(cpu.Decimal);
        Assert.Equal(0x0A, cpu.A);
    }
}
