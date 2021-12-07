namespace dotnes;

public class TranspileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public override bool Execute()
    {
        using var fs = File.Create(OutputPath);
        using var transpiler = new Transpiler(TargetPath);
        transpiler.Write(fs);

        return !Log.HasLoggedErrors;
    }
}
