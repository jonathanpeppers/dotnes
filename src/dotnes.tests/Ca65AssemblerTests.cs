using dotnes;
using dotnes.ObjectModel;

namespace dotnes.tests;

public class Ca65AssemblerTests
{
    #region Expression Evaluator

    [Theory]
    [InlineData("$0500", 0x0500)]
    [InlineData("$FF", 0xFF)]
    [InlineData("$c000", 0xC000)]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("255", 255)]
    [InlineData("%10101010", 0xAA)]
    [InlineData("%00001111", 0x0F)]
    public void Expression_Literals(string expr, int expected)
    {
        var result = Ca65Expression.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("$0500 + 3", 0x0503)]
    [InlineData("$10 * 5", 0x50)]
    [InlineData("$100 - $10", 0xF0)]
    [InlineData("$FF & $0F", 0x0F)]
    [InlineData("$0F | $F0", 0xFF)]
    [InlineData("$FF ^ $0F", 0xF0)]
    [InlineData("1 << 4", 16)]
    [InlineData("$80 >> 3", 16)]
    [InlineData("1 && 1", 1)]
    [InlineData("1 && 0", 0)]
    [InlineData("0 && 1", 0)]
    [InlineData("1 || 0", 1)]
    [InlineData("0 || 0", 0)]
    [InlineData("0 || 1", 1)]
    [InlineData("!0", 1)]
    [InlineData("!1", 0)]
    [InlineData("!42", 0)]
    public void Expression_Arithmetic(string expr, int expected)
    {
        var result = Ca65Expression.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("<($c000)", 0x00)]
    [InlineData(">($c000)", 0xC0)]
    [InlineData("<($1234)", 0x34)]
    [InlineData(">($1234)", 0x12)]
    [InlineData(".lobyte($ABCD)", 0xCD)]
    [InlineData(".hibyte($ABCD)", 0xAB)]
    [InlineData(".lobyte($0537)+5", 0x3C)]
    [InlineData(".lobyte($0500)+11", 0x0B)]
    public void Expression_LoByte_HiByte(string expr, int expected)
    {
        var result = Ca65Expression.Evaluate(expr, _ => null);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Expression_SymbolLookup()
    {
        int? lookup(string name) => name switch
        {
            "FT_BASE_ADR" => 0x0500,
            "FT_ENVELOPES_ALL" => 11,
            "FT_ENV_STRUCT_SIZE" => 5,
            _ => null
        };

        Assert.Equal(0x0500, Ca65Expression.Evaluate("FT_BASE_ADR", lookup));
        Assert.Equal(0x0505, Ca65Expression.Evaluate("FT_BASE_ADR + FT_ENV_STRUCT_SIZE", lookup));
        Assert.Equal(0x0500 + 0 * 11, Ca65Expression.Evaluate("FT_BASE_ADR + 0 * FT_ENVELOPES_ALL", lookup));
        Assert.Equal(0x0500 + 2 * 11, Ca65Expression.Evaluate("FT_BASE_ADR + 2 * FT_ENVELOPES_ALL", lookup));
    }

    [Fact]
    public void Expression_ComplexFamiTone()
    {
        // FT_CHANNELS = FT_ENVELOPES + FT_ENVELOPES_ALL * FT_ENV_STRUCT_SIZE
        // where FT_ENVELOPES = $0500, FT_ENVELOPES_ALL = 11, FT_ENV_STRUCT_SIZE = 5
        int? lookup(string name) => name switch
        {
            "FT_ENVELOPES" => 0x0500,
            "FT_ENVELOPES_ALL" => 11,
            "FT_ENV_STRUCT_SIZE" => 5,
            _ => null
        };

        var result = Ca65Expression.Evaluate("FT_ENVELOPES + FT_ENVELOPES_ALL * FT_ENV_STRUCT_SIZE", lookup);
        Assert.Equal(0x0500 + 11 * 5, result);
    }

    [Fact]
    public void Expression_Unresolved_ReturnsNull()
    {
        var result = Ca65Expression.TryEvaluate("UNKNOWN_SYMBOL".AsSpan(), _ => null);
        Assert.Null(result);
    }

    #endregion

    #region Basic Assembly

    [Fact]
    public void Assemble_ImpliedInstructions()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
test_sub:
    clc
    rts
");
        Assert.Single(blocks);
        Assert.Equal("test_sub", blocks[0].Label);
        Assert.Equal(2, blocks[0].Count); // CLC + RTS
    }

    [Fact]
    public void Assemble_ImmediateAndZeroPage()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
my_func:
    lda #$42
    sta $17
    rts
");
        Assert.Single(blocks);
        Assert.Equal(3, blocks[0].Count);

        // Verify instruction types
        Assert.Equal(Opcode.LDA, blocks[0][0].Opcode);
        Assert.Equal(AddressMode.Immediate, blocks[0][0].Mode);
        Assert.Equal(Opcode.STA, blocks[0][1].Opcode);
        Assert.Equal(AddressMode.ZeroPage, blocks[0][1].Mode);
        Assert.Equal(Opcode.RTS, blocks[0][2].Opcode);
    }

    [Fact]
    public void Assemble_AbsoluteAddressing()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
init:
    lda #$0f
    sta $4015
    rts
");
        Assert.Single(blocks);
        Assert.Equal(Opcode.STA, blocks[0][1].Opcode);
        Assert.Equal(AddressMode.Absolute, blocks[0][1].Mode);
    }

    [Fact]
    public void Assemble_IndexedModes()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
test:
    sta $0500,x
    lda ($00),y
    rts
");
        Assert.Single(blocks);
        Assert.Equal(AddressMode.AbsoluteX, blocks[0][0].Mode);
        Assert.Equal(AddressMode.IndirectIndexed, blocks[0][1].Mode);
    }

    [Fact]
    public void Assemble_BranchWithLocalLabel()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
loop_test:
    ldx #$0a
@loop:
    dex
    bne @loop
    rts
");
        Assert.Single(blocks);
        Assert.Equal(4, blocks[0].Count); // LDX, DEX, BNE, RTS
        Assert.Equal(Opcode.BNE, blocks[0][2].Opcode);
        Assert.Equal(AddressMode.Relative, blocks[0][2].Mode);
    }

    #endregion

    #region Constants and Defines

    [Fact]
    public void Assemble_ConstantAssignment()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
APU_STATUS = $4015

.segment ""CODE""
init:
    lda #$0f
    sta APU_STATUS
    rts
");
        Assert.Single(blocks);
        Assert.Equal(Opcode.STA, blocks[0][1].Opcode);
        Assert.Equal(AddressMode.Absolute, blocks[0][1].Mode);
    }

    [Fact]
    public void Assemble_Define()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.define FT_SFX_ENABLE 1

.segment ""CODE""
.if(FT_SFX_ENABLE)
test:
    nop
    rts
.endif
");
        Assert.Single(blocks);
        Assert.Equal(2, blocks[0].Count);
    }

    #endregion

    #region Conditional Assembly

    [Fact]
    public void Assemble_IfTrue()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.define FEATURE 1

.segment ""CODE""
test:
.if(FEATURE)
    lda #$01
.else
    lda #$00
.endif
    rts
");
        Assert.Single(blocks);
        Assert.Equal(2, blocks[0].Count); // LDA #$01 + RTS
        Assert.Equal(Opcode.LDA, blocks[0][0].Opcode);
    }

    [Fact]
    public void Assemble_IfFalse()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.define FEATURE 0

.segment ""CODE""
test:
.if(FEATURE)
    lda #$01
.else
    lda #$00
.endif
    rts
");
        Assert.Single(blocks);
        Assert.Equal(2, blocks[0].Count); // LDA #$00 + RTS
    }

    [Fact]
    public void Assemble_NestedConditionals()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.define OUTER 1
.define INNER 0

.segment ""CODE""
test:
.if(OUTER)
    nop
.if(INNER)
    brk
.endif
.endif
    rts
");
        Assert.Single(blocks);
        Assert.Equal(2, blocks[0].Count); // NOP + RTS (INNER is false, no BRK)
    }

    #endregion

    #region Import/Export

    [Fact]
    public void Assemble_ExportAndImport()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
    .import popa
    .export _my_func, _my_other_func

.segment ""CODE""
_my_func:
    nop
    rts
_my_other_func:
    nop
    rts
");
        Assert.Contains("popa", asm.Imports);
        Assert.Contains("_my_func", asm.Exports);
        Assert.Contains("_my_other_func", asm.Exports);
    }

    #endregion

    #region Zero Page Segment

    [Fact]
    public void Assemble_ZeroPageAllocation()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""ZEROPAGE""
FT_TEMP: .res 3

.segment ""CODE""
test:
    sta FT_TEMP
    rts
");
        // FT_TEMP should be at zero page offset 0
        Assert.Single(blocks);
        Assert.Equal(Opcode.STA, blocks[0][0].Opcode);
        Assert.Equal(AddressMode.ZeroPage, blocks[0][0].Mode);
    }

    #endregion

    #region Data Blocks

    [Fact]
    public void Assemble_ByteData()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""RODATA""
my_data:
    .byte $c0, $00, $00
");
        Assert.Single(blocks);
        Assert.True(blocks[0].IsDataBlock);
        Assert.Equal("my_data", blocks[0].Label);
        Assert.Equal(3, blocks[0].RawData!.Length);
        Assert.Equal(0xC0, blocks[0].RawData![0]);
        Assert.Equal(0x00, blocks[0].RawData![1]);
    }

    [Fact]
    public void Assemble_WordDataWithLabels()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""RODATA""
my_table:
    .word @entry1, @entry2

@entry1:
    .byte $01, $02, $03

@entry2:
    .byte $04, $05, $06
");
        // All data in one block with internal labels
        Assert.Single(blocks);
        Assert.True(blocks[0].IsDataBlock);
        Assert.Equal("my_table", blocks[0].Label);
        // 4 bytes (2 words) + 3 bytes + 3 bytes = 10 total
        Assert.Equal(10, blocks[0].RawData!.Length);
        // 2 relocations for the .word entries
        Assert.NotNull(blocks[0].Relocations);
        Assert.Equal(2, blocks[0].Relocations!.Count);
        // Internal labels for @entry1 and @entry2
        Assert.NotNull(blocks[0].InternalLabels);
        Assert.True(blocks[0].InternalLabels!.ContainsKey("my_table:@entry1"));
        Assert.Equal(4, blocks[0].InternalLabels!["my_table:@entry1"]); // offset 4
        Assert.True(blocks[0].InternalLabels!.ContainsKey("my_table:@entry2"));
        Assert.Equal(7, blocks[0].InternalLabels!["my_table:@entry2"]); // offset 7
    }

    #endregion

    #region Label Aliases (FamiTone2 pattern)

    [Fact]
    public void Assemble_LabelAlias()
    {
        // FamiTone2 uses patterns like: _famitone_init=FamiToneInit
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
FamiToneInit:
    lda #$0f
    sta $4015
    rts

_famitone_init=FamiToneInit
");
        Assert.Single(blocks);
        Assert.Equal("FamiToneInit", blocks[0].Label);
    }

    #endregion

    #region Accumulator Mode

    [Fact]
    public void Assemble_AccumulatorMode()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
.segment ""CODE""
test:
    asl a
    lsr a
    rts
");
        Assert.Single(blocks);
        Assert.Equal(AddressMode.Accumulator, blocks[0][0].Mode);
        Assert.Equal(AddressMode.Accumulator, blocks[0][1].Mode);
    }

    #endregion

    #region Immediate with lobyte/hibyte

    [Fact]
    public void Assemble_ImmediateLobyte()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
FT_CHANNELS = $0537

.segment ""CODE""
test:
    ldx #.lobyte(FT_CHANNELS)
    rts
");
        Assert.Single(blocks);
        Assert.Equal(Opcode.LDX, blocks[0][0].Opcode);
        Assert.Equal(AddressMode.Immediate, blocks[0][0].Mode);
        // $0537 lobyte = $37
        var bytes = blocks[0][0].Operand!.ToBytes(0, new LabelTable());
        Assert.Equal(0x37, bytes[0]);
    }

    #endregion

    #region FamiTone2 Patterns

    [Fact]
    public void Assemble_FamiTone2_BasicStructure()
    {
        // Test a simplified version of FamiTone2's init routine
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(@"
    .import popa

FT_BASE_ADR = $0500
FT_SONG_LIST_L = FT_BASE_ADR + 1
FT_SONG_LIST_H = FT_BASE_ADR + 2

.segment ""ZEROPAGE""
FT_TEMP: .res 3

.segment ""CODE""

    .export _famitone_init

FamiToneInit:
    sta FT_SONG_LIST_L
    stx FT_SONG_LIST_H
    sta <FT_TEMP
    stx <FT_TEMP+1
    lda #$0f
    sta $4015
    rts

_famitone_init=FamiToneInit
");
        Assert.Contains("popa", asm.Imports);
        Assert.Contains("_famitone_init", asm.Exports);

        // Should have at least one code block
        Assert.NotEmpty(blocks);
        var codeBlock = blocks.First(b => !b.IsDataBlock);
        Assert.Equal("FamiToneInit", codeBlock.Label);
        Assert.True(codeBlock.Count >= 5); // At least the instructions we wrote
    }

    #endregion

    #region FamiTone2 Real Assembly

    [Fact]
    public void Assemble_FamiTone2_RealFile()
    {
        var path = Path.Combine("Data", "fami", "famitone2.s");
        if (!File.Exists(path))
        {
            // Skip if test data not available
            return;
        }

        var asm = new Ca65Assembler();
        using var reader = new StreamReader(path);
        var blocks = asm.Assemble(reader);

        // FamiTone2 should produce code blocks and data blocks
        Assert.NotEmpty(blocks);

        // Should export these symbols
        Assert.Contains("_famitone_init", asm.Exports);
        Assert.Contains("_famitone_update", asm.Exports);
        Assert.Contains("_music_play", asm.Exports);
        Assert.Contains("_music_stop", asm.Exports);
        Assert.Contains("_music_pause", asm.Exports);
        Assert.Contains("_sfx_init", asm.Exports);
        Assert.Contains("_sfx_play", asm.Exports);

        // Should import popa
        Assert.Contains("popa", asm.Imports);

        // Should have code blocks (the various subroutines)
        var codeBlocks = blocks.Where(b => !b.IsDataBlock).ToList();
        Assert.NotEmpty(codeBlocks);

        // Total code size should be substantial (FamiTone2 is ~500+ bytes of code)
        int totalCodeSize = codeBlocks.Sum(b => b.ByteSize);
        Assert.True(totalCodeSize > 200, $"Expected > 200 bytes of code, got {totalCodeSize}");

        // Should have data blocks (note tables, dummy envelope)
        var dataBlocks = blocks.Where(b => b.IsDataBlock).ToList();
        Assert.NotEmpty(dataBlocks);
    }

    [Fact]
    public void Assemble_MusicData_RealFile()
    {
        var path = Path.Combine("Data", "fami", "music_dangerstreets.s");
        if (!File.Exists(path))
            return;

        var asm = new Ca65Assembler();
        using var reader = new StreamReader(path);
        var blocks = asm.Assemble(reader);

        Assert.NotEmpty(blocks);
        Assert.Contains("_danger_streets_music_data", asm.Exports);

        // Music data should have relocations (internal .word @label references)
        var blocksWithRelocations = blocks.Where(b => b.Relocations != null && b.Relocations.Count > 0).ToList();
        Assert.NotEmpty(blocksWithRelocations);
    }

    [Fact]
    public void Assemble_DemoSounds_RealFile()
    {
        var path = Path.Combine("Data", "fami", "demosounds.s");
        if (!File.Exists(path))
            return;

        var asm = new Ca65Assembler();
        using var reader = new StreamReader(path);
        var blocks = asm.Assemble(reader);

        Assert.NotEmpty(blocks);
        Assert.Contains("_demo_sounds", asm.Exports);
    }

    #endregion

    [Fact]
    public void ZeroPageDerivedConstants_ResolveInInstructions()
    {
        var source = @"
.segment ""ZEROPAGE""
FT_TEMP:    .res 3

.segment ""CODE""
FT_TEMP_PTR     = FT_TEMP
FT_TEMP_PTR_L   = FT_TEMP_PTR+0
FT_TEMP_PTR_H   = FT_TEMP_PTR+1

_test:
    sta <FT_TEMP_PTR_L
    stx <FT_TEMP_PTR_H
    rts
";
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(source);

        // Should produce one code block
        Assert.Single(blocks);
        var block = blocks[0];
        Assert.Equal("_test", block.Label);

        // sta <FT_TEMP_PTR_L should resolve to STA $00 (zero page, value 0)
        var inst0 = block[0];
        Assert.Equal(Opcode.STA, inst0.Opcode);
        Assert.Equal(AddressMode.ZeroPage, inst0.Mode);

        // stx <FT_TEMP_PTR_H should resolve to STX $01 (zero page, value 1)
        var inst1 = block[1];
        Assert.Equal(Opcode.STX, inst1.Opcode);
        Assert.Equal(AddressMode.ZeroPage, inst1.Mode);
    }

    [Fact]
    public void Assemble_FamiTone2_ZeroPageConstantsResolve()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(File.ReadAllText(Path.Combine("Data", "fami", "famitone2.s")));

        // famitone2.s should produce 4 blocks: 2 code, 2 data
        Assert.Equal(4, blocks.Count);

        // Zero-page derived constants should all be resolved
        Assert.True(asm.Constants.ContainsKey("FT_TEMP"));
        Assert.True(asm.Constants.ContainsKey("FT_TEMP_PTR"));
        Assert.True(asm.Constants.ContainsKey("FT_TEMP_PTR_L"));
        Assert.True(asm.Constants.ContainsKey("FT_TEMP_PTR_H"));
        Assert.Equal(0, asm.Constants["FT_TEMP_PTR_L"]);
        Assert.Equal(1, asm.Constants["FT_TEMP_PTR_H"]);

        // Code labels should NOT appear in constants (they're resolved via LabelTable)
        Assert.False(asm.Constants.ContainsKey("FamiToneInit"));
        Assert.False(asm.Constants.ContainsKey("FamiToneUpdate"));
    }

    [Fact]
    public void Assemble_MusicData_StartsAtLabel()
    {
        var asm = new Ca65Assembler();
        var blocks = asm.Assemble(File.ReadAllText(Path.Combine("Data", "fami", "music_dangerstreets.s")));

        Assert.Single(blocks);
        var block = blocks[0];
        Assert.Equal("_danger_streets_music_data", block.Label);
        Assert.True(block.IsDataBlock);

        // First byte should be 0x01 (1 song)
        Assert.Equal(0x01, block.RawData![0]);
    }
}
