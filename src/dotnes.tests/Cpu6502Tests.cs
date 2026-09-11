namespace dotnes.tests;

public class Cpu6502Tests
{
    [Theory]
    [InlineData("A97F186901", 0x80, false, false, true, true)]
    [InlineData("A9FF186901", 0, true, true, false, false)]
    [InlineData("A90038E901", 255, false, false, true, false)]
    [InlineData("A98038E901", 127, true, false, false, true)]
    [InlineData("A901C902", 1, false, false, true, false)]
    [InlineData("A9810A", 2, true, false, false, false)]
    [InlineData("A901386A", 128, true, false, true, false)]
    public void ArithmeticFlags(string hex, byte a, bool carry, bool zero, bool negative, bool overflow)
    {
        byte[] code = Convert.FromHexString(hex);
        var cpu = new Cpu6502(code, 0x8000, 0x8000);
        cpu.RunUntil((ushort)(0x8000 + code.Length));
        Assert.Equal(a, cpu.A);
        Assert.Equal(carry, cpu.Carry);
        Assert.Equal(zero, cpu.Zero);
        Assert.Equal(negative, cpu.Negative);
        Assert.Equal(overflow, cpu.Overflow);
    }

    [Fact]
    public void CallsAndHardwareStack()
    {
        // JSR $8006; JMP $800D; LDA #$42; PHA; LDA #0; PLA; RTS
        var cpu = new Cpu6502(Convert.FromHexString("2006804C0D80A94248A9006860"), 0x8000, 0x8000);
        cpu.RunUntil(0x800D);
        Assert.Equal(0x42, cpu.A);
        Assert.Equal(0xFF, cpu.SP);
        Assert.Equal(0x80, cpu.Memory[0x1FF]);
        Assert.Equal(2, cpu.Memory[0x1FE]);
    }

    [Fact]
    public void IndirectAddressingWrapsZeroPageAndCrossesPages()
    {
        var cpu = new Cpu6502(Convert.FromHexString("A002B1FF8D0060"), 0x8000, 0x8000);
        cpu.Memory[0xFF] = 0xFF;
        cpu.Memory[0] = 0x60;
        cpu.Memory[0x6101] = 0xAB;
        cpu.RunUntil(0x8007);
        Assert.Equal(0xAB, cpu.Memory[0x6000]);
    }

    [Fact]
    public void RelativeBranchesUseSignedOffsets()
    {
        var cpu = new Cpu6502(Convert.FromHexString("A203CAD0FD"), 0x8000, 0x8000);
        cpu.RunUntil(0x8005);
        Assert.Equal(0, cpu.X);
        Assert.Equal(7, cpu.InstructionCount);
    }

    [Fact]
    public void UnsupportedInstructionsAndNonterminationFail()
    {
        var cpu = new Cpu6502([0], 0x8000, 0x8000);
        Assert.Contains("Unsupported BRK", Assert.Throws<InvalidOperationException>(() => cpu.Step()).Message);
        cpu = new Cpu6502([0x4C, 0, 0x80], 0x8000, 0x8000);
        Assert.Contains("exceeded", Assert.Throws<InvalidOperationException>(() => cpu.RunUntil(0x9000, 10)).Message);
    }
}
