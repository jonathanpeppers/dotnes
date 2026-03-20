using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
                // Catalog closure fields instead of throwing — closure support is handled
                // by rewriting the IL patterns in BuildProgram6502.
                foreach (var f in type.GetFields())
                {
                    var field = _reader.GetFieldDefinition(f);
                    var fieldName = _reader.GetString(field.Name);
                    int fieldSize = DecodeFieldSize(field);
                    _closureFieldTypes[fieldName] = fieldSize;
                }
                continue;
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
    /// Returns -1 for array types (ELEMENT_TYPE_SZARRAY).
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
            0x1D => -1, // ELEMENT_TYPE_SZARRAY (byte[])
            _ => 1     // Default to 1 byte for unknown types
        };
    }

    /// <summary>
    /// Builds a map of static field names to their byte sizes from assembly metadata.
    /// Uses field signatures to determine type sizes. NES is 8-bit, so int/uint are
    /// capped at 2 bytes (16-bit is sufficient for NES address math).
    /// Array fields (SZARRAY) return negative values encoding their byte count from .cctor.
    /// </summary>
    Dictionary<string, int> BuildStaticFieldSizes()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var arraySizes = ScanCctorArraySizes();

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
                if (size == -1) // SZARRAY
                {
                    size = arraySizes.TryGetValue(fieldName, out var arrSize) ? arrSize : 0;
                    result[fieldName] = -size; // negative = array with this many bytes
                }
                else
                {
                    // NES is 8-bit; cap at 2 bytes (16-bit sufficient for address math)
                    result[fieldName] = Math.Min(size, 2);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Scans static constructors (.cctor) for newarr sizes.
    /// Returns a map of field names to their array byte counts.
    /// </summary>
    Dictionary<string, int> ScanCctorArraySizes()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(t);
            foreach (var m in type.GetMethods())
            {
                var method = _reader.GetMethodDefinition(m);
                var methodName = _reader.GetString(method.Name);
                if (methodName != ".cctor")
                    continue;

                var body = _pe.GetMethodBody(method.RelativeVirtualAddress);
                var il = body.GetILReader();
                int lastLdc = 0;
                ILOpCode prevOp = ILOpCode.Nop;

                while (il.Offset < il.Length)
                {
                    var opCode = DecodeOpCode(ref il);
                    switch (opCode)
                    {
                        case ILOpCode.Ldc_i4_0: lastLdc = 0; break;
                        case ILOpCode.Ldc_i4_1: lastLdc = 1; break;
                        case ILOpCode.Ldc_i4_2: lastLdc = 2; break;
                        case ILOpCode.Ldc_i4_3: lastLdc = 3; break;
                        case ILOpCode.Ldc_i4_4: lastLdc = 4; break;
                        case ILOpCode.Ldc_i4_5: lastLdc = 5; break;
                        case ILOpCode.Ldc_i4_6: lastLdc = 6; break;
                        case ILOpCode.Ldc_i4_7: lastLdc = 7; break;
                        case ILOpCode.Ldc_i4_8: lastLdc = 8; break;
                        case ILOpCode.Ldc_i4_s: lastLdc = il.ReadSByte(); break;
                        case ILOpCode.Ldc_i4: lastLdc = il.ReadInt32(); break;
                        case ILOpCode.Newarr:
                            il.ReadInt32(); // skip type token
                            break;
                        case ILOpCode.Stsfld:
                        {
                            var token = il.ReadInt32();
                            if (prevOp is ILOpCode.Newarr or ILOpCode.Dup)
                            {
                                var handle = MetadataTokens.EntityHandle(token);
                                if (handle.Kind == HandleKind.FieldDefinition)
                                {
                                    var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                                    var name = _reader.GetString(field.Name);
                                    result[name] = lastLdc;
                                }
                            }
                            break;
                        }
                        case ILOpCode.Dup:
                            break;
                        default:
                        {
                            var operandType = GetOperandType(opCode);
                            switch (operandType)
                            {
                                case OperandType.ShortVariable:
                                case OperandType.ShortI:
                                case OperandType.ShortBrTarget:
                                    if (il.Offset < il.Length) il.ReadByte();
                                    break;
                                case OperandType.BrTarget:
                                case OperandType.I:
                                case OperandType.Field:
                                case OperandType.Method:
                                case OperandType.Tok:
                                case OperandType.Type:
                                case OperandType.String:
                                case OperandType.Sig:
                                case OperandType.ShortR:
                                    if (il.Offset + 4 <= il.Length) il.ReadInt32();
                                    break;
                                case OperandType.I8:
                                    if (il.Offset + 8 <= il.Length) il.ReadInt64();
                                    break;
                                case OperandType.R:
                                    if (il.Offset + 8 <= il.Length) il.ReadDouble();
                                    break;
                                case OperandType.Switch:
                                    if (il.Offset + 4 <= il.Length)
                                    {
                                        int count = il.ReadInt32();
                                        for (int s = 0; s < count && il.Offset + 4 <= il.Length; s++)
                                            il.ReadInt32();
                                    }
                                    break;
                                case OperandType.Variable:
                                    if (il.Offset + 2 <= il.Length) il.ReadInt16();
                                    break;
                            }
                            break;
                        }
                    }
                    prevOp = opCode;
                }
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
    (Dictionary<string, ushort> addresses, HashSet<string> wordFields, int totalBytes, Dictionary<string, (ushort Address, int ArraySize)> arrayFields) PreAllocateStaticFields(ILInstruction[] mainInstructions)
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
        var arrayFields = new Dictionary<string, (ushort Address, int ArraySize)>(StringComparer.Ordinal);
        ushort offset = 0;
        foreach (var name in fieldNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            addresses[name] = (ushort)(NESConstants.LocalStackBase + offset);
            int size = fieldSizes.TryGetValue(name, out var s) ? s : 1;
            if (size < 0)
            {
                // Array field: negative size encodes array byte count
                int arraySize = -size;
                arrayFields[name] = ((ushort)(NESConstants.LocalStackBase + offset), arraySize);
                offset += (ushort)arraySize;
                _logger.WriteLine($"Static field '{name}' allocated at ${addresses[name]:X4} (byte[{arraySize}])");
            }
            else
            {
                if (size > 1)
                    wordFields.Add(name);
                offset += (ushort)size;
                _logger.WriteLine($"Static field '{name}' allocated at ${addresses[name]:X4} ({size} byte{(size > 1 ? "s" : "")})");
            }
        }
        return (addresses, wordFields, offset, arrayFields);
    }

    /// <summary>
    /// Scans main IL to find the local variable index that holds the closure struct instance.
    /// Looks for the pattern: ldloca.s N ... stfld closureFieldName
    /// </summary>
    int DetectClosureStructLocal(ILInstruction[] instructions)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode is not (ILOpCode.Ldloca_s or ILOpCode.Ldloca))
                continue;
            int localIdx = instructions[i].Integer ?? 0;
            // Look ahead for stfld/ldfld referencing a closure field
            for (int j = i + 1; j < Math.Min(i + 12, instructions.Length); j++)
            {
                if (instructions[j].OpCode is ILOpCode.Stfld or ILOpCode.Ldfld
                    && instructions[j].String is string fieldName
                    && _closureFieldTypes.ContainsKey(fieldName))
                {
                    return localIdx;
                }
                // Another ldloca.s means the first one was consumed
                if (instructions[j].OpCode is ILOpCode.Ldloca_s or ILOpCode.Ldloca)
                    break;
            }
        }
        return -1;
    }

    /// <summary>
    /// Detects user methods that are closure-capturing functions by scanning their IL
    /// for ldarg.0 + ldfld patterns that reference closure fields. Adjusts their metadata
    /// to remove the implicit closure struct parameter.
    /// </summary>
    void DetectClosureMethods(ReflectionCache reflectionCache)
    {
        foreach (var kvp in UserMethods)
        {
            var methodName = kvp.Key;
            var il = kvp.Value;

            // Check if method accesses closure fields via ldarg.0 + ldfld
            bool isClosure = false;
            for (int i = 0; i < il.Length - 1; i++)
            {
                if (il[i].OpCode == ILOpCode.Ldarg_0
                    && il[i + 1].OpCode == ILOpCode.Ldfld
                    && il[i + 1].String is string fieldName
                    && _closureFieldTypes.ContainsKey(fieldName))
                {
                    isClosure = true;
                    break;
                }
            }

            if (!isClosure)
                continue;

            _closureMethodNames.Add(methodName);

            // Adjust metadata: remove the closure struct parameter (always first)
            if (UserMethodMetadata.TryGetValue(methodName, out var meta))
            {
                int newParamCount = meta.argCount - 1;
                bool[] newIsArrayParam = newParamCount > 0 && meta.isArrayParam.Length > 1
                    ? meta.isArrayParam.Skip(1).ToArray()
                    : Array.Empty<bool>();
                UserMethodMetadata[methodName] = (newParamCount, meta.hasReturnValue, newIsArrayParam);
                // Re-register with corrected param count
                reflectionCache.RegisterUserMethod(methodName, newParamCount, meta.hasReturnValue);
                _logger.WriteLine($"Closure method '{methodName}': adjusted params {meta.argCount} → {newParamCount}");
            }
        }
    }

    /// <summary>
    /// Pre-allocates zero-page addresses for scalar closure fields.
    /// Byte[] fields don't need addresses (they use ROM data labels).
    /// </summary>
    void PreAllocateClosureFields(ref int staticFieldBytes)
    {
        foreach (var kvp in _closureFieldTypes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (kvp.Value == -1) continue; // byte[] fields use ROM labels, not RAM
            int size = Math.Min(kvp.Value, 2); // NES is 8-bit; 16-bit is max for address math
            _closureFieldAddresses[kvp.Key] = (ushort)(NESConstants.LocalStackBase + staticFieldBytes);
            staticFieldBytes += size;
            _logger.WriteLine($"Closure field '{kvp.Key}' allocated at ${_closureFieldAddresses[kvp.Key]:X4} ({size} byte{(size > 1 ? "s" : "")})");
        }
    }
}
