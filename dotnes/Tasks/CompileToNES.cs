namespace dotnes;

public class CompileToNES : Task
{
    public override bool Execute()
    {
        return !Log.HasLoggedErrors;
    }
}
