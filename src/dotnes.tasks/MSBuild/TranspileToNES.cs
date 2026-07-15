namespace dotnes;

public class TranspileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public string[] AssemblyFiles { get; set; } = Array.Empty<string>();

    public bool DiagnosticLogging { get; set; }

    /// <summary>
    /// Nametable mirroring mode: "Horizontal" (default) or "Vertical".
    /// </summary>
    public string NESMirroring { get; set; } = "Horizontal";

    /// <summary>
    /// iNES mapper number (0 = NROM, 4 = MMC3, etc.). Default is 0.
    /// </summary>
    public int NESMapper { get; set; }

    /// <summary>
    /// Number of 16KB PRG ROM banks. Default is 2.
    /// </summary>
    public int NESPrgBanks { get; set; } = 2;

    /// <summary>
    /// Number of 8KB CHR ROM banks. Default is 1.
    /// </summary>
    public int NESChrBanks { get; set; } = 1;

    /// <summary>
    /// Indicates battery-backed SRAM at $6000-$7FFF. Default is false.
    /// </summary>
    public bool NESBattery { get; set; }

    /// <summary>
    /// Enables deterministic physical 8 KiB PRG and 1 KiB CHR placement for mapper 4.
    /// </summary>
    public bool NESMmc3BankedLayout { get; set; }

    /// <summary>
    /// PRG assets with Bank, CpuAddress, and optional Offset metadata.
    /// </summary>
    public ITaskItem[] NESPrgBank { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// CHR assets with Bank and optional Offset metadata.
    /// </summary>
    public ITaskItem[] NESChrBank { get; set; } = Array.Empty<ITaskItem>();

    public ILogger? Logger { get; set; }

    public override bool Execute()
    {
        Logger ??= DiagnosticLogging ? new MSBuildLogger(Log) : null;
        var prgBankAssets = NESPrgBank.Select(ParsePrgBankAsset).ToArray();
        var chrBankAssets = NESChrBank.Select(ParseChrBankAsset).ToArray();
        var assemblies = AssemblyFiles.Select(a => new AssemblyReader(a)).ToList();
        using var input = File.OpenRead(TargetPath);
        using var output = File.Create(OutputPath);
        using var transpiler = new Transpiler(
            input,
            assemblies,
            Logger,
            NESMirroring,
            NESMapper,
            NESPrgBanks,
            NESChrBanks,
            NESBattery,
            NESMmc3BankedLayout,
            prgBankAssets,
            chrBankAssets);
        transpiler.Write(output);

        return !Log.HasLoggedErrors;
    }

    static BankedRomAsset ParsePrgBankAsset(ITaskItem item)
    {
        int bank = ParseMetadata(item, "Bank", required: true);
        int offset = ParseMetadata(item, "Offset", required: false);
        int cpuAddress = ParseMetadata(item, "CpuAddress", required: true);
        if (cpuAddress < 0 || cpuAddress > ushort.MaxValue)
            throw new InvalidOperationException($"NESPrgBank '{item.ItemSpec}' CpuAddress must fit in 16 bits.");
        return new BankedRomAsset(GetPath(item), bank, offset, (ushort)cpuAddress);
    }

    static BankedRomAsset ParseChrBankAsset(ITaskItem item)
    {
        int bank = ParseMetadata(item, "Bank", required: true);
        int offset = ParseMetadata(item, "Offset", required: false);
        return new BankedRomAsset(GetPath(item), bank, offset);
    }

    static int ParseMetadata(ITaskItem item, string name, bool required)
    {
        string value = item.GetMetadata(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                throw new InvalidOperationException($"Bank asset '{item.ItemSpec}' requires {name} metadata.");
            return 0;
        }

        string normalized = value.Trim();
        System.Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(2);
            style = System.Globalization.NumberStyles.AllowHexSpecifier;
        }
        else if (normalized[0] == '$')
        {
            normalized = normalized.Substring(1);
            style = System.Globalization.NumberStyles.AllowHexSpecifier;
        }

        if (!int.TryParse(normalized, style, System.Globalization.CultureInfo.InvariantCulture, out int result))
            throw new InvalidOperationException($"Bank asset '{item.ItemSpec}' has invalid {name} metadata '{value}'.");
        return result;
    }

    static string GetPath(ITaskItem item)
    {
        string fullPath = item.GetMetadata("FullPath");
        return string.IsNullOrEmpty(fullPath) ? item.ItemSpec : fullPath;
    }
}
