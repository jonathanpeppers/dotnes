using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// IL parsing — reads method bodies, decodes IL instructions, and extracts array initializer data.
/// </summary>
partial class Transpiler
{
    /// <summary>
    /// Based on: https://github.com/icsharpcode/ILSpy/blob/8c508d9bbbc6a21cc244e930122ff5bca19cd11c/ILSpy/Analyzers/Builtin/MethodUsesAnalyzer.cs#L51
    /// </summary>
    public IEnumerable<ILInstruction> ReadStaticVoidMain()
    {
        GetUsedMethods(_reader);
        var arrayValues = GetArrayValues(_reader);

        foreach (var h in _reader.MethodDefinitions)
        {
            var methodDef = _reader.GetMethodDefinition(h);
            if ((methodDef.Attributes & MethodAttributes.Static) == 0)
                continue;

            var methodName = _reader.GetString(methodDef.Name);

            if (methodName == "Main" || methodName == "<Main>$")
            {
                // Yield main method instructions
                foreach (var instruction in ReadMethodBody(methodDef, arrayValues))
                    yield return instruction;
            }
            else if (!methodName.StartsWith("."))
            {
                // Check for extern methods (static extern — no IL body)
                if ((methodDef.Attributes & MethodAttributes.PinvokeImpl) != 0
                    || methodDef.RelativeVirtualAddress == 0)
                {
                    // Clean up compiler-generated name (same as user methods)
                    string externName = methodName;
                    if (externName.StartsWith("<"))
                    {
                        int gIdx = externName.IndexOf(">g__");
                        if (gIdx >= 0)
                        {
                            int nameStart = gIdx + 4;
                            int pipeIdx = externName.IndexOf('|', nameStart);
                            externName = pipeIdx > nameStart ? externName.Substring(nameStart, pipeIdx - nameStart) : externName.Substring(nameStart);
                        }
                    }
                    {
                        var esig = _reader.GetBlobReader(methodDef.Signature);
                        esig.ReadByte(); // calling convention
                        int eParamCount = esig.ReadCompressedInteger();
                        byte eRetTypeByte = esig.ReadByte(); // ELEMENT_TYPE_VOID = 0x01
                        bool eHasReturnValue = eRetTypeByte != 0x01;
                        ExternMethods[externName] = (eParamCount, eHasReturnValue);
                    }
                    continue;
                }

                // Extract clean name for user-defined methods
                // Normal methods: use name as-is
                // Local functions: compiler generates names like <<Main>$>g__fade_in|0_0
                string cleanName;
                if (methodName.StartsWith("<"))
                {
                    // Try to extract local function name: pattern is >g__NAME|
                    int gIdx = methodName.IndexOf(">g__");
                    if (gIdx < 0) continue; // skip compiler-generated methods that aren't local functions
                    int nameStart = gIdx + 4;
                    int pipeIdx = methodName.IndexOf('|', nameStart);
                    cleanName = pipeIdx > nameStart ? methodName.Substring(nameStart, pipeIdx - nameStart) : methodName.Substring(nameStart);
                }
                else
                {
                    cleanName = methodName;
                }

                // User-defined method: read and store its IL
                var instructions = ReadMethodBody(methodDef, arrayValues).ToArray();
                UserMethods[cleanName] = instructions;

                // Extract metadata from method signature blob
                var sig = _reader.GetBlobReader(methodDef.Signature);
                sig.ReadByte(); // calling convention
                int paramCount = sig.ReadCompressedInteger();
                byte retTypeByte = sig.ReadByte(); // ELEMENT_TYPE_VOID = 0x01
                bool hasReturnValue = retTypeByte != 0x01;
                // Parse parameter types to detect byte[] (ELEMENT_TYPE_SZARRAY = 0x1D)
                bool[] isArrayParam = new bool[paramCount];
                for (int i = 0; i < paramCount; i++)
                {
                    byte paramTypeByte = sig.ReadByte();
                    if (paramTypeByte == 0x1D) // ELEMENT_TYPE_SZARRAY
                    {
                        sig.ReadByte(); // skip element type (e.g., 0x05 for byte[])
                        isArrayParam[i] = true;
                    }
                }
                UserMethodMetadata[cleanName] = (paramCount, hasReturnValue, isArrayParam);
            }
        }
    }

    IEnumerable<ILInstruction> ReadMethodBody(MethodDefinition methodDef, Dictionary<string, ArrayValue> arrayValues)
    {
        var body = _pe.GetMethodBody(methodDef.RelativeVirtualAddress);
        var blob = body.GetILReader();

        while (blob.RemainingBytes > 0)
        {
            int offset = blob.Offset;
            ILOpCode opCode = DecodeOpCode(ref blob);

            OperandType operandType = GetOperandType(opCode);
            string? stringValue = null;
            int? intValue = null;
            ImmutableArray<byte>? byteValue = null;

            switch (operandType)
            {
                case OperandType.Field:
                case OperandType.Method:
                case OperandType.Sig:
                case OperandType.Tok:
                    var entity = MetadataTokens.EntityHandle(blob.ReadInt32());
                    if (entity.IsNil)
                        continue;

                    switch (entity.Kind)
                    {
                        case HandleKind.TypeDefinition:
                            stringValue = _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)entity).Name);
                            break;
                        case HandleKind.TypeReference:
                            stringValue = _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)entity).Name);
                            break;
                        case HandleKind.MethodDefinition:
                            var method = _reader.GetMethodDefinition((MethodDefinitionHandle)entity);
                            stringValue = _reader.GetString(method.Name);
                            // Clean up compiler-generated local function names
                            // Pattern: <<Main>$>g__fade_in|0_0 → fade_in
                            if (stringValue.StartsWith("<"))
                            {
                                int gIdx = stringValue.IndexOf(">g__");
                                if (gIdx >= 0)
                                {
                                    int nameStart = gIdx + 4;
                                    int pipeIdx = stringValue.IndexOf('|', nameStart);
                                    stringValue = pipeIdx > nameStart ? stringValue.Substring(nameStart, pipeIdx - nameStart) : stringValue.Substring(nameStart);
                                }
                            }
                            break;
                        case HandleKind.MemberReference:
                            stringValue = GetQualifiedMemberName(_reader.GetMemberReference((MemberReferenceHandle)entity));
                            if (stringValue is "InitializeArray" or "RuntimeHelpers.InitializeArray")
                            {
                                // HACK: skip for now
                                continue;
                            }
                            break;
                        case HandleKind.MethodSpecification:
                            // Generic method instantiation (e.g., Array.Fill<byte>)
                            var methodSpec = _reader.GetMethodSpecification((MethodSpecificationHandle)entity);
                            if (methodSpec.Method.Kind == HandleKind.MemberReference)
                                stringValue = GetQualifiedMemberName(_reader.GetMemberReference((MemberReferenceHandle)methodSpec.Method));
                            break;
                        case HandleKind.FieldDefinition:
                            var field = _reader.GetFieldDefinition((FieldDefinitionHandle)entity);
                            var fieldName = _reader.GetString(field.Name);
                            if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
                            {
                                if (arrayValues.TryGetValue (fieldName, out var value))
                                {
                                    byteValue = value.Value;
                                    break;
                                }
                            }
                            // Non-RVA fields (e.g., oam_off) are passed as string operands
                            stringValue = fieldName;
                            break;
                    }
                    break;
                // 64-bit
                case OperandType.I8:
                case OperandType.R:
                    goto default;
                // 32-bit
                case OperandType.BrTarget:
                case OperandType.I:
                case OperandType.ShortR:
                    intValue = blob.ReadInt32();
                    break;
                case OperandType.Type:
                    {
                        var token = blob.ReadInt32();
                        intValue = token;
                        // Resolve type name for Newarr/Ldelema (e.g. "Byte", "UInt16", "Actor")
                        if (opCode == ILOpCode.Newarr || opCode == ILOpCode.Ldelema)
                        {
                            var handle = MetadataTokens.EntityHandle(token);
                            if (handle.Kind == HandleKind.TypeReference)
                                stringValue = _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)handle).Name);
                            else if (handle.Kind == HandleKind.TypeDefinition)
                                stringValue = _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name);
                        }
                    }
                    break;
                case OperandType.String:
                    stringValue = _reader.GetUserString(MetadataTokens.UserStringHandle(blob.ReadInt32()));
                    break;
                // (n + 1) * 32-bit
                case OperandType.Switch:
                    {
                        uint n = blob.ReadUInt32();
                        intValue = (int)n;
                        // Pack all target offsets as raw bytes
                        var targets = new byte[n * 4];
                        for (int ti = 0; ti < (int)n; ti++)
                        {
                            int target = blob.ReadInt32();
                            var targetBytes = BitConverter.GetBytes(target);
                            Array.Copy(targetBytes, 0, targets, ti * 4, 4);
                        }
                        byteValue = targets.ToImmutableArray();
                    }
                    break;
                // 16-bit
                case OperandType.Variable:
                    intValue = blob.ReadInt16();
                    break;
                // 8-bit
                case OperandType.ShortVariable:
                case OperandType.ShortBrTarget:
                case OperandType.ShortI:
                    intValue = blob.ReadByte();
                    break;
                case OperandType.None:
                    break;
                default:
                    throw new NotSupportedException($"{opCode}, OperandType={operandType} is not supported.");
            }

            yield return new ILInstruction(opCode, offset, intValue, stringValue, byteValue);
        }
    }

    /// <summary>
    /// Gets the method name from a MemberReference, qualified with the declaring type
    /// for BCL methods (e.g., "Array.Fill") to avoid collisions with user functions.
    /// NESLib methods remain unqualified (e.g., "pal_col").
    /// </summary>
    string GetQualifiedMemberName(MemberReference member)
    {
        string name = _reader.GetString(member.Name);
        var parent = member.Parent;
        string? typeName = null;
        if (parent.Kind == HandleKind.TypeReference)
            typeName = _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)parent).Name);
        else if (parent.Kind == HandleKind.TypeDefinition)
            typeName = _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)parent).Name);
        if (typeName != null && typeName != "NESLib")
            return $"{typeName}.{name}";
        return name;
    }

    void GetUsedMethods(MetadataReader reader)
    {
        TypeReferenceHandle? neslib = null;

        // Find the TypeReference to NES.NESLib
        foreach (var t in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(t);
            string ns = reader.GetString(type.Namespace);
            if (ns != nameof(NES))
                continue;

            string typeName = reader.GetString(type.Name);
            if (typeName != nameof(NESLib))
                continue;

            neslib = t;
            break;
        }

        if (neslib is null)
            throw new InvalidOperationException("Did not find TypeReference to NES.NESLib!");

        // Find any methods that are used in NES.NESLib
        foreach (var m in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(m);
            if (member.Parent != neslib.Value)
                continue;
            UsedMethods.Add(reader.GetString(member.Name));
        }
    }

    Dictionary<string, ArrayValue> GetArrayValues(MetadataReader reader)
    {
        var dictionary = new Dictionary<string, ArrayValue>(StringComparer.Ordinal);

        foreach (var t in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(t);
            var ns = reader.GetString(type.Namespace);
            if (!string.IsNullOrEmpty(ns))
                continue;

            var typeName = reader.GetString(type.Name);
            if (typeName == "<PrivateImplementationDetails>")
            {
                foreach (var f in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(f);
                    var fieldName = reader.GetString(field.Name);
                    if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
                    {
                        int rva = field.GetRelativeVirtualAddress();
                        int size = field.DecodeSignature(new FieldSizeDecoder(), default);
                        var sectionData = _pe.GetSectionData(rva);
                        dictionary.Add(fieldName, new ArrayValue(fieldName, sectionData, size));
                    }
                }
                break;
            }
        }

        return dictionary;
    }

    static ILOpCode DecodeOpCode(ref BlobReader blob)
    {
        byte opCodeByte = blob.ReadByte();
        return (ILOpCode)(opCodeByte == 0xFE ? 0xFE00 + blob.ReadByte() : opCodeByte);
    }

    static readonly byte[] operandTypes = [(byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.ShortI, (byte)OperandType.I, (byte)OperandType.I8, (byte)OperandType.ShortR, (byte)OperandType.R, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Method, (byte)OperandType.Sig, (byte)OperandType.None, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.Switch, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.String, (byte)OperandType.Method, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, 255, 255, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.Type, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.Type, (byte)OperandType.None, 255, 255, (byte)OperandType.Type, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.Tok, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.BrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.None, (byte)OperandType.None, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Method, 255, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.None, 255, (byte)OperandType.None, (byte)OperandType.ShortI, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, 255, (byte)OperandType.None, 255, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None,];

    static OperandType GetOperandType(ILOpCode opCode)
    {
        ushort index = (ushort)((((int)opCode & 0x200) >> 1) | ((int)opCode & 0xff));
        if (index >= operandTypes.Length)
            return (OperandType)255;
        return (OperandType)operandTypes[index];
    }

    enum OperandType
    {
        BrTarget,
        Field,
        I,
        I8,
        Method,
        None,
        R = 7,
        Sig = 9,
        String,
        Switch,
        Tok,
        Type,
        Variable,
        ShortBrTarget,
        ShortI,
        ShortR,
        ShortVariable
    }
}
