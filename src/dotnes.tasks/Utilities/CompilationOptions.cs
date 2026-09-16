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
    /// Declared number of 16 KiB PRG ROM banks, used to validate managed regions
    /// and reserved fixed banks. Defaults to 2.
    /// </summary>
    public int PrgBanks { get; set; } = 2;

    /// <summary>
    /// Places the program at $C000 in the MMC3 fixed-bank layout instead of $8000.
    /// Requires mapper 4. Defaults to false. ROM packaging, bank assets and reset
    /// stub generation remain the responsibility of the ROM build pipeline.
    /// </summary>
    public bool Mmc3BankedLayout { get; set; }

    /// <summary>
    /// Named managed-method reservations in switchable MMC3 R6 banks.
    /// Empty by default; requires mapper 4 and Mmc3BankedLayout when configured.
    /// </summary>
    public IList<ManagedCodeBank> ManagedCodeBanks { get; } = new List<ManagedCodeBank>();

    /// <summary>
    /// Explicit native assembly or binary assets in physical MMC3 PRG banks.
    /// Empty by default. Uses the same placement rules as NESPrgBank items.
    /// </summary>
    public IList<PrgBankAsset> PrgBankAssets { get; } = new List<PrgBankAsset>();

    /// <summary>
    /// Explicit caller-guaranteed foreground native PRG-RAM entry contracts.
    /// Empty by default; unknown RAM calls remain unsupported.
    /// </summary>
    public IList<NativeRamCode> NativeRamCode { get; } = new List<NativeRamCode>();

    /// <summary>
    /// Physical 8 KiB R6 home bank initialized at startup and restored by gates.
    /// Must be explicitly configured when managed regions are present.
    /// </summary>
    public int? Mmc3ManagedHomeBank { get; set; }

    /// <summary>
    /// Native interrupt callback contract for managed banking. Defaults to None.
    /// </summary>
    public Mmc3ManagedInterruptContract Mmc3ManagedInterruptContract { get; set; }

    /// <summary>
    /// Uses private RAM parameter homes for proven non-reentrant small byte helpers.
    /// Defaults to false. Unproven helpers and native entry points retain standard storage.
    /// </summary>
    public bool OptimizeByteHelpers { get; set; }
}
