namespace dotnes;

/// <summary>
/// Constants for NES memory addresses and zero-page variables.
/// These are shared between NESWriter and Program6502Writer.
/// </summary>
internal static class NESConstants
{
    // Zero-page variables (neslib)
    public const int ZP_START = 0x00;
    public const int STARTUP = 0x01;
    public const int NES_PRG_BANKS = 0x02;
    public const int VRAM_UPDATE = 0x03;
    public const int NAME_UPD_ADR = 0x04;
    public const int NAME_UPD_ENABLE = 0x06;
    public const int PAL_UPDATE = 0x07;
    public const int PAL_BG_PTR = 0x08;
    public const int PAL_SPR_PTR = 0x0A;
    public const int SCROLL_X = 0x0C;
    public const int SCROLL_Y = 0x0D;
    public const int NMI_CALLBACK = 0x14;  // 3 bytes: JMP opcode + address ($14-$16)
    public const int TEMP = 0x17;
    public const int RAND_SEED = 0x3C;  // 1 byte: random seed for LFSR PRNG (cc65 compatible)
    public const int TEMP2 = 0x19;  // Additional temporary variable
    public const int TEMP3 = 0x1A;  // Additional temporary variable
    public const int OAM_OFF = 0x1B; // OAM buffer offset (neslib oam_off global)
    public const int sp = 0x22;
    public const int ptr1 = 0x2A;
    public const int ptr2 = 0x2C;
    public const int tmp1 = 0x32;
    public const int RLE_LOW = 0x2E;
    public const int RLE_HIGH = 0x2F;
    public const int RLE_TAG = 0x30;
    public const int RLE_BYTE = 0x31;
    public const int PRG_FILEOFFS = 0x10;
    public const int PPU_MASK_VAR = 0x12;

    // RAM buffers
    public const ushort OAM_BUF = 0x0200;
    public const ushort PAL_BUF = 0x01C0;
    public const ushort condes = 0x0300;

    // PPU registers
    public const ushort PPU_CTRL = 0x2000;
    public const ushort PPU_MASK = 0x2001;
    public const ushort PPU_STATUS = 0x2002;
    public const ushort PPU_OAM_ADDR = 0x2003;
    public const ushort PPU_OAM_DATA = 0x2004;
    public const ushort PPU_SCROLL = 0x2005;
    public const ushort PPU_ADDR = 0x2006;
    public const ushort PPU_DATA = 0x2007;

    // APU/IO registers
    public const ushort APU_PULSE1_CTRL = 0x4000;
    public const ushort APU_PULSE1_SWEEP = 0x4001;
    public const ushort APU_PULSE1_TIMER_LO = 0x4002;
    public const ushort APU_PULSE1_TIMER_HI = 0x4003;
    public const ushort APU_PULSE2_CTRL = 0x4004;
    public const ushort APU_PULSE2_SWEEP = 0x4005;
    public const ushort APU_PULSE2_TIMER_LO = 0x4006;
    public const ushort APU_PULSE2_TIMER_HI = 0x4007;
    public const ushort APU_TRIANGLE_CTRL = 0x4008;
    public const ushort APU_TRIANGLE_TIMER_LO = 0x400A;
    public const ushort APU_TRIANGLE_TIMER_HI = 0x400B;
    public const ushort APU_STATUS = 0x4015;
    public const ushort DMC_FREQ = 0x4010;
    public const ushort PPU_OAM_DMA = 0x4014;
    public const ushort PPU_FRAMECNT = 0x4017;

    // Music engine state (cc65-compatible BSS at $0300+)
    public const ushort MUSIC_DURATION = 0x0300;  // 1 byte: frames until next note
    public const ushort MUSIC_PTR = 0x0301;       // 2 bytes: current position in music data
    public const ushort MUSIC_CHS = 0x0303;       // 1 byte: channel usage bitmask (static)
    public const ushort MUSIC_TEMP = 0x0329;      // 1 byte: temp note value
    public const ushort MUSIC_PERIOD_LO = 0x032A; // 1 byte: temp period low
    public const ushort MUSIC_PERIOD_HI = 0x032B; // 1 byte: temp period high
    public const ushort MUSIC_TRI_PERIOD_LO = 0x032C; // 1 byte: triangle period low
    public const ushort MUSIC_TRI_PERIOD_HI = 0x032D; // 1 byte: triangle period high

    // Built-in subroutine addresses (resolved after linking)
    public const ushort skipNtsc = 0x81F9;
    public const ushort pal_col = 0x823E;
    public const ushort pal_spr_bright = 0x825D;
    public const ushort pal_bg_bright = 0x826B;
    public const ushort vram_adr = 0x83D4;
    public const ushort vram_write = 0x834F;
    public const ushort ppu_on_all = 0x8289;
    public const ushort ppu_wait_nmi = 0x82F0;
    public const ushort updName = 0x8385;
    public const ushort palBrightTableL = 0x8422;
    public const ushort palBrightTableH = 0x842B;
    public const ushort popax = 0x8539;
    public const ushort popa = 0x854F;

    #region Label name constants (for nameof() usage in code generation)
    // cc65 runtime - use nameof(pusha) etc.
    public const string pusha = nameof(pusha);
    public const string pushax = nameof(pushax);
    public const string incsp2 = nameof(incsp2);
    public const string decsp4 = nameof(decsp4);
    
    // NMI/Update labels
    public const string updPal = nameof(updPal);
    public const string updVRAM = nameof(updVRAM);
    public const string doUpdate = nameof(doUpdate);
    public const string skipUpd = nameof(skipUpd);
    public const string skipAll = nameof(skipAll);
    
    // Initialization labels
    public const string clearRAM = nameof(clearRAM);
    public const string zerobss = nameof(zerobss);
    public const string copydata = nameof(copydata);
    public const string initlib = nameof(initlib);
    public const string donelib = nameof(donelib);
    public const string detectNTSC = nameof(detectNTSC);
    
    // System/CRT labels
    public const string _exit = nameof(_exit);
    public const string _initPPU = nameof(_initPPU);
    public const string _clearPalette = nameof(_clearPalette);
    public const string _clearVRAM = nameof(_clearVRAM);
    public const string _waitSync3 = nameof(_waitSync3);
    public const string _nmi = nameof(_nmi);
    public const string _irq = nameof(_irq);
    
    // Special labels
    public const string nmi_set_callback = nameof(nmi_set_callback);
    public const string __DESTRUCTOR_TABLE__ = nameof(__DESTRUCTOR_TABLE__);
    #endregion
}
