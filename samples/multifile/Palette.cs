using static NES.NESLib;
using static NES.NESColor;

/// <summary>
/// Palette setup helpers.
/// </summary>
static class Palette
{
    public static void setup()
    {
        pal_col(0, DarkBlue);
        pal_col(1, Magenta);
        pal_col(2, LightGray);
        pal_col(3, White);
    }
}
