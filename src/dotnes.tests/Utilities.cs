namespace dotnes.tests;

class Utilities
{
    public static Stream GetResource(string name)
    {
        var stream = typeof(Utilities).Assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException($"Cannot load {name}!");
        return stream;
    }
}
