using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Tests for the 6502 object model - Phase 1 implementation
/// </summary>
public class ObjectModelTests
{
    #region OpcodeTable Tests

    [Theory]
    [InlineData(Opcode.LDA, AddressMode.Immediate, 0xA9)]
    [InlineData(Opcode.LDA, AddressMode.ZeroPage, 0xA5)]
    [InlineData(Opcode.LDA, AddressMode.Absolute, 0xAD)]
    [InlineData(Opcode.STA, AddressMode.ZeroPage, 0x85)]
    [InlineData(Opcode.STA, AddressMode.Absolute, 0x8D)]
    [InlineData(Opcode.JMP, AddressMode.Absolute, 0x4C)]
    [InlineData(Opcode.JSR, AddressMode.Absolute, 0x20)]
    [InlineData(Opcode.BNE, AddressMode.Relative, 0xD0)]
    [InlineData(Opcode.BEQ, AddressMode.Relative, 0xF0)]
    [InlineData(Opcode.RTS, AddressMode.Implied, 0x60)]
    [InlineData(Opcode.INX, AddressMode.Implied, 0xE8)]
    [InlineData(Opcode.DEX, AddressMode.Implied, 0xCA)]
    public void OpcodeTable_Encode_ReturnsCorrectByte(Opcode opcode, AddressMode mode, byte expected)
    {
        byte result = OpcodeTable.Encode(opcode, mode);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0xA9, Opcode.LDA, AddressMode.Immediate)]
    [InlineData(0xA5, Opcode.LDA, AddressMode.ZeroPage)]
    [InlineData(0xAD, Opcode.LDA, AddressMode.Absolute)]
    [InlineData(0x85, Opcode.STA, AddressMode.ZeroPage)]
    [InlineData(0x4C, Opcode.JMP, AddressMode.Absolute)]
    [InlineData(0x20, Opcode.JSR, AddressMode.Absolute)]
    [InlineData(0xD0, Opcode.BNE, AddressMode.Relative)]
    [InlineData(0x60, Opcode.RTS, AddressMode.Implied)]
    public void OpcodeTable_Decode_ReturnsCorrectOpcodeAndMode(byte encoding, Opcode expectedOpcode, AddressMode expectedMode)
    {
        var (opcode, mode) = OpcodeTable.Decode(encoding);
        Assert.Equal(expectedOpcode, opcode);
        Assert.Equal(expectedMode, mode);
    }

    [Fact]
    public void OpcodeTable_InvalidMode_ThrowsException()
    {
        Assert.Throws<InvalidOpcodeAddressModeException>(() => OpcodeTable.Encode(Opcode.LDA, AddressMode.Implied));
    }

    [Fact]
    public void OpcodeTable_UnknownOpcode_ThrowsException()
    {
        Assert.Throws<UnknownOpcodeException>(() => OpcodeTable.Decode(0xFF));
    }

    [Theory]
    [InlineData(AddressMode.Implied, 1)]
    [InlineData(AddressMode.Accumulator, 1)]
    [InlineData(AddressMode.Immediate, 2)]
    [InlineData(AddressMode.ZeroPage, 2)]
    [InlineData(AddressMode.Relative, 2)]
    [InlineData(AddressMode.Absolute, 3)]
    [InlineData(AddressMode.AbsoluteX, 3)]
    public void OpcodeTable_GetInstructionSize_ReturnsCorrectSize(AddressMode mode, int expectedSize)
    {
        Assert.Equal(expectedSize, OpcodeTable.GetInstructionSize(mode));
    }

    #endregion

    #region Instruction Tests

    [Fact]
    public void Instruction_Immediate_EncodesCorrectly()
    {
        var instr = LDA(0x42);
        var labels = new LabelTable();

        var bytes = instr.ToBytes(0x8000, labels);

        Assert.Equal([0xA9, 0x42], bytes);
    }

    [Fact]
    public void Instruction_ZeroPage_EncodesCorrectly()
    {
        var instr = STA_zpg(0x17);
        var labels = new LabelTable();

        var bytes = instr.ToBytes(0x8000, labels);

        Assert.Equal([0x85, 0x17], bytes);
    }

    [Fact]
    public void Instruction_Absolute_EncodesLittleEndian()
    {
        var instr = JMP(0x823E);
        var labels = new LabelTable();

        var bytes = instr.ToBytes(0x8000, labels);

        Assert.Equal([0x4C, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Instruction_JSR_EncodesCorrectly()
    {
        var instr = JSR(0x823E);
        var labels = new LabelTable();

        var bytes = instr.ToBytes(0x8000, labels);

        Assert.Equal([0x20, 0x3E, 0x82], bytes);
    }

    [Fact]
    public void Instruction_Implied_EncodesAsSingleByte()
    {
        var instr = RTS();
        var labels = new LabelTable();

        var bytes = instr.ToBytes(0x8000, labels);

        Assert.Equal([0x60], bytes);
    }

    [Fact]
    public void Instruction_Size_ReturnsCorrectValue()
    {
        Assert.Equal(1, RTS().Size);
        Assert.Equal(2, LDA(0x42).Size);
        Assert.Equal(2, STA_zpg(0x17).Size);
        Assert.Equal(3, JMP(0x8000).Size);
        Assert.Equal(3, JSR(0x8000).Size);
    }

    #endregion

    #region LabelTable Tests

    [Fact]
    public void LabelTable_DefineAndResolve_Works()
    {
        var labels = new LabelTable();
        labels.Define("loop", 0x8010);

        Assert.True(labels.IsDefined("loop"));
        Assert.Equal(0x8010, labels.Resolve("loop"));
    }

    [Fact]
    public void LabelTable_DuplicateLabel_ThrowsException()
    {
        var labels = new LabelTable();
        labels.Define("loop", 0x8010);

        Assert.Throws<DuplicateLabelException>(() => labels.Define("loop", 0x8020));
    }

    [Fact]
    public void LabelTable_UnresolvedLabel_ThrowsException()
    {
        var labels = new LabelTable();

        Assert.Throws<UnresolvedLabelException>(() => labels.Resolve("undefined"));
    }

    [Fact]
    public void LabelTable_TryResolve_ReturnsFalseForUndefined()
    {
        var labels = new LabelTable();

        Assert.False(labels.TryResolve("undefined", out _));
        Assert.Contains("undefined", labels.UnresolvedReferences);
    }

    [Fact]
    public void LabelTable_DefineOrUpdate_DoesNotThrow()
    {
        var labels = new LabelTable();
        labels.Define("loop", 0x8010);

        labels.DefineOrUpdate("loop", 0x8020); // Should not throw

        Assert.Equal(0x8020, labels.Resolve("loop"));
    }

    #endregion

    #region Block Tests

    [Fact]
    public void Block_Emit_AddsInstructions()
    {
        var block = new Block("test");
        block.Emit(LDA(0x00))
             .Emit(STA_zpg(0x17))
             .Emit(RTS());

        Assert.Equal(3, block.Count);
        Assert.Equal(5, block.Size); // LDA imm = 2, STA zpg = 2, RTS = 1 => 5
    }

    [Fact]
    public void Block_RemoveLast_RemovesInstructions()
    {
        var block = new Block();
        block.Emit(LDA(0x00))
             .Emit(STA_zpg(0x17))
             .Emit(RTS());

        block.RemoveLast();

        Assert.Equal(2, block.Count);
        Assert.Equal(Opcode.STA, block[1].Opcode);
    }

    [Fact]
    public void Block_RemoveLast_MultipleCount()
    {
        var block = new Block();
        block.Emit(LDA(0x00))
             .Emit(LDA(0x01))
             .Emit(LDA(0x02));

        block.RemoveLast(2);

        Assert.Equal(1, block.Count);
        Assert.Equal(0x00, ((ImmediateOperand)block[0].Operand!).Value);
    }

    [Fact]
    public void Block_Insert_InsertsAtPosition()
    {
        var block = new Block();
        block.Emit(LDA(0x00))
             .Emit(RTS());

        block.Insert(1, STA_zpg(0x17));

        Assert.Equal(3, block.Count);
        Assert.Equal(Opcode.STA, block[1].Opcode);
        Assert.Equal(Opcode.RTS, block[2].Opcode);
    }

    [Fact]
    public void Block_GetOffsetAt_CalculatesCorrectly()
    {
        var block = new Block();
        block.Emit(LDA(0x00))    // 2 bytes
             .Emit(STA_zpg(0x17))// 2 bytes
             .Emit(RTS());       // 1 byte

        Assert.Equal(0, block.GetOffsetAt(0));
        Assert.Equal(2, block.GetOffsetAt(1));
        Assert.Equal(4, block.GetOffsetAt(2));
    }

    [Fact]
    public void Block_WithLabels_StoresLabels()
    {
        var block = new Block();
        block.Emit(LDA(0x00), "start");
        block.Emit(RTS(), "end");

        Assert.Equal("start", block.GetLabelAt(0));
        Assert.Equal("end", block.GetLabelAt(1));
    }

    #endregion

    #region Program6502 Tests

    [Fact]
    public void Program6502_CreateBlock_AddsBlockToProgram()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };
        var block = program.CreateBlock("main");

        Assert.Single(program.Blocks);
        Assert.Equal("main", program.Blocks[0].Label);
    }

    [Fact]
    public void Program6502_ResolveAddresses_SetsLabelAddresses()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(LDA(0x00))     // 2 bytes at $8000
            .Emit(RTS());        // 1 byte at $8002

        var sub = program.CreateBlock("subroutine");
        sub.Emit(INX())          // 1 byte at $8003
           .Emit(RTS());         // 1 byte at $8004

        program.ResolveAddresses();

        Assert.Equal(0x8000, program.Labels.Resolve("main"));
        Assert.Equal(0x8003, program.Labels.Resolve("subroutine"));
    }

    [Fact]
    public void Program6502_ToBytes_EmitsCorrectMachineCode()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(LDA(0x42))
            .Emit(RTS());

        var bytes = program.ToBytes();

        Assert.Equal([0xA9, 0x42, 0x60], bytes);
    }

    [Fact]
    public void Program6502_LabelResolution_Works()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(JSR("subroutine"))  // 3 bytes at $8000
            .Emit(RTS());             // 1 byte at $8003

        var sub = program.CreateBlock("subroutine");
        sub.Emit(INX())               // 1 byte at $8004
           .Emit(RTS());              // 1 byte at $8005

        var bytes = program.ToBytes();

        // JSR $8004 (subroutine starts at $8004)
        Assert.Equal(0x20, bytes[0]);  // JSR
        Assert.Equal(0x04, bytes[1]);  // low byte of $8004
        Assert.Equal(0x80, bytes[2]);  // high byte of $8004
    }

    [Fact]
    public void Program6502_BranchWithLabel_CalculatesOffset()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(LDA(0x00), "loop")   // $8000
            .Emit(INX())               // $8002
            .Emit(BNE("loop"));        // $8003, target=$8000, offset = $8000 - $8005 = -5 = 0xFB

        var bytes = program.ToBytes();

        Assert.Equal(0xD0, bytes[3]);  // BNE
        Assert.Equal(0xFB, bytes[4]);  // -5 as signed byte
    }

    [Fact]
    public void Program6502_TotalSize_CalculatesCorrectly()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var block = program.CreateBlock();
        block.Emit(LDA(0x00))   // 2
             .Emit(JSR(0x8000)) // 3
             .Emit(RTS());      // 1

        Assert.Equal(6, program.TotalSize);
    }

    [Fact]
    public void Program6502_FindInstructionAt_FindsCorrectInstruction()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var block = program.CreateBlock();
        block.Emit(LDA(0x00))   // at $8000
             .Emit(STA_zpg(0x17)) // at $8002
             .Emit(RTS());        // at $8004

        var instr = program.FindInstructionAt(0x8002);

        Assert.NotNull(instr);
        Assert.Equal(Opcode.STA, instr.Opcode);
    }

    [Fact]
    public void Program6502_Validate_FindsUnresolvedLabels()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var block = program.CreateBlock();
        block.Emit(JSR("undefined_sub"));

        var unresolved = program.Validate();

        Assert.Contains("undefined_sub", unresolved);
    }

    [Fact]
    public void Program6502_FindReferencesTo_FindsAllReferences()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(JSR("helper"))
            .Emit(JMP("helper"));

        program.CreateBlock("helper")
               .Emit(RTS());

        var refs = program.FindReferencesTo("helper").ToList();

        Assert.Equal(2, refs.Count);
    }

    [Fact]
    public void Program6502_GetInstructionAddress_ReturnsCorrectAddress()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var block = program.CreateBlock();
        block.Emit(LDA(0x00))    // $8000
             .Emit(STA_zpg(0x17)) // $8002
             .Emit(RTS());        // $8004

        Assert.Equal(0x8000, program.GetInstructionAddress(block, 0));
        Assert.Equal(0x8002, program.GetInstructionAddress(block, 1));
        Assert.Equal(0x8004, program.GetInstructionAddress(block, 2));
    }

    #endregion

    #region Operand Tests

    [Fact]
    public void AbsoluteOperand_ToBytes_LittleEndian()
    {
        var operand = new AbsoluteOperand(0x1234);
        var labels = new LabelTable();

        var bytes = operand.ToBytes(0x8000, labels);

        Assert.Equal([0x34, 0x12], bytes);
    }

    [Fact]
    public void LabelOperand_ToBytes_ResolvesLabel()
    {
        var labels = new LabelTable();
        labels.Define("target", 0x8100);

        var operand = new LabelOperand("target", OperandSize.Word);
        var bytes = operand.ToBytes(0x8000, labels);

        Assert.Equal([0x00, 0x81], bytes);
    }

    [Fact]
    public void RelativeOperand_ToBytes_CalculatesOffset()
    {
        var labels = new LabelTable();
        labels.Define("loop", 0x8000);

        var operand = new RelativeOperand("loop");
        // Instruction at $8005, so offset = $8000 - ($8005 + 2) = -7 = 0xF9
        var bytes = operand.ToBytes(0x8005, labels);

        Assert.Equal([0xF9], bytes);
    }

    [Fact]
    public void RelativeOperand_OutOfRange_ThrowsException()
    {
        var labels = new LabelTable();
        labels.Define("faraway", 0x8200);

        var operand = new RelativeOperand("faraway");

        Assert.Throws<BranchOutOfRangeException>(() => operand.ToBytes(0x8000, labels));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Integration_SimpleProgram_ProducesCorrectOutput()
    {
        // Replicates a simple "pal_col" call pattern from the existing codebase
        var program = new Program6502 { BaseAddress = 0x8000 };

        // Define external subroutine label first
        program.DefineExternalLabel("_pal_col", 0x823E);

        var main = program.CreateBlock("main");
        main.Emit(LDA(0x02))           // palette index
            .Emit(JSR("_pal_col"))
            .Emit(JMP("forever"), "forever");

        var bytes = program.ToBytes();

        // LDA #$02 = A9 02
        // JSR $823E = 20 3E 82
        // JMP $8005 = 4C 05 80 (address of "forever" label)
        Assert.Equal(
            [0xA9, 0x02, 0x20, 0x3E, 0x82, 0x4C, 0x05, 0x80],
            bytes);
    }

    [Fact]
    public void Integration_Disassemble_ProducesReadableOutput()
    {
        var program = new Program6502 { BaseAddress = 0x8000 };

        var main = program.CreateBlock("main");
        main.Emit(LDA(0x42))
            .Emit(RTS());

        var disasm = program.Disassemble();

        Assert.Contains("main:", disasm);
        Assert.Contains("$8000", disasm);
        Assert.Contains("LDA", disasm);
    }

    #endregion
}
