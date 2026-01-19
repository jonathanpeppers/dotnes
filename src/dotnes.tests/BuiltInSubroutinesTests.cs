using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes.tests;

/// <summary>
/// Tests for BuiltInSubroutines - verifies that Block-based subroutines 
/// produce correct 6502 machine code.
/// </summary>
public class BuiltInSubroutinesTests
{
    #region Core System Subroutines

    [Fact]
    public void Exit_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Exit();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // SEI, LDX #$FF, TXS, INX, STX $2001, STX $4010, STX $2000
        Assert.Equal(
            [0x78, 0xA2, 0xFF, 0x9A, 0xE8, 0x8E, 0x01, 0x20, 0x8E, 0x10, 0x40, 0x8E, 0x00, 0x20],
            bytes);
    }

    [Fact]
    public void Irq_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Irq();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // RTI
        Assert.Equal([0x40], bytes);
    }

    #endregion

    #region Palette Subroutines

    [Fact]
    public void PalAll_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PalAll();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $17, STX $18, LDX #$00, LDA #$20
        Assert.Equal([0x85, 0x17, 0x86, 0x18, 0xA2, 0x00, 0xA9, 0x20], bytes);
    }

    [Fact]
    public void PalCopy_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PalCopy();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $19, LDY #$00, LDA ($17),Y, STA $01C0,X, INX, INY, DEC $19, BNE -11, INC $07, RTS
        Assert.Equal(
            [0x85, 0x19, 0xA0, 0x00, 0xB1, 0x17, 0x9D, 0xC0, 0x01, 0xE8, 0xC8, 0xC6, 0x19, 0xD0, 0xF5, 0xE6, 0x07, 0x60],
            bytes);
    }

    [Fact]
    public void PalClear_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PalClear();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA #$0F, LDX #$00, STA $01C0,X, INX, CPX #$20, BNE -8, STX $07, RTS
        Assert.Equal(
            [0xA9, 0x0F, 0xA2, 0x00, 0x9D, 0xC0, 0x01, 0xE8, 0xE0, 0x20, 0xD0, 0xF8, 0x86, 0x07, 0x60],
            bytes);
    }

    [Fact]
    public void PalSprBright_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PalSprBright();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // TAX, LDA $8422,X, STA $0A, LDA $842B,X, STA $0B, STA $07, RTS
        Assert.Equal(
            [0xAA, 0xBD, 0x22, 0x84, 0x85, 0x0A, 0xBD, 0x2B, 0x84, 0x85, 0x0B, 0x85, 0x07, 0x60],
            bytes);
    }

    [Fact]
    public void PalBgBright_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PalBgBright();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // TAX, LDA $8422,X, STA $08, LDA $842B,X, STA $09, STA $07, RTS
        Assert.Equal(
            [0xAA, 0xBD, 0x22, 0x84, 0x85, 0x08, 0xBD, 0x2B, 0x84, 0x85, 0x09, 0x85, 0x07, 0x60],
            bytes);
    }

    #endregion

    #region PPU Control Subroutines

    [Fact]
    public void PpuMask_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuMask();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $12, RTS
        Assert.Equal([0x85, 0x12, 0x60], bytes);
    }

    [Fact]
    public void PpuOnAll_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuOnAll();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA $12, ORA #$18
        Assert.Equal([0xA5, 0x12, 0x09, 0x18], bytes);
    }

    [Fact]
    public void GetPpuCtrlVar_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.GetPpuCtrlVar();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA $13, RTS
        Assert.Equal([0xA5, 0x13, 0x60], bytes);
    }

    [Fact]
    public void SetPpuCtrlVar_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.SetPpuCtrlVar();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $13, RTS
        Assert.Equal([0x85, 0x13, 0x60], bytes);
    }

    [Fact]
    public void PpuSystem_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuSystem();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA $15, RTS
        Assert.Equal([0xA5, 0x15, 0x60], bytes);
    }

    #endregion

    #region OAM Subroutines

    [Fact]
    public void OamClear_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.OamClear();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDX #$00, STX $14, LDA #$FF, STA $0200,X, INX, INX, INX, INX, BNE -10, RTS
        Assert.Equal(
            [0xA2, 0x00, 0x86, 0x14, 0xA9, 0xFF, 0x9D, 0x00, 0x02, 0xE8, 0xE8, 0xE8, 0xE8, 0xD0, 0xF6, 0x60],
            bytes);
    }

    [Fact]
    public void OamHideRest_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.OamHideRest();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDX $14, LDA #$F0, STA $0200,X, INX, INX, INX, INX, BNE -10, RTS
        Assert.Equal(
            [0xA6, 0x14, 0xA9, 0xF0, 0x9D, 0x00, 0x02, 0xE8, 0xE8, 0xE8, 0xE8, 0xD0, 0xF6, 0x60],
            bytes);
    }

    #endregion

    #region Scroll and Bank Subroutines

    [Fact]
    public void Scroll_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Scroll();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $0C, STX $0D, RTS
        Assert.Equal([0x85, 0x0C, 0x86, 0x0D, 0x60], bytes);
    }

    #endregion

    #region VRAM Subroutines

    [Fact]
    public void VramAdr_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.VramAdr();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STX $2006, STA $2006, RTS
        Assert.Equal([0x8E, 0x06, 0x20, 0x8D, 0x06, 0x20, 0x60], bytes);
    }

    [Fact]
    public void VramPut_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.VramPut();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $2007, RTS
        Assert.Equal([0x8D, 0x07, 0x20, 0x60], bytes);
    }

    #endregion

    #region Timing Subroutines

    [Fact]
    public void NesClock_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.NesClock();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA $1B, LDX $1C, RTS
        Assert.Equal([0xA5, 0x1B, 0xA6, 0x1C, 0x60], bytes);
    }

    [Fact]
    public void NmiSetCallback_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.NmiSetCallback();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $16, STX $17, RTS
        Assert.Equal([0x85, 0x16, 0x86, 0x17, 0x60], bytes);
    }

    #endregion

    #region Stack Operations

    [Fact]
    public void Popa_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Popa();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDY #$00, LDA ($22),Y, INC $22, BNE +2, INC $23, RTS
        Assert.Equal(
            [0xA0, 0x00, 0xB1, 0x22, 0xE6, 0x22, 0xD0, 0x02, 0xE6, 0x23, 0x60],
            bytes);
    }

    [Fact]
    public void Pusha_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Pusha();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDY $22, BNE +2, DEC $23, DEC $22, LDY #$00, STA ($22),Y, RTS
        Assert.Equal(
            [0xA4, 0x22, 0xD0, 0x02, 0xC6, 0x23, 0xC6, 0x22, 0xA0, 0x00, 0x91, 0x22, 0x60],
            bytes);
    }

    [Fact]
    public void Incsp2_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Incsp2();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // CLC, LDA $22, ADC #$02, STA $22, BCC +2, INC $23, RTS
        Assert.Equal(
            [0x18, 0xA5, 0x22, 0x69, 0x02, 0x85, 0x22, 0x90, 0x02, 0xE6, 0x23, 0x60],
            bytes);
    }

    #endregion

    #region Initialization

    [Fact]
    public void Initlib_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Initlib();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA #$23, STA $22, LDA #$03, STA $23, RTS
        Assert.Equal([0xA9, 0x23, 0x85, 0x22, 0xA9, 0x03, 0x85, 0x23, 0x60], bytes);
    }

    [Fact]
    public void Donelib_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Donelib();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // RTS
        Assert.Equal([0x60], bytes);
    }

    #endregion

    #region Block Labels

    [Fact]
    public void AllBlocks_HaveCorrectLabels()
    {
        Assert.Equal("_exit", BuiltInSubroutines.Exit().Label);
        Assert.Equal("_initPPU", BuiltInSubroutines.InitPPU().Label);
        Assert.Equal("_clearPalette", BuiltInSubroutines.ClearPalette().Label);
        Assert.Equal("_clearVRAM", BuiltInSubroutines.ClearVRAM().Label);
        Assert.Equal("_irq", BuiltInSubroutines.Irq().Label);
        Assert.Equal("_pal_all", BuiltInSubroutines.PalAll().Label);
        Assert.Equal("pal_copy", BuiltInSubroutines.PalCopy().Label);
        Assert.Equal("_pal_bg", BuiltInSubroutines.PalBg().Label);
        Assert.Equal("_pal_spr", BuiltInSubroutines.PalSpr().Label);
        Assert.Equal("_pal_col", BuiltInSubroutines.PalCol().Label);
        Assert.Equal("_pal_clear", BuiltInSubroutines.PalClear().Label);
        Assert.Equal("_pal_spr_bright", BuiltInSubroutines.PalSprBright().Label);
        Assert.Equal("_pal_bg_bright", BuiltInSubroutines.PalBgBright().Label);
        Assert.Equal("_pal_bright", BuiltInSubroutines.PalBright().Label);
        Assert.Equal("_ppu_off", BuiltInSubroutines.PpuOff().Label);
        Assert.Equal("_ppu_on_all", BuiltInSubroutines.PpuOnAll().Label);
        Assert.Equal("ppu_onoff", BuiltInSubroutines.PpuOnOff().Label);
        Assert.Equal("_ppu_on_bg", BuiltInSubroutines.PpuOnBg().Label);
        Assert.Equal("_ppu_on_spr", BuiltInSubroutines.PpuOnSpr().Label);
        Assert.Equal("_ppu_mask", BuiltInSubroutines.PpuMask().Label);
        Assert.Equal("_ppu_wait_nmi", BuiltInSubroutines.PpuWaitNmi().Label);
        Assert.Equal("_ppu_wait_frame", BuiltInSubroutines.PpuWaitFrame().Label);
        Assert.Equal("_ppu_system", BuiltInSubroutines.PpuSystem().Label);
        Assert.Equal("_get_ppu_ctrl_var", BuiltInSubroutines.GetPpuCtrlVar().Label);
        Assert.Equal("_set_ppu_ctrl_var", BuiltInSubroutines.SetPpuCtrlVar().Label);
        Assert.Equal("_oam_clear", BuiltInSubroutines.OamClear().Label);
        Assert.Equal("_oam_size", BuiltInSubroutines.OamSize().Label);
        Assert.Equal("_oam_hide_rest", BuiltInSubroutines.OamHideRest().Label);
        Assert.Equal("_oam_spr", BuiltInSubroutines.OamSpr().Label);
        Assert.Equal("_scroll", BuiltInSubroutines.Scroll().Label);
        Assert.Equal("_bank_spr", BuiltInSubroutines.BankSpr().Label);
        Assert.Equal("_bank_bg", BuiltInSubroutines.BankBg().Label);
        Assert.Equal("_vram_adr", BuiltInSubroutines.VramAdr().Label);
        Assert.Equal("_vram_put", BuiltInSubroutines.VramPut().Label);
        Assert.Equal("_vram_fill", BuiltInSubroutines.VramFill().Label);
        Assert.Equal("_vram_inc", BuiltInSubroutines.VramInc().Label);
        Assert.Equal("_vram_write", BuiltInSubroutines.VramWrite().Label);
        Assert.Equal("_set_vram_update", BuiltInSubroutines.SetVramUpdate().Label);
        Assert.Equal("_flush_vram_update", BuiltInSubroutines.FlushVramUpdate().Label);
        Assert.Equal("_nesclock", BuiltInSubroutines.NesClock().Label);
        Assert.Equal("_delay", BuiltInSubroutines.Delay().Label);
        Assert.Equal("_nmi_set_callback", BuiltInSubroutines.NmiSetCallback().Label);
        Assert.Equal("popa", BuiltInSubroutines.Popa().Label);
        Assert.Equal("popax", BuiltInSubroutines.Popax().Label);
        Assert.Equal("pusha", BuiltInSubroutines.Pusha().Label);
        Assert.Equal("pushax", BuiltInSubroutines.Pushax().Label);
        Assert.Equal("incsp2", BuiltInSubroutines.Incsp2().Label);
        Assert.Equal("initlib", BuiltInSubroutines.Initlib().Label);
        Assert.Equal("donelib", BuiltInSubroutines.Donelib().Label);
        Assert.Equal("copydata", BuiltInSubroutines.Copydata().Label);
        Assert.Equal("zerobss", BuiltInSubroutines.Zerobss().Label);
        Assert.Equal("_pad_poll", BuiltInSubroutines.PadPoll().Label);
    }

    #endregion
}
