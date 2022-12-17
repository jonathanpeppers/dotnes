using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace dotnes;

class Transpiler : IDisposable
{
    readonly PEReader pe;
    readonly MetadataReader reader;

    public Transpiler(Stream stream)
    {
        pe = new PEReader(stream);
        reader = pe.GetMetadataReader();
    }

    public void Write(Stream stream)
    {
        using var chr_rom = Get_CHR_ROM();
        int CHR_ROM_SIZE = 0;
        if (chr_rom != null)
        {
            if (chr_rom.Length % NESWriter.CHR_ROM_BLOCK_SIZE != 0)
                throw new InvalidOperationException($"CHR_ROM must be in blocks of {NESWriter.CHR_ROM_BLOCK_SIZE}");

            CHR_ROM_SIZE = (int)(chr_rom.Length / NESWriter.CHR_ROM_BLOCK_SIZE);
        }

        // Generate static void main in a first pass, so we know the size of the program
        ushort sizeOfMain;
        using (var mainWriter = new IL2NESWriter(new MemoryStream()))
        {
            foreach (var instruction in ReadStaticVoidMain())
            {
                if (instruction.Integer != null)
                {
                    mainWriter.Write(instruction.OpCode, instruction.Integer.Value, 0);
                }
                else if (instruction.String != null)
                {
                    mainWriter.Write(instruction.OpCode, instruction.String, 0);
                }
                else
                {
                    mainWriter.Write(instruction.OpCode, 0);
                }
            }
            mainWriter.Flush();
            sizeOfMain = checked((ushort)mainWriter.BaseStream.Length);
        }

        using var writer = new IL2NESWriter(stream);
        writer.WriteHeader(PRG_ROM_SIZE: 2, CHR_ROM_SIZE: 1);
        writer.WriteBuiltIns(sizeOfMain);

        // Write static void main *again*, second pass
        // With a known value for sizeOfMain
        foreach (var instruction in ReadStaticVoidMain())
        {
            if (instruction.Integer != null)
            {
                writer.Write(instruction.OpCode, instruction.Integer.Value, sizeOfMain);
            }
            else if (instruction.String != null)
            {
                writer.Write(instruction.OpCode, instruction.String, sizeOfMain);
            }
            else
            {
                writer.Write(instruction.OpCode, sizeOfMain);
            }
        }

        writer.WriteFinalBuiltIns();

        // Write C# string table
        int stringHeapSize = reader.GetHeapSize(HeapIndex.UserString);
        if (stringHeapSize > 0)
        {
            var handle = MetadataTokens.UserStringHandle(0);
            do
            {
                string value = reader.GetUserString(handle);
                if (!string.IsNullOrEmpty(value))
                {
                    writer.WriteString(value);
                }
                handle = reader.GetNextHandle(handle);
            }
            while (!handle.IsNil);
        }

        writer.WriteDestructorTable();

        // Pad 0s
        int PRG_ROM_SIZE = (int)writer.Length - 16;
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - (PRG_ROM_SIZE % NESWriter.PRG_ROM_BLOCK_SIZE));
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - 6);

        //TODO: no idea what these are???
        writer.Write(new byte[] { 0xBC, 0x80, 0x00, 0x80, 0x02, 0x82 });

        chr_rom?.CopyTo(writer.BaseStream);
        writer.Flush();
    }

    Stream? Get_CHR_ROM()
    {
        foreach (var h in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(h);
            var name = reader.GetString(resource.Name);
            if (name == "CHR_ROM.nes")
            {
                return pe.GetEmbeddedResourceStream(resource);
            }
        }
        return null;
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
