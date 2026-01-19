namespace dotnes.ObjectModel;

/// <summary>
/// Manages labels and their resolved addresses
/// </summary>
public class LabelTable
{
    private readonly Dictionary<string, ushort> _labels = new();
    private readonly HashSet<string> _unresolvedReferences = new();

    /// <summary>
    /// Defines a label at the given address
    /// </summary>
    /// <param name="name">Label name</param>
    /// <param name="address">Address for the label</param>
    /// <exception cref="DuplicateLabelException">If the label is already defined</exception>
    public void Define(string name, ushort address)
    {
        if (_labels.ContainsKey(name))
            throw new DuplicateLabelException(name);
        _labels[name] = address;
        _unresolvedReferences.Remove(name);
    }

    /// <summary>
    /// Defines or updates a label at the given address (no exception if exists)
    /// </summary>
    public void DefineOrUpdate(string name, ushort address)
    {
        _labels[name] = address;
        _unresolvedReferences.Remove(name);
    }

    /// <summary>
    /// Attempts to resolve a label to its address
    /// </summary>
    /// <param name="name">Label name</param>
    /// <param name="address">Resolved address if found</param>
    /// <returns>True if label was resolved</returns>
    public bool TryResolve(string name, out ushort address)
    {
        if (_labels.TryGetValue(name, out address))
            return true;
        _unresolvedReferences.Add(name);
        return false;
    }

    /// <summary>
    /// Gets the address of a label, throwing if not found
    /// </summary>
    public ushort Resolve(string name)
    {
        if (!TryResolve(name, out ushort address))
            throw new UnresolvedLabelException(name);
        return address;
    }

    /// <summary>
    /// Checks if a label is defined
    /// </summary>
    public bool IsDefined(string name) => _labels.ContainsKey(name);

    /// <summary>
    /// Gets all defined labels
    /// </summary>
    public IReadOnlyDictionary<string, ushort> Labels => _labels;

    /// <summary>
    /// Gets labels that were referenced but not resolved
    /// </summary>
    public IReadOnlyCollection<string> UnresolvedReferences => _unresolvedReferences;

    /// <summary>
    /// Clears all labels and unresolved references
    /// </summary>
    public void Clear()
    {
        _labels.Clear();
        _unresolvedReferences.Clear();
    }

    /// <summary>
    /// Number of defined labels
    /// </summary>
    public int Count => _labels.Count;
}
