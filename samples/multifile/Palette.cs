using static NES.NESLib;

/// <summary>
/// Palette setup helpers.
/// </summary>
static class Palette
{
    public static void setup()
    {
        pal_col(0, 0x02);   // set screen to dark blue
        pal_col(1, 0x14);   // fuchsia
        pal_col(2, 0x20);   // grey
        pal_col(3, 0x30);   // white
    }
}
