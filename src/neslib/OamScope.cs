namespace NES;

/// <summary>
/// RAII scope for OAM sprite drawing.
/// Constructor calls oam_clear() and resets oam_off.
/// Dispose() calls oam_hide_rest(oam_off).
/// Instance methods auto-manage oam_off internally.
/// </summary>
public ref struct OamScope
{
    /// <summary>
    /// Begin OAM sprite drawing. Clears OAM buffer and resets oam_off to 0.
    /// </summary>
    public OamScope() => throw null!;

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
