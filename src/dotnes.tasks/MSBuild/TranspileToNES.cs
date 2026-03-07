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
    /// NES vertical mirroring (horizontal scrolling). Default is false (horizontal mirroring).
    /// </summary>
    public bool NESVerticalMirroring { get; set; }

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

    public ILogger? Logger { get; set; }

    public override bool Execute()
    {
        Logger ??= DiagnosticLogging ? new MSBuildLogger(Log) : null;
        var assemblies = AssemblyFiles.Select(a => new AssemblyReader(a)).ToList();
        using var input = File.OpenRead(TargetPath);
        using var output = File.Create(OutputPath);
        using var transpiler = new Transpiler(input, assemblies, Logger, NESVerticalMirroring, NESMapper, NESPrgBanks, NESChrBanks);
        transpiler.Write(output);

        return !Log.HasLoggedErrors;
    }
}
