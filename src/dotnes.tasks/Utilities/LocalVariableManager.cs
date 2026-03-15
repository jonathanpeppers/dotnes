using static dotnes.NESConstants;

namespace dotnes;

/// <summary>
/// Manages local variable zero-page allocation, struct field offset calculation,
/// and static field address tracking for the NES transpiler.
/// This class handles the pure state management of variable tracking, separate
/// from the 6502 code emission in IL2NESWriter.
/// </summary>
class LocalVariableManager
{
    readonly ushort _baseAddress;
    readonly Dictionary<int, string> _structLocalTypes = new();
    readonly Dictionary<string, ushort> _staticFieldAddresses = new(StringComparer.Ordinal);

    public LocalVariableManager(ushort baseAddress = LocalStackBase)
    {
        _baseAddress = baseAddress;
    }

    /// <summary>
    /// Dictionary of local variables, mapping local index to its Local record.
    /// </summary>
    public Dictionary<int, Local> Locals { get; } = new();

    /// <summary>
    /// Cumulative bytes allocated for locals on zero page.
    /// A program with 0 locals has a base address at <see cref="LocalStackBase"/>.
    /// </summary>
    public int LocalCount { get; set; }

    /// <summary>
    /// Local variable indices that are word-sized (ushort). Detected by pre-scanning
    /// for conv.u2 + stloc patterns in the IL. Word locals get 2 bytes of zero page.
    /// </summary>
    public HashSet<int> WordLocals { get; set; } = new();

    /// <summary>
    /// Struct type layouts: type name → ordered list of (fieldName, fieldSizeInBytes).
    /// Field offsets are cumulative from the first field.
    /// </summary>
    public Dictionary<string, List<(string Name, int Size)>> StructLayouts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Computes the next available address for a new local variable allocation.
    /// </summary>
    public ushort NextAddress => (ushort)(_baseAddress + LocalCount);

    /// <summary>
    /// Gets the zero-page address and allocates storage for a struct local.
    /// If already allocated, returns the existing address.
    /// </summary>
    public ushort GetOrAllocateStructLocal(int localIndex, string structType)
    {
        if (Locals.TryGetValue(localIndex, out var existing) && existing.Address is not null)
            return (ushort)existing.Address;

        // Allocate struct on zero page
        int structSize = GetStructSize(structType);

        ushort addr = NextAddress;
        LocalCount += structSize;
        Locals[localIndex] = new Local(0, addr);
        _structLocalTypes[localIndex] = structType;
        return addr;
    }

    /// <summary>
    /// Gets the byte offset of a field within a struct type.
    /// </summary>
    public int GetFieldOffset(string structType, string fieldName)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");

        int offset = 0;
        foreach (var f in fields)
        {
            if (f.Name == fieldName)
                return offset;
            offset += f.Size;
        }
        throw new InvalidOperationException($"Field '{fieldName}' not found in struct '{structType}'");
    }

    /// <summary>
    /// Gets the total size in bytes of a struct type.
    /// </summary>
    public int GetStructSize(string structType)
    {
        if (!StructLayouts.TryGetValue(structType, out var fields))
            throw new InvalidOperationException($"Unknown struct type '{structType}'");
        int size = 0;
        foreach (var f in fields)
            size += f.Size;
        return size > 0 ? size : 1;
    }

    /// <summary>
    /// Resolves the struct type for a local by checking known struct-local mappings
    /// or matching the field name against known struct layouts.
    /// </summary>
    public string ResolveStructType(int localIndex, string fieldName)
    {
        if (_structLocalTypes.TryGetValue(localIndex, out var knownType))
            return knownType;

        // Search all struct layouts for a matching field name
        foreach (var kvp in StructLayouts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            foreach (var f in kvp.Value)
            {
                if (f.Name == fieldName)
                {
                    _structLocalTypes[localIndex] = kvp.Key;
                    return kvp.Key;
                }
            }
        }
        throw new InvalidOperationException($"Cannot resolve struct type for local {localIndex} with field '{fieldName}'");
    }

    /// <summary>
    /// Pre-allocated static field addresses from the main writer,
    /// so user method writers use the same RAM addresses.
    /// </summary>
    public Dictionary<string, ushort> StaticFieldAddresses
    {
        get => _staticFieldAddresses;
        set
        {
            foreach (var kvp in value)
                _staticFieldAddresses[kvp.Key] = kvp.Value;

            // Advance LocalCount so subsequent allocations don't overlap
            // any pre-allocated static field addresses.
            foreach (var addr in value.Values)
            {
                int slotsPastBase = addr - _baseAddress + 1;
                if (slotsPastBase > LocalCount)
                    LocalCount = slotsPastBase;
            }
        }
    }

    /// <summary>
    /// Allocates an absolute address for a user-defined static field.
    /// Static fields share the same $0325+ address space as locals.
    /// </summary>
    public ushort GetOrAllocateStaticField(string fieldName)
    {
        if (_staticFieldAddresses.TryGetValue(fieldName, out var addr))
            return addr;
        addr = NextAddress;
        LocalCount += 1;
        _staticFieldAddresses[fieldName] = addr;
        return addr;
    }

    /// <summary>
    /// Records that a local variable index is associated with a specific struct type.
    /// </summary>
    public void SetStructType(int localIndex, string structType)
    {
        _structLocalTypes[localIndex] = structType;
    }

    /// <summary>
    /// Checks whether a local variable index is known to be a struct type.
    /// </summary>
    public bool IsStructLocal(int localIndex)
    {
        return _structLocalTypes.ContainsKey(localIndex);
    }

    /// <summary>
    /// Gets the struct type name for a local variable, if known.
    /// </summary>
    public string? GetStructType(int localIndex)
    {
        return _structLocalTypes.TryGetValue(localIndex, out var type) ? type : null;
    }

    /// <summary>
    /// The local variable record, representing a single local's allocation and metadata.
    /// </summary>
    public record Local(int Value, int? Address = null, string? LabelName = null, int ArraySize = 0, bool IsWord = false, string? StructArrayType = null);
}
