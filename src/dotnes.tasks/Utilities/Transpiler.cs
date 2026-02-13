using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using dotnes.ObjectModel;

namespace dotnes;

class Transpiler : IDisposable
{
    readonly PEReader _pe;
    readonly MetadataReader _reader;
    readonly IList<AssemblyReader> _assemblyFiles;
    readonly ILogger _logger;

    /// <summary>
    /// A list of methods that were found to be used in the IL code
    /// </summary>
    public HashSet<string> UsedMethods { get; private set; } = new(StringComparer.Ordinal);

    public Transpiler(Stream stream, IList<AssemblyReader> assemblyFiles, ILogger? logger = null)
    {
        _pe = new PEReader(stream);
        _reader = _pe.GetMetadataReader();
        _assemblyFiles = assemblyFiles;
        _logger = logger ?? new NullLogger();
    }

    public void Write(Stream stream)
    {
        if (_assemblyFiles.Count == 0)
            throw new InvalidOperationException("At least one 'chr_generic.s' file must be present!");

        var assemblyReader = _assemblyFiles.FirstOrDefault(a => Path.GetFileName(a.Path) == "chr_generic.s") ?? _assemblyFiles[0];
        var chr_rom = assemblyReader.GetSegments().FirstOrDefault(s => s.Name == "CHARS") ??
            throw new InvalidOperationException($"At least one 'CHARS' segment must be present in: {assemblyReader.Path}");

        _logger.WriteLine($"Building program...");

        // Build the complete program using single-pass transpilation
        var program = BuildProgram6502(out ushort sizeOfMain, out byte locals);
        var programBytes = program.ToBytes();

        _logger.WriteLine($"Size of main: {sizeOfMain}, locals: {locals}");

        // Write the NES ROM
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        // Write NES header (16 bytes)
        _logger.WriteLine($"Writing header...");
        writer.Write('N');
        writer.Write('E');
        writer.Write('S');
        writer.Write((byte)0x1A);
        writer.Write((byte)2); // PRG_ROM_SIZE (2 * 16KB = 32KB)
        writer.Write((byte)1); // CHR_ROM_SIZE (1 * 8KB)
        writer.Write((byte)0); // Flags6
        writer.Write((byte)0); // Flags7
        writer.Write((byte)0); // Flags8
        writer.Write((byte)0); // Flags9
        writer.Write((byte)0); // Flags10
        // Pad header to 16 bytes
        for (int i = 0; i < 5; i++)
            writer.Write((byte)0);

        // Write PRG ROM
        _logger.WriteLine($"Writing PRG ROM ({programBytes.Length} bytes)...");
        writer.Write(programBytes);

        // Pad first PRG ROM bank to 16KB
        int firstBankPadding = NESWriter.PRG_ROM_BLOCK_SIZE - (programBytes.Length % NESWriter.PRG_ROM_BLOCK_SIZE);
        if (firstBankPadding < NESWriter.PRG_ROM_BLOCK_SIZE)
        {
            for (int i = 0; i < firstBankPadding; i++)
                writer.Write((byte)0);
        }

        // Write second PRG ROM bank (16KB), with interrupt vectors at the end
        const int VECTOR_ADDRESSES_SIZE = 6;
        int secondBankPadding = NESWriter.PRG_ROM_BLOCK_SIZE - VECTOR_ADDRESSES_SIZE;
        for (int i = 0; i < secondBankPadding; i++)
            writer.Write((byte)0);

        // Write vectors: NMI, RESET, IRQ (little-endian)
        ushort nmi_data = 0x80BC;
        ushort reset_data = 0x8000;
        ushort irq_data = 0x8202;
        writer.Write((byte)(nmi_data & 0xFF));
        writer.Write((byte)(nmi_data >> 8));
        writer.Write((byte)(reset_data & 0xFF));
        writer.Write((byte)(reset_data >> 8));
        writer.Write((byte)(irq_data & 0xFF));
        writer.Write((byte)(irq_data >> 8));

        // Write CHR ROM
        _logger.WriteLine($"Writing CHR ROM...");
        writer.Write(chr_rom.Bytes);
        
        // Pad CHR ROM to 8KB boundary
        int chrPadding = chr_rom.Bytes.Length % NESWriter.CHR_ROM_BLOCK_SIZE;
        if (chrPadding != 0)
        {
            chrPadding = NESWriter.CHR_ROM_BLOCK_SIZE - chrPadding;
            for (int i = 0; i < chrPadding; i++)
                writer.Write((byte)0);
        }

        writer.Flush();
        _logger.WriteLine($"ROM complete. Total size: {stream.Length} bytes");
    }

    /// <summary>
    /// Builds a full Program6502 object model representation of the transpiled program.
    /// Uses single-pass transpilation with label references, resolves addresses once,
    /// then the program can emit bytes in a single pass.
    /// </summary>
    /// <param name="sizeOfMain">Output: size of the main program in bytes</param>
    /// <param name="locals">Output: number of local variables</param>
    /// <returns>A Program6502 containing built-ins, main program, and final built-ins</returns>
    public Program6502 BuildProgram6502(out ushort sizeOfMain, out byte locals)
    {
        _logger.WriteLine($"Single-pass transpilation...");
        
        var instructions = ReadStaticVoidMain().ToArray();

        // Create the base program with built-ins
        var program = Program6502.CreateWithBuiltIns();

        // Build main program block using label references (addresses resolved later)
        using var writer = new IL2NESWriter(new MemoryStream(), logger: _logger)
        {
            Instructions = instructions,
            UsedMethods = UsedMethods,
        };

        writer.StartBlockBuffering();

        // Translate IL to 6502 (single pass - sizeOfMain = 0 since we'll calculate later)
        for (int i = 0; i < writer.Instructions.Length; i++)
        {
            writer.Index = i;
            var instruction = writer.Instructions[i];
            
            // Record IL instruction labels for branch targets
            // In single-pass mode, we add labels to the block instead of global dictionary
            var labelName = $"instruction_{instruction.Offset:X2}";
            if (writer.CurrentBlock != null)
            {
                // Add label to next instruction
                writer.CurrentBlock.SetNextLabel(labelName);
            }
            
            // Record block count before processing this instruction
            writer.RecordBlockCount(instruction.Offset);
            
            if (instruction.Integer != null)
            {
                writer.Write(instruction, instruction.Integer.Value);
            }
            else if (instruction.String != null)
            {
                writer.Write(instruction, instruction.String);
            }
            else if (instruction.Bytes != null)
            {
                writer.Write(instruction, instruction.Bytes.Value);
            }
            else
            {
                writer.Write(instruction);
            }
        }

        // Get main program as a Block
        var mainBlock = writer.GetMainBlock("main");
        if (mainBlock != null)
        {
            program.AddMainProgram(mainBlock);
            sizeOfMain = (ushort)mainBlock.Size;
        }
        else
        {
            sizeOfMain = 0;
        }

        // Get local count from writer
        locals = checked((byte)writer.LocalCount);

        // Store named ushort[] arrays (note tables) as interleaved 16-bit data (cc65 compatible)
        var noteTableData = new List<(string label, byte[] data)>();
        foreach (var kvp in writer.UShortArrays)
        {
            // Keep raw bytes as-is: interleaved lo/hi pairs (little-endian 16-bit)
            noteTableData.Add((kvp.Key, kvp.Value.ToArray()));
        }

        // Calculate byte array table size
        int byteArrayTableSize = 0;
        foreach (var bytes in writer.ByteArrays)
        {
            byteArrayTableSize += bytes.ToArray().Length;
        }

        // Add note table sizes to the data table size
        foreach (var (_, data) in noteTableData)
        {
            byteArrayTableSize += data.Length;
        }
        
        // Calculate string table size  
        int stringTableSize = 0;
        int stringHeapSize = _reader.GetHeapSize(HeapIndex.UserString);
        if (stringHeapSize > 0)
        {
            var handle = MetadataTokens.UserStringHandle(0);
            do
            {
                string value = _reader.GetUserString(handle);
                if (!string.IsNullOrEmpty(value))
                {
                    stringTableSize += Encoding.ASCII.GetByteCount(value) + 1; // +1 for null terminator
                }
                handle = _reader.GetNextHandle(handle);
            }
            while (!handle.IsNil);
        }

        // Add final built-ins (before data tables)
        // Program layout: built-ins -> main -> final built-ins -> byte/string tables -> destructor
        program.ResolveAddresses(); // Resolve to get current size
        
        // totalSize is used for donelib/copydata - points past the data tables
        // PRG_LAST already accounts for the standard final built-ins size (donelib, copydata, popax,
        // incsp2, popa, pusha, pushax, zerobss with 0 locals). When optional methods change the
        // final built-ins composition, we must add the size delta.
        const ushort PRG_LAST = 0x85AE;
        int standardSize = Program6502.CalculateFinalBuiltInsSize(0, null);
        int actualSize = Program6502.CalculateFinalBuiltInsSize(locals, UsedMethods);
        int finalBuiltInsOffset = actualSize - standardSize;
        ushort totalSize = (ushort)(PRG_LAST.GetAddressAfterMain(sizeOfMain) + finalBuiltInsOffset + byteArrayTableSize + stringTableSize);
        
        program.AddFinalBuiltIns(totalSize, locals, UsedMethods);

        // Add byte array data after final built-ins
        int byteArrayIndex = 0;
        foreach (var bytes in writer.ByteArrays)
        {
            string label = $"bytearray_{byteArrayIndex}";
            program.AddProgramData(bytes.ToArray(), label);
            byteArrayIndex++;
        }

        // Add note table lo/hi data blocks (from ushort[] arrays)
        foreach (var (label, data) in noteTableData)
        {
            program.AddProgramData(data, label);
        }
        
        // Add string table
        if (stringHeapSize > 0)
        {
            var handle = MetadataTokens.UserStringHandle(0);
            do
            {
                string value = _reader.GetUserString(handle);
                if (!string.IsNullOrEmpty(value))
                {
                    // Convert string to ASCII bytes with null terminator
                    byte[] stringBytes = new byte[Encoding.ASCII.GetByteCount(value) + 1];
                    Encoding.ASCII.GetBytes(value, 0, value.Length, stringBytes, 0);
                    stringBytes[stringBytes.Length - 1] = 0; // null terminator
                    program.AddProgramData(stringBytes);
                }
                handle = _reader.GetNextHandle(handle);
            }
            while (!handle.IsNil);
        }

        // Add destructor table LAST
        program.AddDestructorTable();

        // Final address resolution
        program.ResolveAddresses();

        _logger.WriteLine($"Single-pass complete. Size of main: {sizeOfMain}, locals: {locals}");

        return program;
    }

    /// <summary>
    /// Based on: https://github.com/icsharpcode/ILSpy/blob/8c508d9bbbc6a21cc244e930122ff5bca19cd11c/ILSpy/Analyzers/Builtin/MethodUsesAnalyzer.cs#L51
    /// </summary>
    public IEnumerable<ILInstruction> ReadStaticVoidMain()
    {
        GetUsedMethods(_reader);
        var arrayValues = GetArrayValues(_reader);

        foreach (var h in _reader.MethodDefinitions)
        {
            var mainMethod = _reader.GetMethodDefinition(h);
            if ((mainMethod.Attributes & MethodAttributes.Static) == 0)
                continue;

            var mainMethodName = _reader.GetString(mainMethod.Name);
            if (mainMethodName == "Main" || mainMethodName == "<Main>$")
            {
                var body = _pe.GetMethodBody(mainMethod.RelativeVirtualAddress);
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
                                    break;
                                case HandleKind.MemberReference:
                                    var member = _reader.GetMemberReference((MemberReferenceHandle)entity);
                                    stringValue = _reader.GetString(member.Name);
                                    if (stringValue == "InitializeArray")
                                    {
                                        // HACK: skip for now
                                        continue;
                                    }
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
                        case OperandType.ShortR:
                            intValue = blob.ReadInt32();
                            break;
                        case OperandType.Type:
                            {
                                var token = blob.ReadInt32();
                                intValue = token;
                                // Resolve type name for Newarr (e.g. "Byte", "UInt16")
                                if (opCode == ILOpCode.Newarr)
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

                    yield return new ILInstruction(opCode, offset, intValue, stringValue, byteValue);
                }
            }
        }
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

    public void Dispose()
    {
        foreach (var assembly in _assemblyFiles)
        {
            assembly.Dispose();
        }
        _pe.Dispose();
    }
}
