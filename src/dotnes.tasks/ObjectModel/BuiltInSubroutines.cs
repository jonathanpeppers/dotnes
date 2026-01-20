using NES;
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
        var block = new Block(nameof(_exit));
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
        var block = new Block(nameof(_initPPU));
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
        var block = new Block(nameof(_clearPalette));
        block.Emit(LDA(0x3F))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(STX_abs(PPU_ADDR))
             .Emit(LDA(0x0F))
             .Emit(LDX(0x20))
             .Emit(STA_abs(PPU_DATA), "@1")
             .Emit(DEX())
             .Emit(BNE(-6));  // branch back to @1
        return block;
    }

    /// <summary>
    /// Clear VRAM nametables
    /// </summary>
    public static Block ClearVRAM()
    {
        // https://github.com/clbr/neslib/blob/d061b0f7f1a449941111c31eee0fc2e85b1826d7/crt0.s#L148
        var block = new Block(nameof(_clearVRAM));
        block.Emit(TXA())
             .Emit(LDY(0x20))
             .Emit(STY_abs(PPU_ADDR))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDY(0x10))
             .Emit(STA_abs(PPU_DATA), "@1")
             .Emit(INX())
             .Emit(BNE(-6))   // branch back to @1
             .Emit(DEY())
             .Emit(BNE(-9));  // branch back to STA (inner loop start)
        return block;
    }

    /// <summary>
    /// Wait for 3 vblanks
    /// </summary>
    public static Block WaitSync3()
    {
        // NESWriter: A501 C501 F0FC (different from neslib original)
        // This is a simplified version that just waits for STARTUP to change
        var block = new Block(nameof(_waitSync3));
        block.Emit(LDA_zpg(STARTUP))
             .Emit(CMP_zpg(STARTUP))
             .Emit(BEQ(-4));  // BEQ back to CMP (0xFC = -4)
        return block;
    }

    /// <summary>
    /// NMI handler - pushes registers and checks PPU_MASK_VAR
    /// </summary>
    public static Block Nmi()
    {
        // NESWriter: 48 8A 48 98 48 A512 2918 D003 4CE681
        // Note: JMP address (0x81E6) is @skipAll, layout dependent
        var block = new Block(nameof(_nmi));
        block.Emit(PHA())
             .Emit(TXA())
             .Emit(PHA())
             .Emit(TYA())
             .Emit(PHA())
             .Emit(LDA_zpg(0x12))    // PPU_MASK_VAR
             .Emit(AND(0x18))
             .Emit(BNE(3))           // if rendering enabled, continue
             .Emit(JMP_abs(0x81E6)); // JMP to @skipAll (address is layout dependent)
        return block;
    }

    /// <summary>
    /// IRQ handler - pushes registers and jumps to skipNtsc
    /// </summary>
    public static Block Irq()
    {
        var block = new Block(nameof(_irq));
        block.Emit(PHA())
             .Emit(TXA())
             .Emit(PHA())
             .Emit(TYA())
             .Emit(PHA())
             .Emit(LDA(0xFF))
             .Emit(JMP_abs(skipNtsc));  // Use constant address
        return block;
    }

    #endregion

    #region Palette Subroutines

    /// <summary>
    /// _pal_all - Set all 32 colors at once
    /// Note: Must be emitted immediately before pal_copy (falls through)
    /// </summary>
    public static Block PalAll()
    {
        var block = new Block(nameof(NESLib.pal_all));
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x00))
             .Emit(LDA(0x20));
        // Falls through to pal_copy
        return block;
    }

    /// <summary>
    /// pal_copy - Copy palette data (shared tail for pal_all, pal_bg, pal_spr)
    /// </summary>
    public static Block PalCopy()
    {
        var block = new Block(nameof(NESLib.pal_copy));
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
    /// Uses label-based branch to pal_copy
    /// </summary>
    public static Block PalBg()
    {
        var block = new Block(nameof(NESLib.pal_bg));
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x00))
             .Emit(LDA(0x10))
             .Emit(BNE(nameof(NESLib.pal_copy)));  // Branch to pal_copy
        return block;
    }

    /// <summary>
    /// _pal_spr - Set sprite palette (16 colors)
    /// Uses label-based branch to pal_copy
    /// </summary>
    public static Block PalSpr()
    {
        var block = new Block(nameof(NESLib.pal_spr));
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(LDX(0x10))
             .Emit(TXA())
             .Emit(BNE(nameof(NESLib.pal_copy)));  // Branch to pal_copy
        return block;
    }

    /// <summary>
    /// _pal_col - Set a single palette color
    /// </summary>
    public static Block PalCol()
    {
        var block = new Block(nameof(NESLib.pal_col));
        block.Emit(STA_zpg(TEMP))
             .Emit(JSR(nameof(NESConstants.popa)))
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
        var block = new Block(nameof(NESLib.pal_clear));
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
        var block = new Block(nameof(NESLib.pal_spr_bright));
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
        var block = new Block(nameof(NESLib.pal_bg_bright));
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
        var block = new Block(nameof(NESLib.pal_bright));
        block.Emit(JSR(pal_spr_bright))
             .Emit(TXA())
             .Emit(JMP_abs(pal_bg_bright));
        return block;
    }

    #endregion

    #region PPU Control Subroutines

    /// <summary>
    /// _ppu_off - Disable rendering
    /// </summary>
    public static Block PpuOff()
    {
        var block = new Block(nameof(NESLib.ppu_off));
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(AND(0xE7))
             .Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(JMP_abs(ppu_wait_nmi));
        return block;
    }

    /// <summary>
    /// _ppu_on_all - Enable background and sprite rendering
    /// Note: Must be emitted immediately before ppu_onoff (falls through)
    /// </summary>
    public static Block PpuOnAll()
    {
        var block = new Block(nameof(NESLib.ppu_on_all));
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
        var block = new Block(nameof(NESLib.ppu_onoff));
        block.Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(JMP_abs(ppu_wait_nmi));  // Uses constant since ppu_wait_nmi is emitted later
        return block;
    }

    /// <summary>
    /// _ppu_on_bg - Enable background rendering only
    /// Uses label-based branch to ppu_onoff
    /// </summary>
    public static Block PpuOnBg()
    {
        var block = new Block(nameof(NESLib.ppu_on_bg));
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(ORA(0x08))
             .Emit(BNE(nameof(NESLib.ppu_onoff)));
        return block;
    }

    /// <summary>
    /// _ppu_on_spr - Enable sprite rendering only
    /// Uses label-based branch to ppu_onoff
    /// </summary>
    public static Block PpuOnSpr()
    {
        var block = new Block(nameof(NESLib.ppu_on_spr));
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(ORA(0x10))
             .Emit(BNE(nameof(NESLib.ppu_onoff)));
        return block;
    }

    /// <summary>
    /// _ppu_mask - Set PPU_MASK directly
    /// </summary>
    public static Block PpuMask()
    {
        var block = new Block(nameof(NESLib.ppu_mask));
        block.Emit(STA_zpg(PPU_MASK_VAR))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// ppu_wait_nmi - Wait for next NMI
    /// </summary>
    public static Block PpuWaitNmi()
    {
        // NESWriter: LDA #$01, STA VRAM_UPDATE, LDA STARTUP, CMP STARTUP, BEQ -4, RTS
        var block = new Block(nameof(NESLib.ppu_wait_nmi));
        block.Emit(LDA(0x01))
             .Emit(STA_zpg(VRAM_UPDATE))
             .Emit(LDA_zpg(STARTUP))
             .Emit(CMP_zpg(STARTUP), "@1")
             .Emit(BEQ(-4))   // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _ppu_wait_frame - Wait for frame, with extended checks
    /// </summary>
    public static Block PpuWaitFrame()
    {
        // NESWriter has more complex logic with ZP_START and NES_PRG_BANKS checks
        var block = new Block(nameof(NESLib.ppu_wait_frame));
        block.Emit(LDA(0x01))
             .Emit(STA_zpg(VRAM_UPDATE))
             .Emit(LDA_zpg(STARTUP))
             .Emit(CMP_zpg(STARTUP), "@1")
             .Emit(BEQ(-4))   // branch back to @1
             .Emit(LDA_zpg(ZP_START))
             .Emit(BEQ(6), "@2")   // branch to @3 (end)
             .Emit(LDA_zpg(NES_PRG_BANKS))
             .Emit(CMP(0x05), "@loop")
             .Emit(BEQ(-6))   // branch back to @loop (actually to LDA NES_PRG_BANKS)
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _ppu_system - Detect PAL/NTSC
    /// </summary>
    public static Block PpuSystem()
    {
        // NESWriter: LDA ZP_START ($00), LDX #$00, RTS
        var block = new Block(nameof(NESLib.ppu_system));
        block.Emit(LDA_zpg(ZP_START))
             .Emit(LDX(0x00))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _get_ppu_ctrl_var - Get PPU_CTRL variable
    /// </summary>
    public static Block GetPpuCtrlVar()
    {
        // NESWriter: LDA PRG_FILEOFFS ($10), LDX #$00, RTS
        var block = new Block(nameof(NESLib.get_ppu_ctrl_var));
        block.Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(LDX(0x00))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _set_ppu_ctrl_var - Set PPU_CTRL variable
    /// </summary>
    public static Block SetPpuCtrlVar()
    {
        // NESWriter: STA PRG_FILEOFFS ($10), RTS
        var block = new Block(nameof(NESLib.set_ppu_ctrl_var));
        block.Emit(STA_zpg(PRG_FILEOFFS))
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
        // NESWriter: LDX #$00, LDA #$FF, STA $0200,X, INX x4, BNE -9, RTS
        var block = new Block(nameof(NESLib.oam_clear));
        block.Emit(LDX(0x00))
             .Emit(LDA(0xFF))
             .Emit(STA_abs_X(OAM_BUF), "@1")
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(BNE(-9))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_size - Set sprite size (8x8 or 8x16)
    /// </summary>
    public static Block OamSize()
    {
        // NESWriter uses PRG_FILEOFFS ($10), not PPU_CTRL_VAR ($13)
        var block = new Block(nameof(NESLib.oam_size));
        block.Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(AND(0x20))
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(AND(0xDF))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(PRG_FILEOFFS))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_hide_rest - Hide remaining sprites
    /// </summary>
    public static Block OamHideRest()
    {
        // NESWriter: TAX, LDA #$F0, STA $0200,X, INX x4, BNE -9, RTS
        var block = new Block(nameof(NESLib.oam_hide_rest));
        block.Emit(TAX())
             .Emit(LDA(0xF0))
             .Emit(STA_abs_X(OAM_BUF), "@1")
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(INX())
             .Emit(BNE(-9))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _oam_spr - Add a sprite to OAM
    /// </summary>
    public static Block OamSpr()
    {
        // 85B7 TAX           ; _oam_spr
        // 85B8 LDY #$00
        // 85BA LDA (sp),y
        // 85BC INY
        // 85BD STA $0202,x
        // 85C0 LDA (sp),y
        // 85C2 INY
        // 85C3 STA $0201,x
        // 85C6 LDA (sp),y
        // 85C8 INY
        // 85C9 STA $0200,x
        // 85CC LDA (sp),y
        // 85CE STA $0203,x
        // 85D1 LDA sp
        // 85D3 CLC
        // 85D4 ADC #$04
        // 85D6 STA sp
        // 85D8 BCC @1
        // 85DA INC sp+1
        // 85DC TXA           ; @1
        // 85DD CLC
        // 85DE ADC #$04
        // 85E0 LDX #$00
        // 85E2 RTS
        var block = new Block(nameof(NESLib.oam_spr));
        block.Emit(TAX())
             .Emit(LDY(0x00))
             .Emit(LDA_ind_Y(sp))
             .Emit(INY())
             .Emit(STA_abs_X(OAM_BUF + 2))
             .Emit(LDA_ind_Y(sp))
             .Emit(INY())
             .Emit(STA_abs_X(OAM_BUF + 1))
             .Emit(LDA_ind_Y(sp))
             .Emit(INY())
             .Emit(STA_abs_X(OAM_BUF + 0))
             .Emit(LDA_ind_Y(sp))
             .Emit(STA_abs_X(OAM_BUF + 3))
             .Emit(LDA_zpg(sp))
             .Emit(CLC())
             .Emit(ADC(0x04))
             .Emit(STA_zpg(sp))
             .Emit(BCC(0x02))  // skip next instruction
             .Emit(INC_zpg(sp + 1))
             .Emit(TXA(), "@1")
             .Emit(CLC())
             .Emit(ADC(0x04))
             .Emit(LDX(0x00))
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
        // 82FB STA TEMP       ; _scroll
        // 82FD TXA
        // 82FE BNE +$0E       ; to @1
        // 8300 LDA TEMP
        // 8302 CMP #$F0
        // 8304 BCS +$08       ; to @1
        // 8306 STA SCROLL_Y
        // 8308 LDA #$00
        // 830A STA TEMP
        // 830C BEQ +$0B       ; to @2
        // 830E SEC            ; @1
        // 830F LDA TEMP
        // 8311 SBC #$F0
        // 8313 STA SCROLL_Y
        // 8315 LDA #$02
        // 8317 STA TEMP
        // 8319 JSR popax      ; @2
        // 831C STA SCROLL_X
        // 831E TXA
        // 831F AND #$01
        // 8321 ORA TEMP
        // 8323 STA TEMP
        // 8325 LDA PRG_FILEOFFS
        // 8327 AND #$FC
        // 8329 ORA TEMP
        // 832B STA PRG_FILEOFFS
        // 832D RTS
        var block = new Block(nameof(NESLib.scroll));
        block.Emit(STA_zpg(TEMP))
             .Emit(TXA())
             .Emit(BNE(0x0E))    // to @1
             .Emit(LDA_zpg(TEMP))
             .Emit(CMP(0xF0))
             .Emit(BCS(0x08))    // to @1
             .Emit(STA_zpg(SCROLL_Y))
             .Emit(LDA(0x00))
             .Emit(STA_zpg(TEMP))
             .Emit(BEQ(0x0B))    // to @2
             .Emit(SEC(), "@1")
             .Emit(LDA_zpg(TEMP))
             .Emit(SBC(0xF0))
             .Emit(STA_zpg(SCROLL_Y))
             .Emit(LDA(0x02))
             .Emit(STA_zpg(TEMP))
             .Emit(JSR(nameof(NESConstants.popax)), "@2")
             .Emit(STA_zpg(SCROLL_X))
             .Emit(TXA())
             .Emit(AND(0x01))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(AND(0xFC))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(PRG_FILEOFFS))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _bank_spr - Set sprite CHR bank
    /// </summary>
    public static Block BankSpr()
    {
        var block = new Block(nameof(NESLib.bank_spr));
        block.Emit(AND(0x01))
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(AND(0xF7))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(PRG_FILEOFFS))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _bank_bg - Set background CHR bank
    /// </summary>
    public static Block BankBg()
    {
        var block = new Block(nameof(NESLib.bank_bg));
        block.Emit(AND(0x01))
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(ASL_A())
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(AND(0xEF))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(PRG_FILEOFFS))
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
        var block = new Block(nameof(NESLib.vram_adr));
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
        var block = new Block(nameof(NESLib.vram_put));
        block.Emit(STA_abs(PPU_DATA))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_fill - Fill VRAM with a value
    /// </summary>
    public static Block VramFill()
    {
        // 83DF STA $19       ; _vram_fill
        // 83E1 STX $1A
        // 83E3 JSR popa
        // 83E6 LDX $1A
        // 83E8 BEQ +$0C      ; to @3
        // 83EA LDX #$00      ; @1
        // 83EC STA $2007     ; @2
        // 83EF DEX
        // 83F0 BNE -$06      ; to @2
        // 83F2 DEC $1A
        // 83F4 BNE -$0A      ; to @2
        // 83F6 LDX $19       ; @3
        // 83F8 BEQ +$06      ; to @4
        // 83FA STA $2007
        // 83FD DEX
        // 83FE BNE -$06      ; to 83FA
        // 8400 RTS           ; @4
        var block = new Block(nameof(NESLib.vram_fill));
        block.Emit(STA_zpg(0x19))
             .Emit(STX_zpg(0x1A))
             .Emit(JSR(nameof(NESConstants.popa)))
             .Emit(LDX_zpg(0x1A))
             .Emit(BEQ(0x0C))  // branch to @3
             .Emit(LDX(0x00), "@1")
             .Emit(STA_abs(PPU_DATA), "@2")
             .Emit(DEX())
             .Emit(BNE(-6))  // branch back to @2
             .Emit(DEC_zpg(0x1A))
             .Emit(BNE(-10))  // branch back to @2
             .Emit(LDX_zpg(0x19), "@3")
             .Emit(BEQ(0x06))  // branch to @4
             .Emit(STA_abs(PPU_DATA))
             .Emit(DEX())
             .Emit(BNE(-6))  // branch back
             .Emit(RTS(), "@4");
        return block;
    }

    /// <summary>
    /// _vram_inc - Set VRAM increment mode (0=+1, 1=+32)
    /// </summary>
    public static Block VramInc()
    {
        // 8401 ORA #$00      ; _vram_inc
        // 8403 BEQ +$02
        // 8405 LDA #$04
        // 8407 STA TEMP
        // 8409 LDA PRG_FILEOFFS
        // 840B AND #$FB
        // 840D ORA TEMP
        // 840F STA PRG_FILEOFFS
        // 8411 STA $2000
        // 8414 RTS
        var block = new Block(nameof(NESLib.vram_inc));
        block.Emit(ORA(0x00))
             .Emit(BEQ(0x02))
             .Emit(LDA(0x04))
             .Emit(STA_zpg(TEMP))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(AND(0xFB))
             .Emit(ORA_zpg(TEMP))
             .Emit(STA_zpg(PRG_FILEOFFS))
             .Emit(STA_abs(PPU_CTRL))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _vram_write - Write bytes to VRAM
    /// </summary>
    public static Block VramWrite()
    {
        // 834F STA TEMP      ; _vram_write
        // 8351 STX TEMP+1
        // 8353 JSR popax
        // 8356 STA $19
        // 8358 STX $1A
        // 835A LDY #$00
        // 835C LDA ($19),y   ; @1
        // 835E STA $2007
        // 8361 INC $19
        // 8363 BNE +$02
        // 8365 INC $1A
        // 8367 LDA TEMP
        // 8369 BNE +$02
        // 836B DEC TEMP+1
        // 836D DEC TEMP
        // 836F LDA TEMP
        // 8371 ORA TEMP+1
        // 8373 BNE -$19      ; to @1
        // 8375 RTS
        var block = new Block(nameof(NESLib.vram_write));
        block.Emit(STA_zpg(TEMP))
             .Emit(STX_zpg(TEMP + 1))
             .Emit(JSR(nameof(NESConstants.popax)))
             .Emit(STA_zpg(0x19))
             .Emit(STX_zpg(0x1A))
             .Emit(LDY(0x00))
             .Emit(LDA_ind_Y(0x19), "@1")
             .Emit(STA_abs(PPU_DATA))
             .Emit(INC_zpg(0x19))
             .Emit(BNE(0x02))
             .Emit(INC_zpg(0x1A))
             .Emit(LDA_zpg(TEMP))
             .Emit(BNE(0x02))
             .Emit(DEC_zpg(TEMP + 1))
             .Emit(DEC_zpg(TEMP))
             .Emit(LDA_zpg(TEMP))
             .Emit(ORA_zpg(TEMP + 1))
             .Emit(BNE(-25))  // branch back to @1
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _set_vram_update - Set VRAM update pointer
    /// </summary>
    public static Block SetVramUpdate()
    {
        var block = new Block(nameof(NESLib.set_vram_update));
        block.Emit(STA_zpg(NAME_UPD_ADR))
             .Emit(STX_zpg(NAME_UPD_ADR + 1))
             .Emit(ORA_zpg(NAME_UPD_ADR + 1))
             .Emit(STA_zpg(NAME_UPD_ENABLE))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _flush_vram_update - Force VRAM update with full update loop
    /// This is a large subroutine that handles various VRAM update modes
    /// </summary>
    public static Block FlushVramUpdate()
    {
        var block = new Block(nameof(NESLib.flush_vram_update));
        
        // 837F: STA NAME_UPD_ADR, STX NAME_UPD_ADR+1, LDY #$00
        block.Emit(STA_zpg(NAME_UPD_ADR))
             .Emit(STX_zpg(NAME_UPD_ADR + 1))
             .Emit(LDY(0x00));
        
        // @updName (8385): Main update loop start
        block.Emit(LDA_ind_Y(NAME_UPD_ADR), "@updName")
             .Emit(INY())
             .Emit(CMP(0x40))
             .Emit(BCS(0x12))  // BCS @updNotSeq
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDA_ind_Y(NAME_UPD_ADR))
             .Emit(INY())
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDA_ind_Y(NAME_UPD_ADR))
             .Emit(INY())
             .Emit(STA_abs(PPU_DATA))
             .Emit(JMP(nameof(NESConstants.updName)));  // JMP @updName - uses global label
        
        // @updNotSeq (839E)
        block.Emit(TAX())
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(CPX(0x80))
             .Emit(BCC(PAL_BG_PTR))  // BCC @updHorzSeq (8 bytes forward)
             .Emit(CPX(0xFF))
             .Emit(BEQ(0x2A));  // BEQ @updDone
        
        // @updVertSeq (83A9)
        block.Emit(ORA(0x04))
             .Emit(BNE(0x02));  // BNE @updNameSeq
        
        // @updHorzSeq (83AD)
        block.Emit(AND(0xFB));
        
        // @updNameSeq (83AF)
        block.Emit(STA_abs(PPU_CTRL))
             .Emit(TXA())
             .Emit(AND(0x3F))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDA_ind_Y(NAME_UPD_ADR))
             .Emit(INY())
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDA_ind_Y(NAME_UPD_ADR))
             .Emit(INY())
             .Emit(TAX());
        
        // @updNameLoop (83C2)
        block.Emit(LDA_ind_Y(NAME_UPD_ADR), "@updNameLoop")
             .Emit(INY())
             .Emit(STA_abs(PPU_DATA))
             .Emit(DEX())
             .Emit(BNE(0xF7));  // BNE @updNameLoop (0xF7 = -9)
        
        block.Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(STA_abs(PPU_CTRL))
             .Emit(JMP(nameof(NESConstants.updName)));  // JMP @updName - uses global label
        
        // @updDone (83D3)
        block.Emit(RTS());
        
        return block;
    }

    #endregion

    #region Timing Subroutines

    /// <summary>
    /// _nesclock - Read frame counter
    /// </summary>
    public static Block NesClock()
    {
        // NESWriter: LDA __STARTUP__ ($01), LDX #$00, RTS
        var block = new Block(nameof(NESLib.nesclock));
        block.Emit(LDA_zpg(STARTUP))
             .Emit(LDX(0x00))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// _delay - Wait for N frames
    /// </summary>
    public static Block Delay()
    {
        // 8418 TAX           ; _delay
        // 8419 JSR ppu_wait_nmi ; @1
        // 841C DEX
        // 841D BNE @1
        // 8420 RTS
        var block = new Block(nameof(NESLib.delay));
        block.Emit(TAX())
             .Emit(JSR(ppu_wait_nmi), "@1")
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
        var block = new Block(nameof(nmi_set_callback));
        block.Emit(STA_zpg(0x15))  // NMI_CALLBACK+1
             .Emit(STX_zpg(0x16))
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
        // NESWriter: A000 B122 E622 F001 60 E623 60
        var block = new Block(nameof(NESConstants.popa));
        block.Emit(LDY(0x00))
             .Emit(LDA_ind_Y(sp))
             .Emit(INC_zpg(sp))
             .Emit(BEQ(1))     // BEQ +1 to skip RTS
             .Emit(RTS())     // Early return if no overflow
             .Emit(INC_zpg(sp + 1))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// popax - Pop word from stack (A=low, X=high)
    /// Note: This is the start of a sequence that falls through to incsp2
    /// </summary>
    public static Block Popax()
    {
        // NESWriter: A001 B122 AA 88 B122 (falls through to incsp2)
        var block = new Block(nameof(NESConstants.popax));
        block.Emit(LDY(0x01))
             .Emit(LDA_ind_Y(sp))
             .Emit(TAX())
             .Emit(DEY())
             .Emit(LDA_ind_Y(sp));
        // Note: NESWriter falls through to incsp2 here - no RTS
        return block;
    }

    /// <summary>
    /// pusha - Push byte to stack
    /// NESWriter pattern includes pusha0sp/pushaysp prefix
    /// </summary>
    public static Block Pusha()
    {
        // NESWriter: A000 B122 A422 F007 C622 A000 9122 60 C623 C622 9122 60
        // Label at +4 (after pusha0sp/pushaysp prefix)
        var block = new Block(nameof(pusha), labelOffset: 4);
        // pusha0sp prefix
        block.Emit(LDY(0x00))
             // pushaysp
             .Emit(LDA_ind_Y(sp))
             // pusha proper
             .Emit(LDY_zpg(sp))
             .Emit(BEQ(7))     // BEQ to overflow path (PAL_UPDATE = 0x07)
             .Emit(DEC_zpg(sp))
             .Emit(LDY(0x00))
             .Emit(STA_ind_Y(sp))
             .Emit(RTS())
             // overflow path
             .Emit(DEC_zpg(sp + 1))
             .Emit(DEC_zpg(sp))
             .Emit(STA_ind_Y(sp))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// pushax - Push word to stack (A=low, X=high)
    /// NESWriter pattern includes push0/pusha0 prefix
    /// </summary>
    public static Block Pushax()
    {
        // NESWriter: A900 A200 48 A522 38 E902 8522 B002 C623 A001 8A 9122 68 88 9122 60
        // Label at +4 (after push0/pusha0 prefix)
        var block = new Block(nameof(pushax), labelOffset: 4);
        // push0 prefix
        block.Emit(LDA(0x00))
             // pusha0
             .Emit(LDX(0x00))
             // pushax proper
             .Emit(PHA())
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
        // NESWriter: E622 F005 E622 F003 60 E622 E623 60
        var block = new Block(nameof(incsp2));
        block.Emit(INC_zpg(sp))
             .Emit(BEQ(5))     // BEQ to overflow handler 1
             .Emit(INC_zpg(sp))
             .Emit(BEQ(3))     // BEQ to overflow handler 2
             .Emit(RTS())     // Normal return
             .Emit(INC_zpg(sp))  // Overflow handler 1: inc sp again
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
        // NESWriter: A000 F007 A900 A285 4C0003 60
        // This is the cc65 initlib stub - Y=0 means no constructors
        var block = new Block(nameof(initlib));
        block.Emit(LDY(0x00))
             .Emit(BEQ(7))       // PAL_UPDATE = 7, skip to RTS
             .Emit(LDA(0x00))
             .Emit(LDX(0x85))
             .Emit(JMP_abs(condes))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// donelib - Cleanup C runtime library
    /// Note: NESWriter uses totalSize parameter.
    /// </summary>
    public static Block Donelib() => Donelib(0x85FE);

    /// <summary>
    /// donelib - Cleanup C runtime library (parameterized version)
    /// </summary>
    public static Block Donelib(ushort totalSize)
    {
        var block = new Block(nameof(donelib));
        block.Emit(LDY(0x00))
             .Emit(BEQ(7))       // PAL_UPDATE = 7, skip to RTS
             .Emit(LDA((byte)(totalSize & 0xff)))
             .Emit(LDX((byte)(totalSize >> 8)))
             .Emit(JMP_abs(condes))
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// __DESTRUCTOR_TABLE__ - Constructor/Destructor dispatch table
    /// </summary>
    public static Block DestructorTable()
    {
        /*
         * 8602	8D0E03        	STA $030E                     ; __DESTRUCTOR_TABLE__
         * 8605	8E0F03        	STX $030F
         * 8608	8D1503        	STA $0315
         * 860B	8E1603        	STX $0316
         * 860E	88            	DEY
         * 860F	B9FFFF        	LDA $FFFF,y
         * 8612	8D1F03        	STA $031F
         * 8615	88            	DEY
         * 8616	B9FFFF        	LDA $FFFF,y
         * 8619	8D1E03        	STA $031E
         * 861C	8C2103        	STY $0321
         * 861F	20FFFF        	JSR $FFFF
         * 8622	A0FF          	LDY #$FF
         * 8624	D0E8          	BNE $860E
         * 8626	60            	RTS
         */
        var block = new Block(nameof(__DESTRUCTOR_TABLE__));
        block.Emit(STA_abs(0x030E))
             .Emit(STX_abs(0x030F))
             .Emit(STA_abs(0x0315))
             .Emit(STX_abs(0x0316))
             .Emit(DEY(), "@loop")
             .Emit(LDA_abs_Y(0xFFFF))
             .Emit(STA_abs(0x031F))
             .Emit(DEY())
             .Emit(LDA_abs_Y(0xFFFF))
             .Emit(STA_abs(0x031E))
             .Emit(STY_abs(0x0321))
             .Emit(JSR(0xFFFF))
             .Emit(LDY(0xFF))
             .Emit(BNE(-24))  // 0xE8 = -24, back to @loop
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// copydata - Copy initialized data section (simplified)
    /// </summary>
    public static Block Copydata() => Copydata(0x85FE);

    /// <summary>
    /// copydata - Copy initialized data section (parameterized version)
    /// </summary>
    public static Block Copydata(ushort totalSize)
    {
        var block = new Block(nameof(copydata));
        block.Emit(LDA((byte)(totalSize & 0xff)))
             .Emit(STA_zpg(ptr1))
             .Emit(LDA((byte)(totalSize >> 8)))
             .Emit(STA_zpg(ptr1 + 1))
             .Emit(LDA(0x00))
             .Emit(STA_zpg(ptr2))
             .Emit(LDA(0x03))
             .Emit(STA_zpg(ptr2 + 1))
             .Emit(LDX(0xDA))
             .Emit(LDA(0xFF))
             .Emit(STA_zpg(tmp1))
             .Emit(LDY(0x00))
             .Emit(INX(), "@loop1")
             .Emit(BEQ(13))      // 0x0D
             .Emit(LDA_ind_Y(ptr1), "@copyLoop")
             .Emit(STA_ind_Y(ptr2))
             .Emit(INY())
             .Emit(BNE(-10))     // 0xF6
             .Emit(INC_zpg(ptr1 + 1))
             .Emit(INC_zpg(ptr2 + 1))
             .Emit(BNE(-16))     // 0xF0
             .Emit(INC_zpg(tmp1), "@incTmp")
             .Emit(BNE(-17))     // 0xEF
             .Emit(RTS());
        return block;
    }

    /// <summary>
    /// zerobss - Zero BSS section (simplified)
    /// </summary>
    public static Block Zerobss() => Zerobss(0x00);

    /// <summary>
    /// zerobss - Zero BSS section (parameterized version)
    /// </summary>
    public static Block Zerobss(byte locals)
    {
        var block = new Block(nameof(zerobss));
        block.Emit(LDA(0x25))
             .Emit(STA_zpg(ptr1))
             .Emit(LDA(0x03))
             .Emit(STA_zpg(ptr1 + 1))
             .Emit(LDA(0x00))
             .Emit(TAY())
             .Emit(LDX(0x00))
             .Emit(BEQ(10))          // PAL_SPR_PTR = 0x0A
             .Emit(STA_ind_Y(ptr1), "@zeroLoop")
             .Emit(INY())
             .Emit(BNE(-5))          // 0xFB
             .Emit(INC_zpg(ptr1 + 1))
             .Emit(DEX())
             .Emit(BNE(-10))         // 0xF6
             .Emit(CPY(locals), "@checkDone")
             .Emit(BEQ(5))           // 0x05
             .Emit(STA_ind_Y(ptr1))
             .Emit(INY())
             .Emit(BNE(-9))          // 0xF7
             .Emit(RTS());
        return block;
    }

    #endregion

    #region NMI and Update Routines

    /// <summary>
    /// detectNTSC - Detect NTSC/PAL and jump to main
    /// </summary>
    public static Block DetectNTSC()
    {
        var block = new Block(nameof(detectNTSC));
        block.Emit(LDX(0x34))
             .Emit(LDY(0x18))
             .Emit(DEX(), "@loop")
             .Emit(BNE(-3))      // 0xFD
             .Emit(DEY())
             .Emit(BNE(-6))      // 0xFA
             .Emit(LDA_abs(PPU_STATUS))
             .Emit(AND(0x80))
             .Emit(STA_zpg(0x00))
             .Emit(JSR(0x8280))  // ppu_wait_frame
             .Emit(LDA(0x00))
             .Emit(STA_abs(PPU_SCROLL))
             .Emit(STA_abs(PPU_SCROLL))
             .Emit(STA_abs(PPU_OAM_ADDR))
             .Emit(JMP_abs(0x8500));  // main
        return block;
    }

    /// <summary>
    /// doUpdate - Start NMI update sequence
    /// </summary>
    public static Block DoUpdate()
    {
        var block = new Block(nameof(doUpdate));
        block.Emit(LDA(0x02))          // >OAM_BUF
             .Emit(STA_abs(PPU_OAM_DMA))
             .Emit(LDA_zpg(PAL_UPDATE))
             .Emit(BNE(3))             // branch to updPal
             .Emit(JMP_abs(0x81C0));   // updVRAM
        return block;
    }

    /// <summary>
    /// updPal - Update palette in VRAM
    /// </summary>
    public static Block UpdPal()
    {
        var block = new Block(nameof(updPal));
        block.Emit(LDX(0x00))
             .Emit(STX_zpg(PAL_UPDATE))
             .Emit(LDA(0x3F))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(STX_abs(PPU_ADDR))
             // Background color
             .Emit(LDY_abs(PAL_BUF))
             .Emit(LDA_ind_Y(PAL_BG_PTR))
             .Emit(STA_abs(PPU_DATA))
             .Emit(TAX());
        // First 3 palette entries
        for (int i = 1; i <= 3; i++)
        {
            block.Emit(LDY_abs((ushort)(PAL_BUF + i)))
                 .Emit(LDA_ind_Y(PAL_BG_PTR))
                 .Emit(STA_abs(PPU_DATA));
        }
        // Remaining background palettes (3 sets of 4)
        for (int j = 1; j <= 3; j++)
        {
            block.Emit(STX_abs(PPU_DATA));  // background color
            for (int i = 1; i <= 3; i++)
            {
                block.Emit(LDY_abs((ushort)(PAL_BUF + (j * 4) + i)))
                     .Emit(LDA_ind_Y(PAL_BG_PTR))
                     .Emit(STA_abs(PPU_DATA));
            }
        }
        // Sprite palettes (4 sets of 4)
        for (int j = 1; j <= 4; j++)
        {
            block.Emit(STX_abs(PPU_DATA));  // background color
            for (int i = 1; i <= 3; i++)
            {
                block.Emit(LDY_abs((ushort)(PAL_BUF + 12 + (j * 4) + i)))
                     .Emit(LDA_ind_Y(PAL_SPR_PTR))
                     .Emit(STA_abs(PPU_DATA));
            }
        }
        return block;
    }

    /// <summary>
    /// updVRAM - Update VRAM if needed
    /// </summary>
    public static Block UpdVRAM()
    {
        var block = new Block(nameof(updVRAM));
        block.Emit(LDA_zpg(VRAM_UPDATE))
             .Emit(BEQ(11))        // 0x0B - skip to skipUpd
             .Emit(LDA(0x00))
             .Emit(STA_zpg(VRAM_UPDATE))
             .Emit(LDA_zpg(NAME_UPD_ENABLE))
             .Emit(BEQ(3))         // 0x03 - skip to skipUpd
             .Emit(JSR(0x8383));   // _flush_vram_update_nmi
        return block;
    }

    /// <summary>
    /// skipUpd - Reset PPU scroll after update
    /// </summary>
    public static Block SkipUpd()
    {
        var block = new Block(nameof(skipUpd));
        block.Emit(LDA(0x00))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(STA_abs(PPU_ADDR))
             .Emit(LDA_zpg(SCROLL_X))
             .Emit(STA_abs(PPU_SCROLL))
             .Emit(LDA_zpg(SCROLL_Y))
             .Emit(STA_abs(PPU_SCROLL))
             .Emit(LDA_zpg(PRG_FILEOFFS))
             .Emit(STA_abs(PPU_CTRL));
        return block;
    }

    /// <summary>
    /// skipAll - Update PPU mask and frame counter
    /// </summary>
    public static Block SkipAll()
    {
        var block = new Block(nameof(skipAll));
        block.Emit(LDA_zpg(PPU_MASK_VAR))
             .Emit(STA_abs(PPU_MASK))
             .Emit(INC_zpg(STARTUP))
             .Emit(INC_zpg(NES_PRG_BANKS))
             .Emit(LDA_zpg(NES_PRG_BANKS))
             .Emit(CMP(0x06))
             .Emit(BNE(4))         // skip to skipNtsc
             .Emit(LDA(0x00))
             .Emit(STA_zpg(NES_PRG_BANKS));
        return block;
    }

    /// <summary>
    /// skipNtsc - Finish NMI, call callback and return
    /// </summary>
    public static Block SkipNtsc()
    {
        var block = new Block(nameof(NESConstants.skipNtsc));
        block.Emit(JSR(0x0014))    // NMICallback (at $0014)
             .Emit(PLA())
             .Emit(TAY())
             .Emit(PLA())
             .Emit(TAX())
             .Emit(PLA())
             .Emit(RTI());
        return block;
    }

    /// <summary>
    /// clearRAM - Clear RAM and initialize system
    /// Note: Uses fixed addresses for labels (pal_bright=0x8279, pal_clear=0x824E, oam_clear=0x82AE, initlib=0x84F4)
    /// </summary>
    public static Block ClearRAM()
    {
        var block = new Block(nameof(clearRAM));
        block.Emit(TXA())
             .Emit(STA_zpg_X(0x00), "@loop")
             .Emit(STA_abs_X(0x0100))
             .Emit(STA_abs_X(0x0200))
             .Emit(STA_abs_X(0x0300))
             .Emit(STA_abs_X(0x0400))
             .Emit(STA_abs_X(0x0500))
             .Emit(STA_abs_X(0x0600))
             .Emit(STA_abs_X(0x0700))
             .Emit(INX())
             .Emit(BNE(-26))       // 0xE6 - back to @loop
             .Emit(LDA(0x04))
             .Emit(JSR(0x8279))    // pal_bright
             .Emit(JSR(0x824E))    // pal_clear
             .Emit(JSR(0x82AE))    // oam_clear
             .Emit(JSR("zerobss"))
             .Emit(JSR("copydata"))
             .Emit(LDA(0x00))
             .Emit(STA_zpg(sp))
             .Emit(LDA(PAL_BG_PTR))  // 0x08 = stack high byte
             .Emit(STA_zpg(sp + 1))
             .Emit(JSR(0x84F4))    // initlib
             .Emit(LDA(0x4C))      // JMP opcode
             .Emit(STA_zpg(0x14))
             .Emit(LDA(0x10))      // low byte of callback
             .Emit(STA_zpg(0x15))
             .Emit(LDA(0x82))      // high byte of callback
             .Emit(STA_zpg(0x16))
             .Emit(LDA(0x80))      // PPU_CTRL setting
             .Emit(STA_zpg(0x10))
             .Emit(STA_abs(PPU_CTRL))
             .Emit(LDA(0x06))      // PPU_MASK setting
             .Emit(STA_zpg(0x12));
        return block;
    }

    #endregion

    #region Pad Polling

    /// <summary>
    /// _pad_poll - Poll controller state
    /// </summary>
    public static Block PadPoll()
    {
        var block = new Block(nameof(NESLib.pad_poll));
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
