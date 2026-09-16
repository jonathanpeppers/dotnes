namespace dotnes;

/// <summary>
/// Reserves a named region for managed methods in a physical MMC3 PRG bank.
/// </summary>
public sealed class ManagedCodeBank
{
    /// <summary>
    /// Nonempty, case-sensitive name referenced by NESCodeBankAttribute.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Zero-based physical 8 KiB PRG bank index, excluding the final two fixed banks.
    /// </summary>
    public int Bank { get; set; }

    /// <summary>
    /// CPU window base address. Managed banking supports only R6 at $8000.
    /// </summary>
    public ushort CpuAddress { get; set; } = 0x8000;

    /// <summary>
    /// Byte offset within the physical bank. Defaults to zero.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Required positive reservation size in bytes, including unused space.
    /// </summary>
    public int Size { get; set; }
}

/// <summary>
/// Caller scheduling obligations for native callbacks with managed MMC3 banking.
/// </summary>
public enum Mmc3ManagedInterruptContract
{
    /// <summary>
    /// No user interrupt callbacks are permitted with managed banking.
    /// </summary>
    None,

    /// <summary>
    /// Native callbacks may select CHR registers but must not nest, change PRG
    /// mappings, or enter banked managed code. This is not proof of interrupt timing.
    /// </summary>
    NonNestingChrCallbacks,
}

/// <summary>
/// Explicit caller contract for one foreground native entry in PRG RAM.
/// This declaration neither initializes the code nor proves its runtime effects.
/// </summary>
public sealed class NativeRamCode
{
    /// <summary>
    /// Nonempty logical name identifying the declared region.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Exact callable entry address. The full region must fit in $6000-$7FFF.
    /// </summary>
    public ushort Address { get; set; }

    /// <summary>
    /// Required positive region size in bytes, with no overlap with other declarations.
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// Required explicit behavior contract. None grants no permission to call RAM code.
    /// </summary>
    public NativeRamCodeContract Contract { get; set; }
}

/// <summary>
/// Caller-guaranteed behavior of declared native PRG-RAM code, not compiler proof.
/// </summary>
public enum NativeRamCodeContract
{
    /// <summary>
    /// No behavior contract. Cannot authorize native RAM entry.
    /// </summary>
    None,

    /// <summary>
    /// Foreground-only entry at the exact declared address, by a normal call or
    /// tail JMP, returning with RTS and balanced hardware stack. Preserves the
    /// software stack, mapper registers, full selector, and compiler selector shadow.
    /// Never reachable from callbacks or banked code.
    /// </summary>
    ForegroundRtsPreservesMapperContext,
}
