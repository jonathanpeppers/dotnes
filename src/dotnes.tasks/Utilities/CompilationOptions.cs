namespace dotnes;

/// <summary>
/// Target options for compiling a 6502 program, independent of iNES image packaging.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// iNES mapper number (0-255). Defaults to 0 (NROM).
    /// A mapper number alone does not enable banked placement or insert bank-switching code.
    /// </summary>
    public int Mapper { get; set; }

    /// <summary>
    /// Places the program at $C000 in the MMC3 fixed-bank layout instead of $8000.
    /// Requires mapper 4. Defaults to false. ROM packaging, bank assets and reset
    /// stub generation remain the responsibility of the ROM build pipeline.
    /// </summary>
    public bool Mmc3BankedLayout { get; set; }
}
