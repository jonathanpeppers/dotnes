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
    public const int TEMP = 0x17;
    public const int sp = 0x22;
    public const int ptr1 = 0x2A;
    public const int ptr2 = 0x2C;
    public const int tmp1 = 0x32;
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
    public const ushort DMC_FREQ = 0x4010;
    public const ushort PPU_OAM_DMA = 0x4014;
    public const ushort PPU_FRAMECNT = 0x4017;

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
}
