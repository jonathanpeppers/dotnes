namespace NES;

/// <summary>
/// RAII scope for OAM sprite drawing.
/// Dispose() calls oam_hide_rest(oam_off).
/// Instance methods auto-manage oam_off internally.
/// </summary>
public ref struct OamFrame
{
    /// <summary>
    /// Set a single sprite in OAM buffer. Manages oam_off automatically.
    /// </summary>
    public void spr(byte x, byte y, byte chrnum, byte attr) => throw null!;

    /// <summary>
    /// Set a metasprite in OAM buffer. Manages oam_off automatically.
    /// </summary>
    public void meta_spr(byte x, byte y, byte[] data) => throw null!;

    /// <summary>
    /// Set a metasprite in OAM buffer with palette override. Manages oam_off automatically.
    /// </summary>
    public void meta_spr_pal(byte x, byte y, byte pal, byte[] data) => throw null!;

    public void Dispose() => throw null!;
}
