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
        // 10 instructions: sta, txa, and, sta, lda, and, ora, sta, rts (+ asl absent)
        // 9 instructions total
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
}
