using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace dotnes;

class ILReader : IDisposable
{
    readonly PEReader pe;
    readonly MetadataReader reader;

    public ILReader(string path)
    {
        pe = new PEReader(File.OpenRead(path));
        reader = pe.GetMetadataReader();
    }

    /// <summary>
    /// Based on: https://github.com/icsharpcode/ILSpy/blob/8c508d9bbbc6a21cc244e930122ff5bca19cd11c/ILSpy/Analyzers/Builtin/MethodUsesAnalyzer.cs#L51
    /// </summary>
    public IEnumerable<ILInstruction> ReadStaticVoidMain()
    {
        foreach (var h in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(h);
            if ((method.Attributes & MethodAttributes.Static) == 0)
                continue;

            var methodName = reader.GetString(method.Name);
            if (methodName == "Main" || methodName == "<Main>$")
            {
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                var blob = body.GetILReader();

                while (blob.RemainingBytes > 0)
                {
                    ILOpCode opCode = DecodeOpCode(ref blob);

                    OperandType operandType = GetOperandType(opCode);
                    string? stringValue = null;
                    int? intValue = null;

                    switch (operandType)
                    {
                        case OperandType.Field:
                        case OperandType.Method:
                        case OperandType.Sig:
                        case OperandType.Tok:
                            var member = MetadataTokens.EntityHandle(blob.ReadInt32());
                            if (member.IsNil)
                                continue;

                            switch (member.Kind)
                            {
                                case HandleKind.TypeDefinition:
                                    stringValue = reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)member).Name);
                                    break;
                                case HandleKind.TypeReference:
                                    stringValue = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member).Name);
                                    break;
                                case HandleKind.MethodDefinition:
                                    stringValue = reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)member).Name);
                                    break;
                                case HandleKind.MemberReference:
                                    stringValue = reader.GetString(reader.GetMemberReference((MemberReferenceHandle)member).Name);
                                    break;
                                case HandleKind.FieldDefinition:
                                    stringValue = reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)member).Name);
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
                        case OperandType.Type:
                        case OperandType.ShortR:
                            intValue = blob.ReadInt32();
                            break;
                        case OperandType.String:
                            stringValue = reader.GetUserString(MetadataTokens.UserStringHandle(blob.ReadInt32()));
                            break;
                        // (n + 1) * 32-bit
                        case OperandType.Switch:
                            //uint n = blob.ReadUInt32();
                            //blob.Offset += (int)(n * 4);
                            goto default;
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

                    yield return new ILInstruction(opCode, intValue, stringValue);
                }
            }
        }
    }

    static ILOpCode DecodeOpCode(ref BlobReader blob)
    {
        byte opCodeByte = blob.ReadByte();
        return (ILOpCode)(opCodeByte == 0xFE ? 0xFE00 + blob.ReadByte() : opCodeByte);
    }

    static readonly byte[] operandTypes = { (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.ShortVariable, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.ShortI, (byte)OperandType.I, (byte)OperandType.I8, (byte)OperandType.ShortR, (byte)OperandType.R, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Method, (byte)OperandType.Sig, (byte)OperandType.None, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.BrTarget, (byte)OperandType.Switch, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.String, (byte)OperandType.Method, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, 255, 255, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Field, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.Type, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.Type, (byte)OperandType.None, 255, 255, (byte)OperandType.Type, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.Tok, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.BrTarget, (byte)OperandType.ShortBrTarget, (byte)OperandType.None, (byte)OperandType.None, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Method, (byte)OperandType.Method, 255, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.Variable, (byte)OperandType.None, 255, (byte)OperandType.None, (byte)OperandType.ShortI, (byte)OperandType.None, (byte)OperandType.None, (byte)OperandType.Type, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, 255, (byte)OperandType.None, 255, (byte)OperandType.Type, (byte)OperandType.None, (byte)OperandType.None, };

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

    public void Dispose() => pe.Dispose();
}
