using static NES.NESLib;

/// <summary>
/// Display/PPU helpers.
/// </summary>
static class Display
{
    public static void write_message()
    {
        vram_adr(NTADR_A(2, 2));
        vram_write("HELLO, MULTI-FILE!");
    }

    public static void enable()
    {
        ppu_on_all();
    }
}
