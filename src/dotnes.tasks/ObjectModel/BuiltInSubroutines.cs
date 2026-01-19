using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes.ObjectModel;

/// <summary>
/// Factory class that creates Block templates for NESLib built-in subroutines.
/// These blocks use label-based addressing for forward references, which get
/// resolved when the program is assembled.
/// </summary>
internal static class BuiltInSubroutines
{
    #region Core System Subroutines

    /// <summary>
    /// System initialization - disables interrupts, sets up stack
    /// </summary>
    public static Block Exit()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L111
        var block = new Block("_exit");
        block.Emit(SEI())
             .Emit(LDX(0xFF))
             .Emit(TXS())
             .Emit(INX())
             .Emit(STX_abs(PPU_MASK))
             .Emit(STX_abs(DMC_FREQ))
             .Emit(STX_abs(PPU_CTRL));
        return block;
    }

    /// <summary>
    /// Initialize PPU - waits for vblank twice
    /// </summary>
    public static Block InitPPU()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L121
        var block = new Block("_initPPU");
        block.Emit(BIT_abs(PPU_STATUS))
             .Emit(BIT_abs(PPU_STATUS), "@1")
             .Emit(BPL(-5))   // branch back to @1
             .Emit(BIT_abs(PPU_STATUS), "@2")
             .Emit(BPL(-5))   // branch back to @2
             .Emit(LDA(0x40))
             .Emit(STA_abs(PPU_FRAMECNT));
        return block;
    }

    /// <summary>
    /// Clear palette RAM to $0F (black)
    /// </summary>
    public static Block ClearPalette()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L135
        var block = new Block("_clearPalette");
        block.Emit(LDA(0x3F))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(STX_abs(PPU_ADDR))
             .Emit(LDA(0x0F))
             .Emit(LDX(0x20))
             .Emit(STA_abs(PPU_DATA), "@1")
             .Emit(DEX())
             .Emit(BNE(-5));  // branch back to @1
        return block;
    }

    /// <summary>
    /// Clear VRAM nametables
    /// </summary>
    public static Block ClearVRAM()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L148
        var block = new Block("_clearVRAM");
        block.Emit(TXA())
             .Emit(LDY(0x20))
             .Emit(STY_abs(PPU_ADDR))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDY(0x10), "@1")
             .Emit(STA_abs(PPU_DATA), "@2")
             .Emit(INX())
             .Emit(BNE(-5))   // branch back to @2
             .Emit(DEY())
             .Emit(BNE(-8));  // branch back to @1
        return block;
    }

    /// <summary>
    /// Wait for 3 vblanks
    /// </summary>
    public static Block WaitSync3()
    {
        var block = new Block("_waitSync3");
        block.Emit(LDA(0x03))
             .Emit(STA_zpg(STARTUP), "waitSync")
             .Emit(LDA_abs(PPU_STATUS), "@1")
             .Emit(BPL(-5))   // branch back to @1
             .Emit(DEC_zpg(STARTUP))
             .Emit(BNE(-10)); // branch back to waitSync
        return block;
    }

    /// <summary>
    /// NMI handler
    /// </summary>
    public static Block Nmi()
    {
        var block = new Block("_nmi");
        block.Emit(PHA())
             .Emit(TXA())
             .Emit(PHA())
             .Emit(TYA())
             .Emit(PHA())
             .Emit(LDA_zpg(STARTUP))
             .Emit(BNE(-2))   // Skip if startup not complete (branch to RTS-like behavior)
             // Actually this needs more context - simplified version
             .Emit(INC_zpg(0x1B));  // Frame counter
        return block;
    }

    /// <summary>
    /// IRQ handler (just returns)
    /// </summary>
    public static Block Irq()
    {
        var block = new Block("_irq");
        block.Emit(RTI());
        return block;
    }

    #endregion

    #region Palette Subroutines

    /// <summary>
    /// _pal_all - Set all 32 colors at once
    /// </summary>
    public static Block PalAll()
    {
        var block = new Block("_pal_all");
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x00))
             .Emit(LDA(0x20));
        // Note: Falls through to pal_copy
        return block;
    }

    /// <summary>
    /// pal_copy - Copy palette data
    /// </summary>
    public static Block PalCopy()
    {
        var block = new Block("pal_copy");
        block.Emit(STA_zpg(0x19))
             .Emit(LDY(0x00))
             .Emit(LDA_ind_Y(TEMP), "@0")
             .Emit(STA_abs_X(PAL_BUF))
             .Emit(INX())
             .Emit(INY())
             .Emit(DEC_zpg(0x19))
             .Emit(BNE(-11))  // branch back to @0
             .Emit(INC_zpg(PAL_UPDATE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _pal_bg - Set background palette (16 colors)
    /// </summary>
    public static Block PalBg()
    {
        var block = new Block("_pal_bg");
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x00))
             .Emit(LDA(0x10))
             .Emit(BNE(-28));  // Branch to pal_copy (relative offset depends on layout)
        return block;
    }

    /// <summary>
    /// _pal_spr - Set sprite palette (16 colors)
    /// </summary>
    public static Block PalSpr()
    {
        var block = new Block("_pal_spr");
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x10))
             .Emit(TXA())
             .Emit(BNE(-37));  // Branch to pal_copy
        return block;
    }

    /// <summary>
    /// _pal_col - Set a single palette color
    /// </summary>
    public static Block PalCol()
    {
        var block = new Block("_pal_col");
        block.Emit(STA_zpg(TEMP))
             .Emit(JSR("popa"))
             .Emit(AND(0x1F))
             .Emit(TAX())
             .Emit(LDA_zpg(TEMP))
             .Emit(STA_abs_X(PAL_BUF))
             .Emit(INC_zpg(PAL_UPDATE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _pal_clear - Clear all palette entries to $0F
    /// </summary>
    public static Block PalClear()
    {
        var block = new Block("_pal_clear");
        block.Emit(LDA(0x0F))
             .Emit(LDX(0x00))
             .Emit(STA_abs_X(PAL_BUF), "@1")
             .Emit(INX())
             .Emit(CPX(0x20))
             .Emit(BNE(-8))   // branch back to @1
             .Emit(STX_zpg(PAL_UPDATE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _pal_spr_bright - Set sprite palette brightness
    /// </summary>
    public static Block PalSprBright()
    {
        var block = new Block("_pal_spr_bright");
        block.Emit(TAX())
             .Emit(LDA_abs_X(palBrightTableL))
             .Emit(STA_zpg(PAL_SPR_PTR))
             .Emit(LDA_abs_X(palBrightTableH))
             .Emit(STA_zpg(PAL_SPR_PTR + 1))
             .Emit(STA_zpg(PAL_UPDATE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _pal_bg_bright - Set background palette brightness
    /// </summary>
    public static Block PalBgBright()
    {
        var block = new Block("_pal_bg_bright");
        block.Emit(TAX())
             .Emit(LDA_abs_X(palBrightTableL))
             .Emit(STA_zpg(PAL_BG_PTR))
             .Emit(LDA_abs_X(palBrightTableH))
             .Emit(STA_zpg(PAL_BG_PTR + 1))
             .Emit(STA_zpg(PAL_UPDATE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _pal_bright - Set both sprite and bg brightness
    /// </summary>
    public static Block PalBright()
    {
        var block = new Block("_pal_bright");
        block.Emit(JSR("_pal_spr_bright"))
             .Emit(TXA())
             .Emit(JMP_abs("_pal_bg_bright"));
        return block;
    }

    #endregion

    #region PPU Control Subroutines

    /// <summary>
    /// _ppu_off - Disable rendering
    /// </summary>
    public static Block PpuOff()
    {
        var block = new Block("_ppu_off");
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(AND(0xE7))
             .Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(JMP_abs("_ppu_wait_nmi"));
        return block;
    }

    /// <summary>
    /// _ppu_on_all - Enable background and sprite rendering
    /// </summary>
    public static Block PpuOnAll()
    {
        var block = new Block("_ppu_on_all");
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(ORA(0x18));
        // Falls through to ppu_onoff
        return block;
    }

    /// <summary>
    /// ppu_onoff - Common tail for PPU enable functions
    /// </summary>
    public static Block PpuOnOff()
    {
        var block = new Block("ppu_onoff");
        block.Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(JMP_abs("_ppu_wait_nmi"));
        return block;
    }

    /// <summary>
    /// _ppu_on_bg - Enable background rendering only
    /// </summary>
    public static Block PpuOnBg()
    {
        var block = new Block("_ppu_on_bg");
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(ORA(0x08))
             .Emit(BNE(-11));  // Branch to ppu_onoff
        return block;
    }

    /// <summary>
    /// _ppu_on_spr - Enable sprite rendering only
    /// </summary>
    public static Block PpuOnSpr()
    {
        var block = new Block("_ppu_on_spr");
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(ORA(0x10))
             .Emit(BNE(-16));  // Branch to ppu_onoff
        return block;
    }

    /// <summary>
    /// _ppu_mask - Set PPU_MASK directly
    /// </summary>
    public static Block PpuMask()
    {
        var block = new Block("_ppu_mask");
        block.Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _ppu_wait_nmi - Wait for next NMI
    /// </summary>
    public static Block PpuWaitNmi()
    {
        var block = new Block("_ppu_wait_nmi");
        block.Emit(LDA_zpg(0x1B))
             .Emit(CMP_zpg(0x1B), "@1")
             .Emit(BEQ(-4))   // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _ppu_wait_frame - Alias for ppu_wait_nmi
    /// </summary>
    public static Block PpuWaitFrame()
    {
        var block = new Block("_ppu_wait_frame");
        block.Emit(JMP_abs("_ppu_wait_nmi"));
        return block;
    }

    /// <summary>
    /// _ppu_system - Detect PAL/NTSC
    /// </summary>
    public static Block PpuSystem()
    {
        var block = new Block("_ppu_system");
        block.Emit(LDA_zpg(0x15))  // NTSC_MODE
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _get_ppu_ctrl_var - Get PPU_CTRL variable
    /// </summary>
    public static Block GetPpuCtrlVar()
    {
        var block = new Block("_get_ppu_ctrl_var");
        block.Emit(LDA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _set_ppu_ctrl_var - Set PPU_CTRL variable
    /// </summary>
    public static Block SetPpuCtrlVar()
    {
        var block = new Block("_set_ppu_ctrl_var");
        block.Emit(STA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(RTS());
        return block;
    }

    #endregion

    #region OAM (Sprite) Subroutines

    /// <summary>
    /// _oam_clear - Clear all OAM entries
    /// </summary>
    public static Block OamClear()
    {
        var block = new Block("_oam_clear");
        block.Emit(LDX(0x00))
             .Emit(STX_zpg(0x14))  // OAM_INDEX
             .Emit(LDA(0xFF))
             .Emit(STA_abs_X(OAM_BUF), "@1")
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(BNE(-10))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_size - Set sprite size (8x8 or 8x16)
    /// </summary>
    public static Block OamSize()
    {
        var block = new Block("_oam_size");
        block.Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(AND(0xDF))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(0x13))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_hide_rest - Hide remaining sprites
    /// </summary>
    public static Block OamHideRest()
    {
        var block = new Block("_oam_hide_rest");
        block.Emit(LDX_zpg(0x14))  // OAM_INDEX
             .Emit(LDA(0xF0))
             .Emit(STA_abs_X(OAM_BUF), "@1")
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(BNE(-10))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_spr - Add a sprite to OAM
    /// </summary>
    public static Block OamSpr()
    {
        var block = new Block("_oam_spr");
        block.Emit(STA_zpg(0x19))  // Store tile
             .Emit(JSR("popa"))
             .Emit(STA_zpg(0x1A))  // Store attr
             .Emit(JSR("popa"))
             .Emit(STA_zpg(TEMP + 1))  // Store Y
             .Emit(JSR("popa"))
             .Emit(LDX_zpg(0x14))  // OAM_INDEX
             .Emit(STA_abs_X(OAM_BUF + 3))  // X position
             .Emit(LDA_zpg(0x19))
             .Emit(STA_abs_X(OAM_BUF + 1))  // Tile
             .Emit(LDA_zpg(0x1A))
             .Emit(STA_abs_X(OAM_BUF + 2))  // Attr
             .Emit(LDA_zpg(TEMP + 1))
             .Emit(STA_abs_X(OAM_BUF))      // Y position
             .Emit(TXA())
             .Emit(CLC())
             .Emit(ADC(0x04))
             .Emit(STA_zpg(0x14))  // Update OAM_INDEX
             .Emit(RTS());
        return block;
    }

    #endregion

    #region Scroll and Bank Subroutines

    /// <summary>
    /// _scroll - Set scroll position
    /// </summary>
    public static Block Scroll()
    {
        var block = new Block("_scroll");
        block.Emit(STA_zpg(SCROLL_X))
             .Emit(STX_zpg(SCROLL_Y))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _bank_spr - Set sprite CHR bank
    /// </summary>
    public static Block BankSpr()
    {
        var block = new Block("_bank_spr");
        block.Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(AND(0xF7))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(0x13))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _bank_bg - Set background CHR bank
    /// </summary>
    public static Block BankBg()
    {
        var block = new Block("_bank_bg");
        block.Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(AND(0xEF))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(0x13))
             .Emit(RTS());
        return block;
    }

    #endregion

    #region VRAM Subroutines

    /// <summary>
    /// _vram_adr - Set VRAM address
    /// </summary>
    public static Block VramAdr()
    {
        var block = new Block("_vram_adr");
        block.Emit(STX_abs(PPU_ADDR))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_put - Write byte to VRAM
    /// </summary>
    public static Block VramPut()
    {
        var block = new Block("_vram_put");
        block.Emit(STA_abs(PPU_DATA))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_fill - Fill VRAM with a value
    /// </summary>
    public static Block VramFill()
    {
        var block = new Block("_vram_fill");
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(JSR("popa"))
             .Emit(LDX_zpg(TEMP))
             .Emit(BEQ(6))  // skip if low byte is 0
             .Emit(INC_zpg(TEMP + 1))
             .Emit(LDY(0x00))
             .Emit(STA_abs(PPU_DATA), "@1")
             .Emit(DEX())
             .Emit(BNE(-5))  // branch back to @1
             .Emit(DEC_zpg(TEMP + 1))
             .Emit(BNE(-8))  // continue if high byte > 0
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_inc - Set VRAM increment mode (0=+1, 1=+32)
    /// </summary>
    public static Block VramInc()
    {
        var block = new Block("_vram_inc");
        block.Emit(ORA_zpg(0x13))  // PPU_CTRL_VAR
             .Emit(STA_zpg(0x13))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_write - Write bytes to VRAM
    /// </summary>
    public static Block VramWrite()
    {
        var block = new Block("_vram_write");
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(JSR("popa"))
             .Emit(TAY())
             .Emit(LDX(0x00), "@1")
             .Emit(LDA_ind_X(ptr1))
             .Emit(STA_abs(PPU_DATA))
             .Emit(INC_zpg(ptr1))
             .Emit(BNE(2))   // skip increment of high byte
             .Emit(INC_zpg(ptr1 + 1))
             .Emit(INX())
             .Emit(CPX_zpg(TEMP))
             .Emit(BNE(-14)) // branch back to @1
             .Emit(DEY())
             .Emit(BNE(-17))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _set_vram_update - Set VRAM update pointer
    /// </summary>
    public static Block SetVramUpdate()
    {
        var block = new Block("_set_vram_update");
        block.Emit(STA_zpg(NAME_UPD_ADR))
             .Emit(STX_zpg(NAME_UPD_ADR + 1))
             .Emit(ORA_zpg(NAME_UPD_ADR + 1))
             .Emit(STA_zpg(NAME_UPD_ENABLE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _flush_vram_update - Force VRAM update
    /// </summary>
    public static Block FlushVramUpdate()
    {
        var block = new Block("_flush_vram_update");
        block.Emit(LDA_zpg(NAME_UPD_ENABLE))
             .Emit(BEQ(3))    // skip if disabled
             .Emit(JSR("updName"))
             .Emit(RTS());
        return block;
    }

    #endregion

    #region Timing Subroutines

    /// <summary>
    /// _nesclock - Read frame counter
    /// </summary>
    public static Block NesClock()
    {
        var block = new Block("_nesclock");
        block.Emit(LDA_zpg(0x1B))
             .Emit(LDX_zpg(0x1C))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _delay - Wait for N frames
    /// </summary>
    public static Block Delay()
    {
        var block = new Block("_delay");
        block.Emit(TAX())
             .Emit(JSR("_ppu_wait_nmi"), "@1")
             .Emit(DEX())
             .Emit(BNE(-6))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _nmi_set_callback - Set NMI callback address
    /// </summary>
    public static Block NmiSetCallback()
    {
        var block = new Block("_nmi_set_callback");
        block.Emit(STA_zpg(0x16))  // NMI_CALLBACK
             .Emit(STX_zpg(0x16 + 1))
             .Emit(RTS());
        return block;
    }

    #endregion

    #region Stack Operations

    /// <summary>
    /// popa - Pop byte from stack
    /// </summary>
    public static Block Popa()
    {
        var block = new Block("popa");
        block.Emit(LDY(0x00))
             .Emit(LDA_ind_Y(sp))
             .Emit(INC_zpg(sp))
             .Emit(BNE(2))
             .Emit(INC_zpg(sp + 1))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// popax - Pop word from stack (A=low, X=high)
    /// </summary>
    public static Block Popax()
    {
        var block = new Block("popax");
        block.Emit(LDY(0x01))
             .Emit(LDA_ind_Y(sp))
             .Emit(TAX())
             .Emit(DEY())
             .Emit(LDA_ind_Y(sp))
             .Emit(INY())
             .Emit(INY())
             .Emit(STY_zpg(TEMP))
             .Emit(CLC())
             .Emit(LDA_zpg(sp))
             .Emit(ADC_zpg(TEMP))
             .Emit(STA_zpg(sp))
             .Emit(BCC(2))
             .Emit(INC_zpg(sp + 1))
             .Emit(LDA_ind_Y(sp))
             .Emit(LDY(0x00))
             .Emit(LDA_ind_Y(sp))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// pusha - Push byte to stack
    /// </summary>
    public static Block Pusha()
    {
        var block = new Block("pusha");
        block.Emit(LDY_zpg(sp))
             .Emit(BNE(2))
             .Emit(DEC_zpg(sp + 1))
             .Emit(DEC_zpg(sp))
             .Emit(LDY(0x00))
             .Emit(STA_ind_Y(sp))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// pushax - Push word to stack (A=low, X=high)
    /// </summary>
    public static Block Pushax()
    {
        var block = new Block("pushax");
        block.Emit(PHA())
             .Emit(LDA_zpg(sp))
             .Emit(SEC())
             .Emit(SBC(0x02))
             .Emit(STA_zpg(sp))
             .Emit(BCS(2))
             .Emit(DEC_zpg(sp + 1))
             .Emit(LDY(0x01))
             .Emit(TXA())
             .Emit(STA_ind_Y(sp))
             .Emit(PLA())
             .Emit(DEY())
             .Emit(STA_ind_Y(sp))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// incsp2 - Increment stack pointer by 2
    /// </summary>
    public static Block Incsp2()
    {
        var block = new Block("incsp2");
        block.Emit(CLC())
             .Emit(LDA_zpg(sp))
             .Emit(ADC(0x02))
             .Emit(STA_zpg(sp))
             .Emit(BCC(2))
             .Emit(INC_zpg(sp + 1))
             .Emit(RTS());
        return block;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// initlib - Initialize C runtime library
    /// </summary>
    public static Block Initlib()
    {
        var block = new Block("initlib");
        block.Emit(LDA(0x23))
             .Emit(STA_zpg(sp))
             .Emit(LDA(0x03))
             .Emit(STA_zpg(sp + 1))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// donelib - Cleanup C runtime library
    /// </summary>
    public static Block Donelib()
    {
        var block = new Block("donelib");
        block.Emit(RTS());
        return block;
    }

    /// <summary>
    /// copydata - Copy initialized data section
    /// </summary>
    public static Block Copydata()
    {
        var block = new Block("copydata");
        // Simplified - actual implementation is more complex
        block.Emit(RTS());
        return block;
    }

    /// <summary>
    /// zerobss - Zero BSS section
    /// </summary>
    public static Block Zerobss()
    {
        var block = new Block("zerobss");
        block.Emit(LDA(0x00))
             .Emit(STA_zpg(ptr1))
             .Emit(STA_zpg(ptr1 + 1))
             .Emit(RTS());
        return block;
    }

    #endregion

    #region Pad Polling

    /// <summary>
    /// _pad_poll - Poll controller state
    /// </summary>
    public static Block PadPoll()
    {
        var block = new Block("_pad_poll");
        block.Emit(TAY())
             .Emit(LDX(0x00))
             // Pad poll port
             .Emit(LDA(0x01), "@padPollPort")
             .Emit(STA_abs(0x4016))
             .Emit(LDA(0x00))
             .Emit(STA_abs(0x4016))
             .Emit(LDA(0x08))
             .Emit(STA_zpg(TEMP))
             // Pad poll loop
             .Emit(LDA_abs_Y(0x4016), "@padPollLoop")
             .Emit(LSR_A())
             .Emit(ROR_X_zpg(TEMP + 1))
             .Emit(DEC_zpg(TEMP))
             .Emit(BNE(-10))  // branch to @padPollLoop
             .Emit(INX())
             .Emit(CPX(0x03))
             .Emit(BNE(-29)) // branch to @padPollPort
             .Emit(LDA_zpg(TEMP + 1))
             .Emit(CMP_zpg(0x19))
             .Emit(BEQ(6))   // branch to @done
             .Emit(CMP_zpg(0x1A))
             .Emit(BEQ(2))   // branch to @done
             .Emit(LDA_zpg(0x19))
             // @done
             .Emit(STA_abs_Y(0x003C), "@done")
             .Emit(TAX())
             .Emit(EOR_Y_abs(0x003E))
             .Emit(AND_Y_abs(0x003C))
             .Emit(STA_abs_Y(0x0040))
             .Emit(TXA())
             .Emit(STA_abs_Y(0x003E))
             .Emit(LDX(0x00))
             .Emit(RTS());
        return block;
    }

    #endregion
}
