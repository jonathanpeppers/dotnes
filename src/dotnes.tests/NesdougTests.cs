using dotnes;
using dotnes.ObjectModel;
using Xunit.Abstractions;

namespace dotnes.tests;

/// <summary>
/// Tests for the nesdoug helper APIs added in NESLib.
/// Each test calls one helper from C# and verifies the corresponding
/// 6502 subroutine block was emitted into the ROM.
/// </summary>
public class NesdougTests : RoslynTests
{
    public NesdougTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void SetScrollX_EmitsBlockAndJsr()
    {
        using var transpiler = BuildProgram(
            """
            ushort sx = 0;
            set_scroll_x(sx);
            while (true) ;
            """, out var program);

        var mainBlock = program.GetMainBlock();
        Assert.NotEmpty(mainBlock);
        // Main should contain JSR (0x20) to set_scroll_x somewhere
        Assert.Contains((byte)0x20, mainBlock);

        var sub = program.GetBlock("set_scroll_x");
        Assert.NotNull(sub);
        // 9 instructions: sta, txa, and, sta, lda, and, ora, sta, rts (no asl_a in this variant)
        Assert.Equal(9, sub.Count);
    }

    [Fact]
    public void SetScrollY_EmitsBlock()
    {
        using var transpiler = BuildProgram(
            """
            ushort sy = 0;
            set_scroll_y(sy);
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("set_scroll_y");
        Assert.NotNull(sub);
        // 10 instructions including the ASL_A
        Assert.Equal(10, sub.Count);
    }

    [Fact]
    public void GetPpuAddr_EmitsBlock()
    {
        using var transpiler = BuildProgram(
            """
            byte x = 96, y = 16;
            ushort addr = get_ppu_addr(0, x, y);
            vram_adr(addr);
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("get_ppu_addr");
        Assert.NotNull(sub);
        // Two popa calls => block must reference popa label
        var popa = program.GetBlock("popa");
        Assert.NotNull(popa);
    }

    [Fact]
    public void ColorEmphasis_EmitsExpectedHex()
    {
        using var transpiler = BuildProgram(
            """
            color_emphasis(ColorEmphasis.Red | ColorEmphasis.Blue);
            while (true) ;
            """, out var program);

        var sub = Assert.IsType<Block>(program.GetBlock("color_emphasis"));
        Assert.Equal(
            new byte[] { 0x29, 0xE0, 0x85, 0x17, 0xA5, 0x12, 0x29, 0x1F, 0x05, 0x17, 0x85, 0x12, 0x60 },
            GetBlockBytes(program, sub));
        AssertCalls(program, "color_emphasis");
    }

    [Fact]
    public void ColorEmphasis_AcceptsByteConstant()
    {
        using var transpiler = BuildProgram(
            """
            color_emphasis(COL_EMP_RED | COL_EMP_BLUE);
            while (true) ;
            """, out var program);

        Assert.Contains(
            "A9A0" + Convert.ToHexString(CallBytes(program, "color_emphasis")),
            Convert.ToHexString(program.GetMainBlock()));
    }

    [Fact]
    public void PalFadeTo_EmitsExpectedHex()
    {
        using var transpiler = BuildProgram(
            """
            pal_fade_to(1, 3);
            while (true) ;
            """, out var program);

        var sub = Assert.IsType<Block>(program.GetBlock("pal_fade_to"));
        ushort checkEqual = program.GetInstructionAddress(sub, 18);
        byte[] expected =
        [
            0x85, 0x43,
            .. CallBytes(program, "popa"),
            0x85, 0x44,
            0x4C, (byte)checkEqual, (byte)(checkEqual >> 8),
            0xA9, 0x04,
            .. CallBytes(program, "delay"),
            0xA5, 0x44, 0xC5, 0x43, 0xB0, 0x0B,
            0x18, 0x69, 0x01, 0x85, 0x44,
            .. CallBytes(program, "pal_bright"),
            0x4C, (byte)checkEqual, (byte)(checkEqual >> 8),
            0x38, 0xE9, 0x01, 0x85, 0x44,
            .. CallBytes(program, "pal_bright"),
            0xA5, 0x44, 0xC5, 0x43, 0xD0, 0xDC,
            .. CallBytes(program, "ppu_wait_nmi"),
            0x60
        ];
        Assert.Equal(expected, GetBlockBytes(program, sub));
        AssertCalls(program, "pal_fade_to");
    }

    [Fact]
    public void GetAtAddr_EmitsExpectedHex()
    {
        using var transpiler = BuildProgram(
            """
            byte x = 96, y = 16;
            ushort addr = get_at_addr(2, x, y);
            vram_adr(addr);
            while (true) ;
            """, out var program);

        var sub = Assert.IsType<Block>(program.GetBlock("get_at_addr"));
        var popa = CallBytes(program, "popa");
        byte[] expected =
        [
            0x29, 0xE0, 0x85, 0x17,
            .. popa, 0x29, 0xE0, 0x4A, 0x4A, 0x4A, 0x05, 0x17, 0x4A, 0x4A, 0x09, 0xC0, 0x85, 0x17,
            .. popa, 0x29, 0x03, 0x0A, 0x0A, 0x09, 0x23, 0xAA, 0xA5, 0x17, 0x60
        ];
        Assert.Equal(expected, GetBlockBytes(program, sub));
        AssertCalls(program, "get_at_addr");
    }

    [Fact]
    public void FlushVramUpdateNmi_UsesConfiguredPointer()
    {
        using var transpiler = BuildProgram(
            """
            set_vram_update((ushort)0x0200);
            flush_vram_update_nmi();
            while (true) ;
            """, out var program);

        var sub = Assert.IsType<Block>(program.GetBlock("flush_vram_update_nmi"));
        var target = program.GetLabels()["updName"];
        Assert.Equal(new byte[] { 0x4C, (byte)target, (byte)(target >> 8) }, GetBlockBytes(program, sub));
        AssertCalls(program, "flush_vram_update_nmi");
    }

    [Fact]
    public void ClearVramBuffer_EmitsBlockAndPullsInVrambufClear()
    {
        using var transpiler = BuildProgram(
            """
            clear_vram_buffer();
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("clear_vram_buffer");
        Assert.NotNull(sub);
        // It's a tail-call (JMP vrambuf_clear) = 1 instruction
        Assert.Equal(1, sub.Count);

        // vrambuf_clear must also be present
        var vrambufClear = program.GetBlock("vrambuf_clear");
        Assert.NotNull(vrambufClear);
    }

    [Fact]
    public void GetPadNew_EmitsBlockAndPullsInPadTrigger()
    {
        using var transpiler = BuildProgram(
            """
            byte joy = (byte)pad_poll(0);
            PAD trig = get_pad_new(0);
            oam_spr(0, 0, 0, 0, 0);
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("get_pad_new");
        Assert.NotNull(sub);
        // Tail-call JMP pad_trigger = 1 instruction
        Assert.Equal(1, sub.Count);

        // pad_trigger and pad_poll must be present
        Assert.NotNull(program.GetBlock("pad_trigger"));
        Assert.NotNull(program.GetBlock("pad_poll"));
    }

    [Fact]
    public void GetFrameCount_EmitsBlock()
    {
        using var transpiler = BuildProgram(
            """
            byte f = get_frame_count();
            oam_spr(f, 0, 0, 0, 0);
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("get_frame_count");
        Assert.NotNull(sub);
        // Tail-call JMP nesclock = 1 instruction
        Assert.Equal(1, sub.Count);
    }

    [Fact]
    public void SetMusicSpeed_EmitsStubBlock()
    {
        using var transpiler = BuildProgram(
            """
            set_music_speed(6);
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("set_music_speed");
        Assert.NotNull(sub);
        // Stub: just RTS
        Assert.Equal(1, sub.Count);
    }

    [Fact]
    public void OneVramBuffer_EmitsBlock()
    {
        using var transpiler = BuildProgram(
            """
            one_vram_buffer(0x42, NTADR_A(5, 10));
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("one_vram_buffer");
        Assert.NotNull(sub);
        Assert.True(sub.Count > 0);
    }

    [Fact]
    public void MultiVramBufferHorz_EmitsBlockAndCommon()
    {
        using var transpiler = BuildProgram(
            """
            byte[] row = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            multi_vram_buffer_horz(row, 8, NTADR_A(2, 2));
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("multi_vram_buffer_horz");
        Assert.NotNull(sub);

        var common = program.GetBlock("multi_vram_buffer_common");
        Assert.NotNull(common);
    }

    [Fact]
    public void MultiVramBufferVert_EmitsBlockAndCommon()
    {
        using var transpiler = BuildProgram(
            """
            byte[] col = new byte[] { 1, 2, 3, 4 };
            multi_vram_buffer_vert(col, 4, NTADR_A(2, 4));
            while (true) ;
            """, out var program);

        var sub = program.GetBlock("multi_vram_buffer_vert");
        Assert.NotNull(sub);

        var common = program.GetBlock("multi_vram_buffer_common");
        Assert.NotNull(common);
    }

    static byte[] GetBlockBytes(Program6502 program, Block block)
    {
        int offset = program.GetBlockAddress(block) - program.BaseAddress;
        return program.ToBytes().Skip(offset).Take(block.Size).ToArray();
    }

    static byte[] CallBytes(Program6502 program, string label)
    {
        ushort address = program.GetLabels()[label];
        return [0x20, (byte)address, (byte)(address >> 8)];
    }

    static void AssertCalls(Program6502 program, string label)
    {
        Assert.Contains(
            Convert.ToHexString(CallBytes(program, label)),
            Convert.ToHexString(program.GetMainBlock()));
    }
}
