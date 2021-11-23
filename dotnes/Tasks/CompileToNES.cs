namespace dotnes;

public class CompileToNES : Task
{
    [Required]
    public string TargetPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public override bool Execute()
    {
        using var fs = File.Create(OutputPath);
        using var writer = new NESWriter(fs);
        writer.Write();

        return !Log.HasLoggedErrors;
    }
}
