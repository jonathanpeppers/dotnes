using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Tests for Program6502Writer - the adapter layer that emits 6502 code using the new object model
/// </summary>
public class Program6502WriterTests
{
    #region Basic Write Tests

    [Fact]
    public void Write_ImpliedInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.RTS);

        var bytes = writer.ToBytes();
        Assert.Equal([0x60], bytes);
    }

    [Fact]
    public void Write_ImmediateInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x42);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x42], bytes);
    }

    [Fact]
    public void Write_ZeroPageInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.STA, AddressMode.ZeroPage, 0x17);

        var bytes = writer.ToBytes();
        Assert.Equal([0x85, 0x17], bytes);
    }

    [Fact]
    public void Write_AbsoluteInstruction_EmitsLittleEndian()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.JMP, AddressMode.Absolute, (ushort)0x823E);

        var bytes = writer.ToBytes();
        Assert.Equal([0x4C, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Write_JSRInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.JSR, AddressMode.Absolute, (ushort)0x823E);

        var bytes = writer.ToBytes();
        Assert.Equal([0x20, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Write_MultipleInstructions_EmitsSequence()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x02);
        writer.Write(Opcode.STA, AddressMode.ZeroPage, 0x17);
        writer.Write(Opcode.RTS);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x02, 0x85, 0x17, 0x60], bytes);
    }

    #endregion

    #region Branch Instructions

    [Fact]
    public void Write_BranchWithOffset_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.BNE, AddressMode.Relative, 0xFB); // -5 as signed byte

        var bytes = writer.ToBytes();
        Assert.Equal([0xD0, 0xFB], bytes);
    }

    [Fact]
    public void Write_BEQInstruction_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.BEQ, AddressMode.Relative, 0x05);

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
        writer.WriteWithLabel(Opcode.JSR, AddressMode.Absolute, "_pal_col");

        var bytes = writer.ToBytes();
        Assert.Equal([0x20, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void WriteWithLabel_JMP_ResolvesLabel()
    {
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("forever", 0x8005);
        writer.WriteWithLabel(Opcode.JMP, AddressMode.Absolute, "forever");

        var bytes = writer.ToBytes();
        Assert.Equal([0x4C, 0x05, 0x80], bytes);
    }

    #endregion

    #region RemoveLastInstructions (SeekBack replacement)

    [Fact]
    public void RemoveLastInstructions_RemovesSingleInstruction()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);
        writer.Write(Opcode.STA, AddressMode.ZeroPage, 0x17);
        writer.Write(Opcode.RTS);

        writer.RemoveLastInstructions();

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00, 0x85, 0x17], bytes); // RTS removed
    }

    [Fact]
    public void RemoveLastInstructions_RemovesMultipleInstructions()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);
        writer.Write(Opcode.STA, AddressMode.ZeroPage, 0x17);
        writer.Write(Opcode.RTS);

        writer.RemoveLastInstructions(2);

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00], bytes); // STA and RTS removed
    }

    [Fact]
    public void GetSizeOfLastInstructions_CalculatesCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);     // 2 bytes
        writer.Write(Opcode.JSR, AddressMode.Absolute, (ushort)0x8000);   // 3 bytes
        writer.Write(Opcode.RTS);      // 1 byte

        Assert.Equal(4, writer.GetSizeOfLastInstructions(2)); // JSR + RTS = 3 + 1
        Assert.Equal(6, writer.GetSizeOfLastInstructions(3)); // LDA + JSR + RTS = 2 + 3 + 1
    }

    #endregion

    #region Block Management

    [Fact]
    public void CreateBlock_CreatesNewBlock()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);

        var block = writer.CreateBlock("subroutine");
        writer.Write(Opcode.INX);
        writer.Write(Opcode.RTS);

        Assert.Equal(2, writer.Program.BlockCount);
    }

    [Fact]
    public void DefineLabel_AtCurrentPosition_WorksCorrectly()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);     // $8000 - 2 bytes
        writer.DefineLabel("loop");
        writer.Write(Opcode.INX);      // $8002 - label points here
        writer.Write(Opcode.RTS);

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

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00); // 2 bytes
        Assert.Equal(2, writer.CurrentSize);

        writer.Write(Opcode.JSR, AddressMode.Absolute, (ushort)0x8000); // 3 bytes
        Assert.Equal(5, writer.CurrentSize);

        writer.Write(Opcode.RTS); // 1 byte
        Assert.Equal(6, writer.CurrentSize);
    }

    #endregion

    #region LastLDA Property

    [Fact]
    public void LastLDA_SetAfterLDAInstruction()
    {
        using var writer = new Program6502Writer();

        Assert.False(writer.LastLDA);

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x42);
        Assert.True(writer.LastLDA);

        writer.Write(Opcode.STA, AddressMode.ZeroPage, 0x17);
        Assert.False(writer.LastLDA);
    }

    #endregion

    #region Validation

    [Fact]
    public void Validate_FindsUnresolvedLabels()
    {
        using var writer = new Program6502Writer();

        writer.WriteWithLabel(Opcode.JSR, AddressMode.Absolute, "undefined_sub");

        var unresolved = writer.Validate();

        Assert.Contains("undefined_sub", unresolved);
    }

    [Fact]
    public void Validate_NoUnresolvedLabels_ReturnsEmpty()
    {
        using var writer = new Program6502Writer();

        writer.DefineExternalLabel("my_sub", 0x8100);
        writer.WriteWithLabel(Opcode.JSR, AddressMode.Absolute, "my_sub");

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

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x02);           // palette index
        writer.WriteWithLabel(Opcode.JSR, AddressMode.Absolute, "_pal_col");
        
        // Infinite loop
        writer.DefineLabel("forever");
        writer.Write(Opcode.JMP, AddressMode.Absolute, (ushort)0x8005);     // JMP to self (at $8005)

        var bytes = writer.ToBytes();

        Assert.Equal(
            [0xA9, 0x02, 0x20, 0x3E, 0x82, 0x4C, 0x05, 0x80],
            bytes);
    }

    [Fact]
    public void Integration_MixedFluentAndWrite_WorksTogether()
    {
        using var writer = new Program6502Writer();

        // Mix Write API with fluent API
        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x00);
        writer.Emit(STA_zpg(0x17));
        writer.Write(Opcode.INX);
        writer.Emit(RTS());

        var bytes = writer.ToBytes();
        Assert.Equal([0xA9, 0x00, 0x85, 0x17, 0xE8, 0x60], bytes);
    }

    [Fact]
    public void Disassemble_ReturnsReadableOutput()
    {
        using var writer = new Program6502Writer();

        writer.Write(Opcode.LDA, AddressMode.Immediate, 0x42);
        writer.Write(Opcode.RTS);

        var disasm = writer.Disassemble();

        Assert.Contains("$8000", disasm);
        Assert.Contains("LDA", disasm);
        Assert.Contains("RTS", disasm);
    }

    #endregion

    #region All Opcode/AddressMode Tests

    [Fact]
    public void Write_ADC_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.ADC, AddressMode.Immediate, 0x42);
        Assert.Equal([0x69, 0x42], writer.ToBytes());
    }

    [Fact]
    public void Write_AND_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.AND, AddressMode.Immediate, 0x1F);
        Assert.Equal([0x29, 0x1F], writer.ToBytes());
    }

    [Fact]
    public void Write_EOR_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.EOR, AddressMode.Immediate, 0xFF);
        Assert.Equal([0x49, 0xFF], writer.ToBytes());
    }

    [Fact]
    public void Write_ORA_ImmediateMode_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.ORA, AddressMode.Immediate, 0x80);
        Assert.Equal([0x09, 0x80], writer.ToBytes());
    }

    [Fact]
    public void Write_CMP_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.CMP, AddressMode.ZeroPage, 0x17);
        Assert.Equal([0xC5, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_DEC_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.DEC, AddressMode.ZeroPage, 0x17);
        Assert.Equal([0xC6, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_INC_ZeroPage_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.INC, AddressMode.ZeroPage, 0x17);
        Assert.Equal([0xE6, 0x17], writer.ToBytes());
    }

    [Fact]
    public void Write_CLC_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.CLC);
        Assert.Equal([0x18], writer.ToBytes());
    }

    [Fact]
    public void Write_CLD_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.CLD);
        Assert.Equal([0xD8], writer.ToBytes());
    }

    [Fact]
    public void Write_DEX_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.DEX);
        Assert.Equal([0xCA], writer.ToBytes());
    }

    [Fact]
    public void Write_DEY_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.DEY);
        Assert.Equal([0x88], writer.ToBytes());
    }

    [Fact]
    public void Write_SEC_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.SEC);
        Assert.Equal([0x38], writer.ToBytes());
    }

    [Fact]
    public void Write_SEI_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.SEI);
        Assert.Equal([0x78], writer.ToBytes());
    }

    [Fact]
    public void Write_TAX_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.TAX);
        Assert.Equal([0xAA], writer.ToBytes());
    }

    [Fact]
    public void Write_TAY_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.TAY);
        Assert.Equal([0xA8], writer.ToBytes());
    }

    [Fact]
    public void Write_TXA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.TXA);
        Assert.Equal([0x8A], writer.ToBytes());
    }

    [Fact]
    public void Write_TXS_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.TXS);
        Assert.Equal([0x9A], writer.ToBytes());
    }

    [Fact]
    public void Write_TYA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.TYA);
        Assert.Equal([0x98], writer.ToBytes());
    }

    [Fact]
    public void Write_PHA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.PHA);
        Assert.Equal([0x48], writer.ToBytes());
    }

    [Fact]
    public void Write_PHP_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.PHP);
        Assert.Equal([0x08], writer.ToBytes());
    }

    [Fact]
    public void Write_PLA_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.PLA);
        Assert.Equal([0x68], writer.ToBytes());
    }

    [Fact]
    public void Write_PLP_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.PLP);
        Assert.Equal([0x28], writer.ToBytes());
    }

    [Fact]
    public void Write_RTI_Implied_EmitsCorrectly()
    {
        using var writer = new Program6502Writer();
        writer.Write(Opcode.RTI);
        Assert.Equal([0x40], writer.ToBytes());
    }

    #endregion

    #region Program6502 Integration Tests

    [Fact]
    public void CreateWithBuiltIns_DefinesExpectedLabels()
    {
        // Build program using new object model
        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();
        var programLabels = program.GetLabels();

        // Verify key labels are defined with non-zero addresses
        var expectedLabels = new[]
        {
            "pal_col", "pal_bg", "pal_all", "pal_spr", "pal_clear",
            "ppu_on_all", "ppu_on_bg", "ppu_on_spr", "ppu_off",
            "vram_adr", "vram_write", "vram_put", "vram_fill",
            "oam_clear", "oam_size", "oam_hide_rest",
            "scroll", "delay", "nesclock", "initlib"
        };

        foreach (var label in expectedLabels)
        {
            Assert.True(programLabels.ContainsKey(label), $"Program6502 missing label: {label}");
            Assert.NotEqual(0, programLabels[label]);
        }
    }

    [Fact]
    public void CreateWithBuiltIns_ProducesValidProgram()
    {
        var program = Program6502.CreateWithBuiltIns();
        
        // Should have many blocks
        Assert.True(program.BlockCount > 40, $"Expected > 40 blocks, got {program.BlockCount}");
        
        // Should be able to resolve addresses without error
        program.ResolveAddresses();
        
        // Should be able to emit bytes without error
        var bytes = program.ToBytes();
        Assert.True(bytes.Length > 1000, $"Expected > 1000 bytes, got {bytes.Length}");
    }

    [Fact]
    public void CreateWithBuiltIns_HasForwardReferences()
    {
        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();
        var labels = program.GetLabels();

        // Forward references should be defined (with placeholder value 0)
        Assert.True(labels.ContainsKey("popa"));
        Assert.True(labels.ContainsKey("popax"));
        Assert.True(labels.ContainsKey("pusha"));
        Assert.True(labels.ContainsKey("pushax"));
        Assert.True(labels.ContainsKey("zerobss"));
        Assert.True(labels.ContainsKey("copydata"));
    }

    [Fact]
    public void AddFinalBuiltIns_SetsCorrectAddresses()
    {
        var program = Program6502.CreateWithBuiltIns();
        
        // Add final built-ins (these define popa, popax, etc.)
        program.AddFinalBuiltIns(totalSize: 0x85FE, locals: 0);
        program.ResolveAddresses();
        
        var labels = program.GetLabels();

        // Forward references should now have non-zero addresses
        Assert.NotEqual(0, labels["popa"]);
        Assert.NotEqual(0, labels["popax"]);
        Assert.NotEqual(0, labels["pusha"]);
        Assert.NotEqual(0, labels["pushax"]);
        Assert.NotEqual(0, labels["zerobss"]);
        Assert.NotEqual(0, labels["copydata"]);
    }

    [Fact]
    public void GetBuiltInLabels_ReturnsExpectedLabels()
    {
        var labels = Program6502.GetBuiltInLabels();

        // Should have all the key labels
        var expectedLabels = new[]
        {
            "pal_col", "pal_bg", "pal_all", "pal_spr", "pal_clear",
            "ppu_on_all", "ppu_on_bg", "ppu_on_spr", "ppu_off",
            "vram_adr", "vram_write", "vram_put", "vram_fill",
            "oam_clear", "oam_size", "oam_hide_rest",
            "scroll", "delay", "nesclock", "initlib"
        };

        foreach (var label in expectedLabels)
        {
            Assert.True(labels.ContainsKey(label), $"Missing label: {label}");
            Assert.NotEqual(0, labels[label]);
        }
    }

    [Fact]
    public void GetBuiltInSize_ReturnsPositiveSize()
    {
        var size = Program6502.GetBuiltInSize();
        
        // Built-ins should be a significant size
        Assert.True(size > 1000, $"Expected > 1000 bytes, got {size}");
    }

    [Fact]
    public void CreateWithBuiltIns_ProducesSameLabelsAsNESWriter()
    {
        // Build program using new object model
        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();
        var programLabels = program.GetLabels();

        // Build using NESWriter approach
        using var ms = new MemoryStream();
        using var writer = new NESWriter(ms, leaveOpen: true);
        writer.WriteBuiltIns();
        var writerLabels = writer.Labels;

        // Compare key labels that WriteBuiltIns defines
        var labelsToCheck = new[]
        {
            "pal_col", "pal_bg", "pal_all", "pal_spr", "pal_clear",
            "ppu_on_all", "ppu_on_bg", "ppu_on_spr", "ppu_off",
            "vram_adr", "vram_write", "vram_put", "vram_fill",
            "oam_clear", "oam_size", "oam_hide_rest",
            "scroll", "delay", "nesclock", "initlib"
        };

        foreach (var label in labelsToCheck)
        {
            Assert.True(writerLabels.ContainsKey(label), $"NESWriter missing label: {label}");
            Assert.True(programLabels.ContainsKey(label), $"Program6502 missing label: {label}");
            Assert.Equal(writerLabels[label], programLabels[label]);
        }
    }

    [Fact]
    public void CreateWithBuiltIns_ProducesSameBytesAsNESWriter()
    {
        // Build program using new object model
        var program = Program6502.CreateWithBuiltIns();
        var programBytes = program.ToBytes();

        // Build using NESWriter approach
        using var ms = new MemoryStream();
        using var writer = new NESWriter(ms, leaveOpen: true);
        writer.WriteBuiltIns();
        var writerBytes = ms.ToArray();

        Assert.Equal(writerBytes.Length, programBytes.Length);
        Assert.Equal(writerBytes, programBytes);
    }

    #endregion
}
