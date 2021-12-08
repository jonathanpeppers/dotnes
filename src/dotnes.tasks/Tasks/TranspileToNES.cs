namespace dotnes;

public class TranspileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public override bool Execute()
    {
        using var input = File.OpenRead(TargetPath);
        using var output = File.Create(OutputPath);
        using var transpiler = new Transpiler(input);
        transpiler.Write(output);

        return !Log.HasLoggedErrors;
    }
}
