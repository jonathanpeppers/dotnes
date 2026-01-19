using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Tests for Program6502Writer - the adapter layer between old NESWriter API and new object model
/// </summary>
public class Program6502WriterTests
{
    #region Basic Write Tests (NESInstruction compatibility)

    [Fact]
    public void Write_ImpliedInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.RTS_impl);

        var bytes = writer.ToBytes();
        Assert.Equal([0x60], bytes);
    }

    [Fact]
    public void Write_ImmediateInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x42);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x42], bytes);
    }

    [Fact]
    public void Write_ZeroPageInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.STA_zpg, 0x17);

        var bytes = writer.ToBytes();
        Assert.Equal([0x85, 0x17], bytes);
    }

    [Fact]
    public void Write_AbsoluteInstruction_EmitsLittleEndian()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.JMP_abs, 0x823E);

        var bytes = writer.ToBytes();
        Assert.Equal([0x4C, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Write_JSRInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.JSR, 0x823E);

        var bytes = writer.ToBytes();
        Assert.Equal([0x20, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Write_MultipleInstructions_EmitsSequence()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x02);
        writer.Write(NESInstruction.STA_zpg, 0x17);
        writer.Write(NESInstruction.RTS_impl);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x02, 0x85, 0x17, 0x60], bytes);
    }

    #endregion

    #region Branch Instructions

    [Fact]
    public void Write_BranchWithOffset_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.BNE_rel, 0xFB); // -5 as signed byte

        var bytes = writer.ToBytes();
        Assert.Equal([0xD0, 0xFB], bytes);
    }

    [Fact]
    public void Write_BEQInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.BEQ_rel, 0x05);

        var bytes = writer.ToBytes();
        Assert.Equal([0xF0, 0x05], bytes);
    }

    #endregion

    #region Label-Based Addressing

    [Fact]
    public void WriteWithLabel_JSR_ResolvesLabel()
    {
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("_pal_col", 0x823E);
        writer.WriteWithLabel(NESInstruction.JSR, "_pal_col");

        var bytes = writer.ToBytes();
        Assert.Equal([0x20, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void WriteWithLabel_JMP_ResolvesLabel()
    {
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("forever", 0x8005);
        writer.WriteWithLabel(NESInstruction.JMP_abs, "forever");

        var bytes = writer.ToBytes();
        Assert.Equal([0x4C, 0x05, 0x80], bytes);
    }

    #endregion

    #region RemoveLastInstructions (SeekBack replacement)

    [Fact]
    public void RemoveLastInstructions_RemovesSingleInstruction()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x00);
        writer.Write(NESInstruction.STA_zpg, 0x17);
        writer.Write(NESInstruction.RTS_impl);

        writer.RemoveLastInstructions();

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00, 0x85, 0x17], bytes); // RTS removed
    }

    [Fact]
    public void RemoveLastInstructions_RemovesMultipleInstructions()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x00);
        writer.Write(NESInstruction.STA_zpg, 0x17);
        writer.Write(NESInstruction.RTS_impl);

        writer.RemoveLastInstructions(2);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00], bytes); // STA and RTS removed
    }

    [Fact]
    public void GetSizeOfLastInstructions_CalculatesCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x00);     // 2 bytes
        writer.Write(NESInstruction.JSR, 0x8000);   // 3 bytes
        writer.Write(NESInstruction.RTS_impl);      // 1 byte

        Assert.Equal(4, writer.GetSizeOfLastInstructions(2)); // JSR + RTS = 3 + 1
        Assert.Equal(6, writer.GetSizeOfLastInstructions(3)); // LDA + JSR + RTS = 2 + 3 + 1
    }

    #endregion

    #region Block Management

    [Fact]
    public void CreateBlock_CreatesNewBlock()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x00);

        var block = writer.CreateBlock("subroutine");
        writer.Write(NESInstruction.INX_impl);
        writer.Write(NESInstruction.RTS_impl);

        Assert.Equal(2, writer.Program.BlockCount);
    }

    [Fact]
    public void DefineLabel_AtCurrentPosition_WorksCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x00);     // $8000 - 2 bytes
        writer.DefineLabel("loop");
        writer.Write(NESInstruction.INX_impl);      // $8002 - label points here
        writer.Write(NESInstruction.RTS_impl);

        // Force resolve
        _ = writer.ToBytes();

        Assert.True(writer.Labels.IsDefined("loop"));
        Assert.Equal(0x8002, writer.Labels.Resolve("loop"));
    }

    #endregion

    #region Fluent API

    [Fact]
    public void Emit_FluentAPI_WorksCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Emit(LDA(0x02))
              .Emit(STA_zpg(0x17))
              .Emit(RTS());

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x02, 0x85, 0x17, 0x60], bytes);
    }

    [Fact]
    public void Emit_WithLabel_SetsLabelCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Emit(LDA(0x00), "start");
        writer.Emit(RTS());

        _ = writer.ToBytes();

        Assert.True(writer.Labels.IsDefined("start"));
        Assert.Equal(0x8000, writer.Labels.Resolve("start"));
    }

    #endregion

    #region CurrentSize

    [Fact]
    public void CurrentSize_ReflectsEmittedCode()
    {
        using var writer = new Program6502Writer();

        Assert.Equal(0, writer.CurrentSize);

        writer.Write(NESInstruction.LDA, 0x00); // 2 bytes
        Assert.Equal(2, writer.CurrentSize);

        writer.Write(NESInstruction.JSR, 0x8000); // 3 bytes
        Assert.Equal(5, writer.CurrentSize);

        writer.Write(NESInstruction.RTS_impl); // 1 byte
        Assert.Equal(6, writer.CurrentSize);
    }

    #endregion

    #region LastLDA Property

    [Fact]
    public void LastLDA_SetAfterLDAInstruction()
    {
        using var writer = new Program6502Writer();

        Assert.False(writer.LastLDA);

        writer.Write(NESInstruction.LDA, 0x42);
        Assert.True(writer.LastLDA);

        writer.Write(NESInstruction.STA_zpg, 0x17);
        Assert.False(writer.LastLDA);
    }

    #endregion

    #region Validation

    [Fact]
    public void Validate_FindsUnresolvedLabels()
    {
        using var writer = new Program6502Writer();

        writer.WriteWithLabel(NESInstruction.JSR, "undefined_sub");

        var unresolved = writer.Validate();

        Assert.Contains("undefined_sub", unresolved);
    }

    [Fact]
    public void Validate_NoUnresolvedLabels_ReturnsEmpty()
    {
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("my_sub", 0x8100);
        writer.WriteWithLabel(NESInstruction.JSR, "my_sub");

        var unresolved = writer.Validate();

        Assert.Empty(unresolved);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_PalColPattern_ProducesCorrectOutput()
    {
        // Replicates the typical pal_col call pattern
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("_pal_col", 0x823E);

        writer.Write(NESInstruction.LDA, 0x02);           // palette index
        writer.WriteWithLabel(NESInstruction.JSR, "_pal_col");
        
        // Infinite loop
        writer.DefineLabel("forever");
        writer.Write(NESInstruction.JMP_abs, 0x8005);     // JMP to self (at $8005)

        var bytes = writer.ToBytes();

        Assert.Equal(
            [0xA9, 0x02, 0x20, 0x3E, 0x82, 0x4C, 0x05, 0x80],
            bytes);
    }

    [Fact]
    public void Integration_MixedFluentAndLegacy_WorksTogether()
    {
        using var writer = new Program6502Writer();

        // Mix legacy NESInstruction API with new fluent API
        writer.Write(NESInstruction.LDA, 0x00);
        writer.Emit(STA_zpg(0x17));
        writer.Write(NESInstruction.INX_impl);
        writer.Emit(RTS());

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00, 0x85, 0x17, 0xE8, 0x60], bytes);
    }

    [Fact]
    public void Disassemble_ReturnsReadableOutput()
    {
        using var writer = new Program6502Writer();

        writer.Write(NESInstruction.LDA, 0x42);
        writer.Write(NESInstruction.RTS_impl);

        var disasm = writer.Disassemble();

        Assert.Contains("$8000", disasm);
        Assert.Contains("LDA", disasm);
        Assert.Contains("RTS", disasm);
    }

    #endregion

    #region All NESInstruction Conversion Tests

    // These tests verify that NESInstruction enum values convert correctly to opcodes
    // We use individual facts instead of Theory/InlineData because NESInstruction is internal

    [Fact]
    public void Write_ADC_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.ADC, 0x42);
        Assert.Equal([0x69, 0x42], writer.ToBytes());
    }

    [Fact]
    public void Write_AND_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.AND, 0x1F);
        Assert.Equal([0x29, 0x1F], writer.ToBytes());
    }

    [Fact]
    public void Write_EOR_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.EOR_imm, 0xFF);
        Assert.Equal([0x49, 0xFF], writer.ToBytes());
    }

    [Fact]
    public void Write_ORA_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.ORA, 0x80);
        Assert.Equal([0x09, 0x80], writer.ToBytes());
    }

    [Fact]
    public void Write_CMP_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.CMP_zpg, 0x17);
        Assert.Equal([0xC5, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_DEC_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.DEC_zpg, 0x17);
        Assert.Equal([0xC6, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_INC_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.INC_zpg, 0x17);
        Assert.Equal([0xE6, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_CLC_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.CLC);
        Assert.Equal([0x18], writer.ToBytes());
    }

    [Fact]
    public void Write_CLD_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.CLD_impl);
        Assert.Equal([0xD8], writer.ToBytes());
    }

    [Fact]
    public void Write_DEX_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.DEX_impl);
        Assert.Equal([0xCA], writer.ToBytes());
    }

    [Fact]
    public void Write_DEY_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.DEY_impl);
        Assert.Equal([0x88], writer.ToBytes());
    }

    [Fact]
    public void Write_SEC_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.SEC_impl);
        Assert.Equal([0x38], writer.ToBytes());
    }

    [Fact]
    public void Write_SEI_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.SEI_impl);
        Assert.Equal([0x78], writer.ToBytes());
    }

    [Fact]
    public void Write_TAX_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.TAX_impl);
        Assert.Equal([0xAA], writer.ToBytes());
    }

    [Fact]
    public void Write_TAY_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.TAY_impl);
        Assert.Equal([0xA8], writer.ToBytes());
    }

    [Fact]
    public void Write_TXA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.TXA_impl);
        Assert.Equal([0x8A], writer.ToBytes());
    }

    [Fact]
    public void Write_TXS_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.TXS_impl);
        Assert.Equal([0x9A], writer.ToBytes());
    }

    [Fact]
    public void Write_TYA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.TYA_impl);
        Assert.Equal([0x98], writer.ToBytes());
    }

    [Fact]
    public void Write_PHA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.PHA_impl);
        Assert.Equal([0x48], writer.ToBytes());
    }

    [Fact]
    public void Write_PHP_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.PHP_impl);
        Assert.Equal([0x08], writer.ToBytes());
    }

    [Fact]
    public void Write_PLA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.PLA_impl);
        Assert.Equal([0x68], writer.ToBytes());
    }

    [Fact]
    public void Write_PLP_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.PLP_impl);
        Assert.Equal([0x28], writer.ToBytes());
    }

    [Fact]
    public void Write_RTI_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(NESInstruction.RTI_impl);
        Assert.Equal([0x40], writer.ToBytes());
    }

    #endregion
}
