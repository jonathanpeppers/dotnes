namespace dotnes;

public class TranspileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public string[] AssemblyFiles { get; set; } = Array.Empty<string>();

    public override bool Execute()
    {
        using var input = File.OpenRead(TargetPath);
        using var output = File.Create(OutputPath);
        using var transpiler = new Transpiler(input)
        {
            AssemblyFiles = AssemblyFiles.Select(a => new AssemblyReader(a)).ToList(),
        };
        transpiler.Write(output);

        return !Log.HasLoggedErrors;
    }
}
