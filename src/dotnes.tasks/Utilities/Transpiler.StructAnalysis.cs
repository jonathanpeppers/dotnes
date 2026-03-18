using System.Reflection;
using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Struct analysis — detects struct layouts, decodes field sizes, builds static field
/// size maps, and pre-allocates shared static field addresses.
/// </summary>
partial class Transpiler
{
    /// <summary>
    /// Scans the assembly's TypeDefinitions for user-defined value types (structs)
    /// and returns a dictionary of struct name → field list (name, size in bytes).
    /// </summary>
    Dictionary<string, List<(string Name, int Size)>> DetectStructLayouts()
    {
        var result = new Dictionary<string, List<(string Name, int Size)>>(StringComparer.Ordinal);

        foreach (var t in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(t);
            var ns = _reader.GetString(type.Namespace);

            // Skip types in non-empty namespaces (system types, NES namespace, etc.)
            if (!string.IsNullOrEmpty(ns))
                continue;

            // Check if this is a value type (struct) by looking at the base type
            var baseType = type.BaseType;
            if (baseType.IsNil)
                continue;

            string? baseTypeName = null;
            if (baseType.Kind == HandleKind.TypeReference)
                baseTypeName = _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseType).Name);
            else if (baseType.Kind == HandleKind.TypeDefinition)
                baseTypeName = _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseType).Name);

            // Value types derive from System.ValueType
            if (baseTypeName != "ValueType")
                continue;

            var typeName = _reader.GetString(type.Name);
            // Detect compiler-generated closure structs (display classes)
            if (typeName.Contains("DisplayClass"))
            {
                var capturedFields = string.Join(", ",
                    type.GetFields().Select(f => _reader.GetString(_reader.GetFieldDefinition(f).Name)));
                throw new TranspileException(
                    $"Closures are not supported. The compiler generated a closure struct '{typeName}' " +
                    $"capturing variable(s): {capturedFields}. " +
                    "This happens when local functions reference outer variables (like byte[] arrays). " +
                    "Workaround: pass captured variables as parameters to the function instead, " +
                    "or inline the local function code into the main body.");
            }
            // Skip other compiler-generated types
            if (typeName.StartsWith("<") || typeName.Contains("__"))
                continue;

            var fields = new List<(string Name, int Size)>();
            foreach (var f in type.GetFields())
            {
                var field = _reader.GetFieldDefinition(f);
                var fieldName = _reader.GetString(field.Name);
                int fieldSize = DecodeFieldSize(field);
                fields.Add((fieldName, fieldSize));
            }

            if (fields.Count > 0)
                result[typeName] = fields;
        }

        return result;
    }

    /// <summary>
    /// Decodes the size of a field from its signature blob.
    /// </summary>
    int DecodeFieldSize(FieldDefinition field)
    {
        var sig = _reader.GetBlobReader(field.Signature);
        sig.ReadByte(); // field calling convention (0x06)
        byte elementType = sig.ReadByte();
        return elementType switch
        {
            0x02 => 1, // ELEMENT_TYPE_BOOLEAN
            0x04 => 1, // ELEMENT_TYPE_I1 (sbyte)
            0x05 => 1, // ELEMENT_TYPE_U1 (byte)
            0x06 => 2, // ELEMENT_TYPE_I2 (short)
            0x07 => 2, // ELEMENT_TYPE_U2 (ushort)
            0x08 => 4, // ELEMENT_TYPE_I4 (int)
            0x09 => 4, // ELEMENT_TYPE_U4 (uint)
            _ => 1     // Default to 1 byte for unknown types
        };
    }

    /// <summary>
    /// Builds a map of static field names to their byte sizes from assembly metadata.
    /// Uses field signatures to determine type sizes. NES is 8-bit, so int/uint are
    /// capped at 2 bytes (16-bit is sufficient for NES address math).
    /// </summary>
    Dictionary<string, int> BuildStaticFieldSizes()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(t);
            foreach (var f in type.GetFields())
            {
                var field = _reader.GetFieldDefinition(f);
                if ((field.Attributes & FieldAttributes.Static) == 0)
                    continue;
                if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
                    continue;
                var fieldName = _reader.GetString(field.Name);
                int size = DecodeFieldSize(field);
                // NES is 8-bit; cap at 2 bytes (16-bit sufficient for address math)
                result[fieldName] = Math.Min(size, 2);
            }
        }
        return result;
    }

    /// <summary>
    /// Find the struct size that contains a given field name.
    /// Returns the total struct size, or 0 if no matching struct is found.
    /// </summary>
    static int FindStructSizeByField(
        string fieldName,
        Dictionary<string, List<(string Name, int Size)>> structLayouts)
    {
        foreach (var kvp in structLayouts)
        {
            foreach (var f in kvp.Value)
            {
                if (f.Name == fieldName)
                {
                    int size = 0;
                    foreach (var field in kvp.Value) size += field.Size;
                    return size;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Pre-scans main and all user method IL for user-defined static field references
    /// (Stsfld/Ldsfld) and allocates a shared address for each unique field.
    /// This ensures all methods resolve the same field name to the same RAM address.
    /// Multi-byte fields (int, ushort, short) get 2 bytes of zero page.
    /// </summary>
    (Dictionary<string, ushort> addresses, HashSet<string> wordFields, int totalBytes) PreAllocateStaticFields(ILInstruction[] mainInstructions)
    {
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);

        // Scan main IL
        foreach (var instr in mainInstructions)
        {
            if (instr.OpCode is ILOpCode.Stsfld or ILOpCode.Ldsfld && instr.String is not null)
                fieldNames.Add(instr.String);
        }

        // Scan user method IL
        foreach (var kvp in UserMethods)
        {
            foreach (var instr in kvp.Value)
            {
                if (instr.OpCode is ILOpCode.Stsfld or ILOpCode.Ldsfld && instr.String is not null)
                    fieldNames.Add(instr.String);
            }
        }

        // Remove NESLib fields that are handled specially
        fieldNames.Remove("oam_off");

        // Build field size map from metadata
        var fieldSizes = BuildStaticFieldSizes();

        // Allocate addresses sequentially starting at LocalStackBase,
        // using the correct byte size for each field.
        var addresses = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var wordFields = new HashSet<string>(StringComparer.Ordinal);
        ushort offset = 0;
        foreach (var name in fieldNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            addresses[name] = (ushort)(NESConstants.LocalStackBase + offset);
            int size = fieldSizes.TryGetValue(name, out var s) ? s : 1;
            if (size > 1)
                wordFields.Add(name);
            offset += (ushort)size;
            _logger.WriteLine($"Static field '{name}' allocated at ${addresses[name]:X4} ({size} byte{(size > 1 ? "s" : "")})");
        }
        return (addresses, wordFields, offset);
    }
}
