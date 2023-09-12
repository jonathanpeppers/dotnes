﻿using System.Buffers;
using System.Collections.Immutable;
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

    public IList<AssemblyReader> AssemblyFiles { get; set; } = new List<AssemblyReader>();

    public void Write(Stream stream)
    {
        if (AssemblyFiles.Count == 0)
            throw new InvalidOperationException("At least one 'chr_generic.s' file must be present!");

        var assemblyReader = AssemblyFiles.FirstOrDefault(a => Path.GetFileName(a.Path) == "chr_generic.s") ?? AssemblyFiles[0];
        var chr_rom = assemblyReader.GetSegments().FirstOrDefault(s => s.Name == "CHARS");
        if (chr_rom == null)
        {
            throw new InvalidOperationException($"At least one 'CHARS' segment must be present in: {assemblyReader.Path}");
        }
        int CHR_ROM_SIZE = (int)(chr_rom.Bytes.Length / NESWriter.CHR_ROM_BLOCK_SIZE);

        // Generate static void main in a first pass, so we know the size of the program
        ushort sizeOfMain;
        using (var mainWriter = new IL2NESWriter(new MemoryStream()))
        {
            foreach (var instruction in ReadStaticVoidMain())
            {
                if (instruction.Integer != null)
                {
                    mainWriter.Write(instruction.OpCode, instruction.Integer.Value, sizeOfMain: 0);
                }
                else if (instruction.String != null)
                {
                    mainWriter.Write(instruction.OpCode, instruction.String, sizeOfMain: 0);
                }
                else if (instruction.Bytes != null)
                {
                    mainWriter.Write(instruction.OpCode, instruction.Bytes.Value, sizeOfMain: 0);
                }
                else
                {
                    mainWriter.Write(instruction.OpCode, sizeOfMain: 0);
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
            else if (instruction.Bytes != null)
            {
                writer.Write(instruction.OpCode, instruction.Bytes.Value, sizeOfMain);
            }
            else
            {
                writer.Write(instruction.OpCode, sizeOfMain);
            }
        }

        // NOTE: not sure if string or byte[] is first
        using (var memoryStream = new MemoryStream())
        {
            using (var tableWriter = new IL2NESWriter(memoryStream, leaveOpen: true))
            {
                // Write byte[] table
                tableWriter.WriteByteArrays(writer);

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
                            tableWriter.WriteString(value);
                        }
                        handle = reader.GetNextHandle(handle);
                    }
                    while (!handle.IsNil);
                }
            }

            const ushort PRG_LAST = 0x85AE;
            writer.WriteFinalBuiltIns((ushort)(PRG_LAST.GetAddressAfterMain(sizeOfMain) + memoryStream.Length));
            memoryStream.Position = 0;
            memoryStream.CopyTo(writer.BaseStream);
        }

        writer.WriteDestructorTable();

        // Pad 0s
        int PRG_ROM_SIZE = (int)writer.Length - 16;
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - (PRG_ROM_SIZE % NESWriter.PRG_ROM_BLOCK_SIZE));
        writer.WriteZeroes(NESWriter.PRG_ROM_BLOCK_SIZE - 6);

        //TODO: no idea what these are???
        writer.Write(new byte[] { 0xBC, 0x80, 0x00, 0x80, 0x02, 0x82 });

        writer.Write(chr_rom.Bytes);
        // Pad remaining zeros
        int padLength = chr_rom.Bytes.Length % NESWriter.CHR_ROM_BLOCK_SIZE;
        if (padLength != 0)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(padLength);
            try
            {
                writer.Write(buffer, 0, padLength);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        writer.Flush();
    }

    /// <summary>
    /// Based on: https://github.com/icsharpcode/ILSpy/blob/8c508d9bbbc6a21cc244e930122ff5bca19cd11c/ILSpy/Analyzers/Builtin/MethodUsesAnalyzer.cs#L51
    /// </summary>
    public IEnumerable<ILInstruction> ReadStaticVoidMain()
    {
        var arrayValues = GetArrayValues(reader);

        foreach (var h in reader.MethodDefinitions)
        {
            var mainMethod = reader.GetMethodDefinition(h);
            if ((mainMethod.Attributes & MethodAttributes.Static) == 0)
                continue;

            var mainMethodName = reader.GetString(mainMethod.Name);
            if (mainMethodName == "Main" || mainMethodName == "<Main>$")
            {
                var body = pe.GetMethodBody(mainMethod.RelativeVirtualAddress);
                var blob = body.GetILReader();

                while (blob.RemainingBytes > 0)
                {
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
                                    stringValue = reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)entity).Name);
                                    break;
                                case HandleKind.TypeReference:
                                    stringValue = reader.GetString(reader.GetTypeReference((TypeReferenceHandle)entity).Name);
                                    break;
                                case HandleKind.MethodDefinition:
                                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)entity);
                                    stringValue = reader.GetString(method.Name);
                                    break;
                                case HandleKind.MemberReference:
                                    var member = reader.GetMemberReference((MemberReferenceHandle)entity);
                                    stringValue = reader.GetString(member.Name);
                                    if (stringValue == "InitializeArray")
                                    {
                                        // HACK: skip for now
                                        continue;
                                    }
                                    break;
                                case HandleKind.FieldDefinition:
                                    var field = reader.GetFieldDefinition((FieldDefinitionHandle)entity);
                                    var fieldName = reader.GetString(field.Name);
                                    if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
                                    {
                                        if (arrayValues.TryGetValue (fieldName, out var value))
                                        {
                                            byteValue = value.Value;
                                            break;
                                        }
                                    }
                                    throw new NotImplementedException($"Reading fields like {fieldName} is not implemented!");
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

                    yield return new ILInstruction(opCode, intValue, stringValue, byteValue);
                }
            }
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
                        var sectionData = pe.GetSectionData(rva);
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

    public void Dispose()
    {
        foreach (var assembly in AssemblyFiles)
        {
            assembly.Dispose();
        }
        pe.Dispose();
    }
}
