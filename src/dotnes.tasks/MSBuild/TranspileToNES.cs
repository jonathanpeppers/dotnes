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

    public ILogger? Logger { get; set; }

    public override bool Execute()
    {
        Logger ??= DiagnosticLogging ? new MSBuildLogger(Log) : null;
        var assemblies = AssemblyFiles.Select(a => new AssemblyReader(a)).ToList();
        using var input = File.OpenRead(TargetPath);
        using var output = File.Create(OutputPath);
        using var transpiler = new Transpiler(input, assemblies, Logger, NESVerticalMirroring);
        transpiler.Write(output);

        return !Log.HasLoggedErrors;
    }
}
