namespace dotnes;

public class TranspileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public string[] AssemblyFiles { get; set; } = Array.Empty<string>();

    public bool DiagnosticLogging { get; set; }

    public bool NESOptimizeByteHelpers { get; set; }

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

    /// <summary>
    /// Logical named managed regions with Bank, CpuAddress, Size, and optional Offset metadata.
    /// </summary>
    public ITaskItem[] NESManagedCodeBank { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Logical native PRG-RAM entries with required Address, Size, and Contract metadata.
    /// </summary>
    public ITaskItem[] NESNativeRamCode { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Explicit physical R6 home bank. Empty means unconfigured.
    /// </summary>
    public string NESMmc3ManagedHomeBank { get; set; } = "";

    /// <summary>
    /// Native callback contract: None or NonNestingChrCallbacks. Empty means None.
    /// </summary>
    public string NESMmc3ManagedInterruptContract { get; set; } = "";

    public ILogger? Logger { get; set; }

    public override bool Execute()
    {
        Logger ??= DiagnosticLogging ? new MSBuildLogger(Log) : null;
        var prgBankAssets = NESPrgBank.Select(ParsePrgBankAsset).ToArray();
        var chrBankAssets = NESChrBank.Select(ParseChrBankAsset).ToArray();
        var managedCodeBanks = NESManagedCodeBank.Select(ParseManagedCodeBank).ToArray();
        var nativeRamCode = NESNativeRamCode.Select(ParseNativeRamCode).ToArray();
        int? managedHomeBank = string.IsNullOrWhiteSpace(NESMmc3ManagedHomeBank)
            ? null
            : ParseInteger(NESMmc3ManagedHomeBank,
                $"NESMmc3ManagedHomeBank has invalid value '{NESMmc3ManagedHomeBank}'.");
        var managedInterruptContract = ParseManagedInterruptContract(NESMmc3ManagedInterruptContract);
        var assemblies = AssemblyFiles.Select(a => new AssemblyReader(a)).ToList();
        using var input = File.OpenRead(TargetPath);
        using var output = new MemoryStream();
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
            chrBankAssets,
            managedCodeBanks,
            managedHomeBank,
            managedInterruptContract,
            nativeRamCode: nativeRamCode)
        {
            OptimizeByteHelpers = NESOptimizeByteHelpers,
        };
        transpiler.Write(output);

        if (Log.HasLoggedErrors)
            return false;
        using var destination = File.Create(OutputPath);
        output.Position = 0;
        output.CopyTo(destination);
        return true;
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

    static ManagedCodeBank ParseManagedCodeBank(ITaskItem item)
    {
        int bank = ParseMetadata(item, "Bank", required: true, itemType: "NESManagedCodeBank");
        int cpuAddress = ParseMetadata(item, "CpuAddress", required: true, itemType: "NESManagedCodeBank");
        int offset = ParseMetadata(item, "Offset", required: false, itemType: "NESManagedCodeBank");
        int size = ParseMetadata(item, "Size", required: true, itemType: "NESManagedCodeBank");
        if (cpuAddress < 0 || cpuAddress > ushort.MaxValue)
            throw new InvalidOperationException($"NESManagedCodeBank '{item.ItemSpec}' CpuAddress must fit in 16 bits.");
        return new ManagedCodeBank
        {
            Name = item.ItemSpec,
            Bank = bank,
            CpuAddress = (ushort)cpuAddress,
            Offset = offset,
            Size = size,
        };
    }

    static NativeRamCode ParseNativeRamCode(ITaskItem item)
    {
        int address = ParseMetadata(item, "Address", required: true, itemType: "NESNativeRamCode");
        int size = ParseMetadata(item, "Size", required: true, itemType: "NESNativeRamCode");
        if (address < 0 || address > ushort.MaxValue)
            throw new InvalidOperationException($"NESNativeRamCode '{item.ItemSpec}' Address must fit in 16 bits.");
        string value = item.GetMetadata("Contract");
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"NESNativeRamCode '{item.ItemSpec}' requires Contract metadata.");
        var contract = value.Trim() switch
        {
            nameof(NativeRamCodeContract.None) => NativeRamCodeContract.None,
            nameof(NativeRamCodeContract.ForegroundRtsPreservesMapperContext) => NativeRamCodeContract.ForegroundRtsPreservesMapperContext,
            _ => throw new InvalidOperationException(
                $"NESNativeRamCode '{item.ItemSpec}' has invalid Contract metadata '{value}'. Expected ForegroundRtsPreservesMapperContext."),
        };
        return new NativeRamCode
        {
            Name = item.ItemSpec,
            Address = (ushort)address,
            Size = size,
            Contract = contract,
        };
    }

    static Mmc3ManagedInterruptContract ParseManagedInterruptContract(string value) => value.Trim() switch
    {
        "" => Mmc3ManagedInterruptContract.None,
        nameof(Mmc3ManagedInterruptContract.None) => Mmc3ManagedInterruptContract.None,
        nameof(Mmc3ManagedInterruptContract.NonNestingChrCallbacks) => Mmc3ManagedInterruptContract.NonNestingChrCallbacks,
        _ => throw new InvalidOperationException(
            $"NESMmc3ManagedInterruptContract has invalid value '{value}'. Expected None or NonNestingChrCallbacks."),
    };

    static int ParseMetadata(ITaskItem item, string name, bool required, string itemType = "Bank asset")
    {
        string value = item.GetMetadata(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                throw new InvalidOperationException($"{itemType} '{item.ItemSpec}' requires {name} metadata.");
            return 0;
        }

        return ParseInteger(value, $"{itemType} '{item.ItemSpec}' has invalid {name} metadata '{value}'.");
    }

    static int ParseInteger(string value, string errorMessage)
    {
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
            throw new InvalidOperationException(errorMessage);
        return result;
    }

    static string GetPath(ITaskItem item)
    {
        string fullPath = item.GetMetadata("FullPath");
        return string.IsNullOrEmpty(fullPath) ? item.ItemSpec : fullPath;
    }
}
