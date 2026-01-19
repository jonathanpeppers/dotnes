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
    public void InitPPU_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.InitPPU();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // BIT $2002, BIT $2002, BPL -5, BIT $2002, BPL -5, LDA #$40, STA $4017
        Assert.Equal(
            [0x2C, 0x02, 0x20, 0x2C, 0x02, 0x20, 0x10, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB, 0xA9, 0x40, 0x8D, 0x17, 0x40],
            bytes);
    }

    [Fact]
    public void ClearPalette_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.ClearPalette();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA #$3F, STA $2006, STX $2006, LDA #$0F, LDX #$20, STA $2007, DEX, BNE -6 (0xFA)
        Assert.Equal(
            [0xA9, 0x3F, 0x8D, 0x06, 0x20, 0x8E, 0x06, 0x20, 0xA9, 0x0F, 0xA2, 0x20, 0x8D, 0x07, 0x20, 0xCA, 0xD0, 0xFA],
            bytes);
    }

    [Fact]
    public void ClearVRAM_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.ClearVRAM();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // TXA, LDY #$20, STY $2006, STA $2006, LDY #$10, STA $2007, INX, BNE -6 (0xFA), DEY, BNE -9 (0xF7)
        Assert.Equal(
            [0x8A, 0xA0, 0x20, 0x8C, 0x06, 0x20, 0x8D, 0x06, 0x20, 0xA0, 0x10, 0x8D, 0x07, 0x20, 0xE8, 0xD0, 0xFA, 0x88, 0xD0, 0xF7],
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

        // PHA, TXA, PHA, TYA, PHA, LDA #$FF, JMP $81F9 (skipNtsc constant)
        Assert.Equal([0x48, 0x8A, 0x48, 0x98, 0x48, 0xA9, 0xFF, 0x4C, 0xF9, 0x81], bytes);
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
    public void PalBg_WithPalCopy_ProducesCorrectBranch()
    {
        // Test that PalBg correctly branches to pal_copy
        var program = new Program6502 { BaseAddress = 0x8211 };
        program.AddBlock(BuiltInSubroutines.PalAll());  // 8 bytes at 0x8211
        program.AddBlock(BuiltInSubroutines.PalCopy()); // 18 bytes at 0x8219
        program.AddBlock(BuiltInSubroutines.PalBg());   // 9 bytes at 0x822B (branches back to 0x8219)
        program.ResolveAddresses();

        var bytes = program.ToBytes();
        
        // PalBg should be: STA $17, STX $18, LDX #$00, LDA #$10, BNE offset_to_pal_copy
        // At 0x822B: bytes [0..3] are 85 17 86 18, [4..5] A2 00, [6..7] A9 10, [8..9] D0 xx
        // The BNE at 0x8233 needs to branch back to 0x8219 (pal_copy)
        // Offset = 0x8219 - 0x8235 = -28 = 0xE4
        var palBgStart = 8 + 18; // after PalAll and PalCopy
        Assert.Equal(0xD0, bytes[palBgStart + 8]); // BNE opcode
        Assert.Equal(0xE4, bytes[palBgStart + 9]); // Branch offset -28
    }

    [Fact]
    public void PpuOnBg_WithPpuOnOff_ProducesCorrectBranch()
    {
        // Test that PpuOnBg correctly branches to ppu_onoff
        var program = new Program6502 { BaseAddress = 0x8289 };
        program.DefineExternalLabel("ppu_wait_nmi", 0x82F0);
        program.AddBlock(BuiltInSubroutines.PpuOnAll());  // 4 bytes at 0x8289
        program.AddBlock(BuiltInSubroutines.PpuOnOff()); // 5 bytes at 0x828D
        program.AddBlock(BuiltInSubroutines.PpuOnBg());  // 5 bytes at 0x8292 (branches back to 0x828D)
        program.ResolveAddresses();

        var bytes = program.ToBytes();
        
        // PpuOnBg: LDA $12, ORA #$08, BNE offset_to_ppu_onoff
        // At 0x8292: [0..1] A5 12, [2..3] 09 08, [4..5] D0 xx
        // BNE at 0x8296 branches to 0x828D
        // Offset = 0x828D - 0x8298 = -11 = 0xF5
        var ppuOnBgStart = 4 + 5; // after PpuOnAll and PpuOnOff
        Assert.Equal(0xD0, bytes[ppuOnBgStart + 4]); // BNE opcode
        Assert.Equal(0xF5, bytes[ppuOnBgStart + 5]); // Branch offset -11
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

        // NESWriter: LDA $10, LDX #$00, RTS
        Assert.Equal([0xA5, 0x10, 0xA2, 0x00, 0x60], bytes);
    }

    [Fact]
    public void SetPpuCtrlVar_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.SetPpuCtrlVar();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: STA $10, RTS
        Assert.Equal([0x85, 0x10, 0x60], bytes);
    }

    [Fact]
    public void PpuSystem_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuSystem();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: LDA $00, LDX #$00, RTS
        Assert.Equal([0xA5, 0x00, 0xA2, 0x00, 0x60], bytes);
    }

    [Fact]
    public void PpuWaitNmi_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuWaitNmi();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA #$01, STA $03, LDA $01, CMP $01, BEQ -4 ($FC), RTS
        Assert.Equal([0xA9, 0x01, 0x85, 0x03, 0xA5, 0x01, 0xC5, 0x01, 0xF0, 0xFC, 0x60], bytes);
    }

    [Fact]
    public void PpuWaitFrame_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PpuWaitFrame();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // LDA #$01, STA $03, LDA $01, CMP $01, BEQ -4, LDA $00, BEQ +6, LDA $02, CMP #$05, BEQ -6, RTS
        Assert.Equal([0xA9, 0x01, 0x85, 0x03, 0xA5, 0x01, 0xC5, 0x01, 0xF0, 0xFC, 
                      0xA5, 0x00, 0xF0, 0x06, 0xA5, 0x02, 0xC9, 0x05, 0xF0, 0xFA, 0x60], bytes);
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

        // NESWriter: LDX #$00, LDA #$FF, STA $0200,X, INX, INX, INX, INX, BNE -9 ($F7), RTS
        Assert.Equal(
            [0xA2, 0x00, 0xA9, 0xFF, 0x9D, 0x00, 0x02, 0xE8, 0xE8, 0xE8, 0xE8, 0xD0, 0xF7, 0x60],
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

        // NESWriter: TAX, LDA #$F0, STA $0200,X, INX, INX, INX, INX, BNE -9 ($F7), RTS
        Assert.Equal(
            [0xAA, 0xA9, 0xF0, 0x9D, 0x00, 0x02, 0xE8, 0xE8, 0xE8, 0xE8, 0xD0, 0xF7, 0x60],
            bytes);
    }

    #endregion

    #region Scroll and Bank Subroutines

    [Fact]
    public void Scroll_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Scroll();
        var program = new Program6502 { BaseAddress = 0x8000 };
        // Scroll uses JSR("popax") which needs external label resolution
        program.DefineExternalLabel("popax", 0x8539);
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // Full scroll implementation matching NESWriter:
        // 85 17 8A D0 0E A5 17 C9 F0 B0 08 85 0D A9 00 85 17 F0 0B
        // 38 A5 17 E9 F0 85 0D A9 02 85 17 20 39 85 85 0C 8A 29 01
        // 05 17 85 17 A5 10 29 FC 05 17 85 10 60
        Assert.Equal([
            0x85, 0x17,             // STA TEMP
            0x8A,                   // TXA
            0xD0, 0x0E,             // BNE +14
            0xA5, 0x17,             // LDA TEMP
            0xC9, 0xF0,             // CMP #$F0
            0xB0, 0x08,             // BCS +8
            0x85, 0x0D,             // STA SCROLL_Y
            0xA9, 0x00,             // LDA #$00
            0x85, 0x17,             // STA TEMP
            0xF0, 0x0B,             // BEQ +11
            0x38,                   // SEC
            0xA5, 0x17,             // LDA TEMP
            0xE9, 0xF0,             // SBC #$F0
            0x85, 0x0D,             // STA SCROLL_Y
            0xA9, 0x02,             // LDA #$02
            0x85, 0x17,             // STA TEMP
            0x20, 0x39, 0x85,       // JSR popax
            0x85, 0x0C,             // STA SCROLL_X
            0x8A,                   // TXA
            0x29, 0x01,             // AND #$01
            0x05, 0x17,             // ORA TEMP
            0x85, 0x17,             // STA TEMP
            0xA5, 0x10,             // LDA PRG_FILEOFFS
            0x29, 0xFC,             // AND #$FC
            0x05, 0x17,             // ORA TEMP
            0x85, 0x10,             // STA PRG_FILEOFFS
            0x60                    // RTS
        ], bytes);
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

        // LDA $01 (STARTUP), LDX #$00, RTS
        Assert.Equal([0xA5, 0x01, 0xA2, 0x00, 0x60], bytes);
    }

    [Fact]
    public void NmiSetCallback_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.NmiSetCallback();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // STA $15, STX $16, RTS
        Assert.Equal([0x85, 0x15, 0x86, 0x16, 0x60], bytes);
    }

    [Fact]
    public void PadPoll_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.PadPoll();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // Expected bytes from NESWriter.Write_pad_poll()
        byte[] expected = [
            0xA8,                   // TAY
            0xA2, 0x00,             // LDX #$00
            0xA9, 0x01,             // LDA #$01
            0x8D, 0x16, 0x40,       // STA $4016
            0xA9, 0x00,             // LDA #$00
            0x8D, 0x16, 0x40,       // STA $4016
            0xA9, 0x08,             // LDA #$08
            0x85, 0x17,             // STA TEMP
            0xB9, 0x16, 0x40,       // LDA $4016,y
            0x4A,                   // LSR
            0x76, 0x18,             // ROR $18,x (TEMP+1)
            0xC6, 0x17,             // DEC TEMP
            0xD0, 0xF6,             // BNE -10
            0xE8,                   // INX
            0xE0, 0x03,             // CPX #$03
            0xD0, 0xE3,             // BNE -29
            0xA5, 0x18,             // LDA $18 (TEMP+1)
            0xC5, 0x19,             // CMP $19
            0xF0, 0x06,             // BEQ +6
            0xC5, 0x1A,             // CMP $1A
            0xF0, 0x02,             // BEQ +2
            0xA5, 0x19,             // LDA $19
            0x99, 0x3C, 0x00,       // STA $003C,y
            0xAA,                   // TAX
            0x59, 0x3E, 0x00,       // EOR $003E,y
            0x39, 0x3C, 0x00,       // AND $003C,y
            0x99, 0x40, 0x00,       // STA $0040,y
            0x8A,                   // TXA
            0x99, 0x3E, 0x00,       // STA $003E,y
            0xA2, 0x00,             // LDX #$00
            0x60                    // RTS
        ];

        Assert.Equal(expected, bytes);
    }

    #endregion

    #region Initialization and Sync

    [Fact]
    public void WaitSync3_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.WaitSync3();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: A501 C501 F0FC
        // LDA $01, CMP $01, BEQ -4
        Assert.Equal([0xA5, 0x01, 0xC5, 0x01, 0xF0, 0xFC], bytes);
    }

    [Fact]
    public void Nmi_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Nmi();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: 48 8A 48 98 48 A512 2918 D003 4CE681
        // PHA, TXA, PHA, TYA, PHA, LDA $12, AND #$18, BNE +3, JMP $81E6
        Assert.Equal([0x48, 0x8A, 0x48, 0x98, 0x48, 0xA5, 0x12, 0x29, 0x18, 0xD0, 0x03, 0x4C, 0xE6, 0x81], bytes);
    }

    #endregion

    #region Stack Operations

    [Fact]
    public void Popax_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Popax();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: A001 B122 AA 88 B122 (falls through to incsp2)
        // LDY #$01, LDA ($22),Y, TAX, DEY, LDA ($22),Y
        Assert.Equal([0xA0, 0x01, 0xB1, 0x22, 0xAA, 0x88, 0xB1, 0x22], bytes);
    }

    [Fact]
    public void Pushax_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Pushax();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: A900 A200 48 A522 38 E902 8522 B002 C623 A001 8A 9122 68 88 9122 60
        // LDA #$00, LDX #$00, PHA, LDA $22, SEC, SBC #$02, STA $22, BCS +2, DEC $23, LDY #$01, TXA, STA ($22),Y, PLA, DEY, STA ($22),Y, RTS
        Assert.Equal(
            [0xA9, 0x00, 0xA2, 0x00, 0x48, 0xA5, 0x22, 0x38, 0xE9, 0x02, 0x85, 0x22, 0xB0, 0x02, 0xC6, 0x23, 0xA0, 0x01, 0x8A, 0x91, 0x22, 0x68, 0x88, 0x91, 0x22, 0x60],
            bytes);
    }

    [Fact]
    public void Popa_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Popa();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: A000 B122 E622 F001 60 E623 60
        // LDY #$00, LDA ($22),Y, INC $22, BEQ +1, RTS, INC $23, RTS
        Assert.Equal(
            [0xA0, 0x00, 0xB1, 0x22, 0xE6, 0x22, 0xF0, 0x01, 0x60, 0xE6, 0x23, 0x60],
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

        // NESWriter: A000 B122 A422 F007 C622 A000 9122 60 C623 C622 9122 60
        // LDY #$00, LDA ($22),Y, LDY $22, BEQ +7, DEC $22, LDY #$00, STA ($22),Y, RTS, DEC $23, DEC $22, STA ($22),Y, RTS
        Assert.Equal(
            [0xA0, 0x00, 0xB1, 0x22, 0xA4, 0x22, 0xF0, 0x07, 0xC6, 0x22, 0xA0, 0x00, 0x91, 0x22, 0x60, 0xC6, 0x23, 0xC6, 0x22, 0x91, 0x22, 0x60],
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

        // NESWriter: E622 F005 E622 F003 60 E622 E623 60
        // INC $22, BEQ +5, INC $22, BEQ +3, RTS, INC $22, INC $23, RTS
        Assert.Equal(
            [0xE6, 0x22, 0xF0, 0x05, 0xE6, 0x22, 0xF0, 0x03, 0x60, 0xE6, 0x22, 0xE6, 0x23, 0x60],
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

        // NESWriter: A000 F007 A900 A285 4C0003 60
        // LDY #$00, BEQ +7, LDA #$00, LDX #$85, JMP $0300, RTS
        Assert.Equal([0xA0, 0x00, 0xF0, 0x07, 0xA9, 0x00, 0xA2, 0x85, 0x4C, 0x00, 0x03, 0x60], bytes);
    }

    [Fact]
    public void Donelib_ProducesCorrectBytes()
    {
        var block = BuiltInSubroutines.Donelib();
        var program = new Program6502 { BaseAddress = 0x8000 };
        program.AddBlock(block);
        program.ResolveAddresses();

        var bytes = program.ToBytes();

        // NESWriter: A000 F007 A9FE A285 4C0003 60
        // LDY #$00, BEQ +7, LDA #$FE, LDX #$85, JMP $0300, RTS
        Assert.Equal([0xA0, 0x00, 0xF0, 0x07, 0xA9, 0xFE, 0xA2, 0x85, 0x4C, 0x00, 0x03, 0x60], bytes);
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
        Assert.Equal("pal_all", BuiltInSubroutines.PalAll().Label);
        Assert.Equal("pal_copy", BuiltInSubroutines.PalCopy().Label);
        Assert.Equal("pal_bg", BuiltInSubroutines.PalBg().Label);
        Assert.Equal("pal_spr", BuiltInSubroutines.PalSpr().Label);
        Assert.Equal("pal_col", BuiltInSubroutines.PalCol().Label);
        Assert.Equal("pal_clear", BuiltInSubroutines.PalClear().Label);
        Assert.Equal("pal_spr_bright", BuiltInSubroutines.PalSprBright().Label);
        Assert.Equal("pal_bg_bright", BuiltInSubroutines.PalBgBright().Label);
        Assert.Equal("pal_bright", BuiltInSubroutines.PalBright().Label);
        Assert.Equal("ppu_off", BuiltInSubroutines.PpuOff().Label);
        Assert.Equal("ppu_on_all", BuiltInSubroutines.PpuOnAll().Label);
        Assert.Equal("ppu_onoff", BuiltInSubroutines.PpuOnOff().Label);
        Assert.Equal("ppu_on_bg", BuiltInSubroutines.PpuOnBg().Label);
        Assert.Equal("ppu_on_spr", BuiltInSubroutines.PpuOnSpr().Label);
        Assert.Equal("ppu_mask", BuiltInSubroutines.PpuMask().Label);
        Assert.Equal("ppu_wait_nmi", BuiltInSubroutines.PpuWaitNmi().Label);
        Assert.Equal("ppu_wait_frame", BuiltInSubroutines.PpuWaitFrame().Label);
        Assert.Equal("ppu_system", BuiltInSubroutines.PpuSystem().Label);
        Assert.Equal("get_ppu_ctrl_var", BuiltInSubroutines.GetPpuCtrlVar().Label);
        Assert.Equal("set_ppu_ctrl_var", BuiltInSubroutines.SetPpuCtrlVar().Label);
        Assert.Equal("oam_clear", BuiltInSubroutines.OamClear().Label);
        Assert.Equal("oam_size", BuiltInSubroutines.OamSize().Label);
        Assert.Equal("oam_hide_rest", BuiltInSubroutines.OamHideRest().Label);
        Assert.Equal("oam_spr", BuiltInSubroutines.OamSpr().Label);
        Assert.Equal("scroll", BuiltInSubroutines.Scroll().Label);
        Assert.Equal("bank_spr", BuiltInSubroutines.BankSpr().Label);
        Assert.Equal("bank_bg", BuiltInSubroutines.BankBg().Label);
        Assert.Equal("vram_adr", BuiltInSubroutines.VramAdr().Label);
        Assert.Equal("vram_put", BuiltInSubroutines.VramPut().Label);
        Assert.Equal("vram_fill", BuiltInSubroutines.VramFill().Label);
        Assert.Equal("vram_inc", BuiltInSubroutines.VramInc().Label);
        Assert.Equal("vram_write", BuiltInSubroutines.VramWrite().Label);
        Assert.Equal("set_vram_update", BuiltInSubroutines.SetVramUpdate().Label);
        Assert.Equal("flush_vram_update", BuiltInSubroutines.FlushVramUpdate().Label);
        Assert.Equal("nesclock", BuiltInSubroutines.NesClock().Label);
        Assert.Equal("delay", BuiltInSubroutines.Delay().Label);
        Assert.Equal("nmi_set_callback", BuiltInSubroutines.NmiSetCallback().Label);
        Assert.Equal("popa", BuiltInSubroutines.Popa().Label);
        Assert.Equal("popax", BuiltInSubroutines.Popax().Label);
        Assert.Equal("pusha", BuiltInSubroutines.Pusha().Label);
        Assert.Equal("pushax", BuiltInSubroutines.Pushax().Label);
        Assert.Equal("incsp2", BuiltInSubroutines.Incsp2().Label);
        Assert.Equal("initlib", BuiltInSubroutines.Initlib().Label);
        Assert.Equal("donelib", BuiltInSubroutines.Donelib().Label);
        Assert.Equal("copydata", BuiltInSubroutines.Copydata().Label);
        Assert.Equal("zerobss", BuiltInSubroutines.Zerobss().Label);
        Assert.Equal("pad_poll", BuiltInSubroutines.PadPoll().Label);
    }

    #endregion
}
