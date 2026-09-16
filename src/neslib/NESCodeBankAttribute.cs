namespace NES;

/// <summary>
/// Places managed method bodies in a named MMC3 code-bank region.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NESCodeBankAttribute : Attribute
{
    /// <summary>
    /// Declares the nonempty, case-sensitive region name used by the compiler.
    /// </summary>
    public NESCodeBankAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the configured managed code-bank region name.
    /// </summary>
    public string Name { get; }
}
