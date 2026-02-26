using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

class Transpiler : IDisposable
{
    readonly PEReader _pe;
    readonly MetadataReader _reader;
    readonly IList<AssemblyReader> _assemblyFiles;
    readonly ILogger _logger;
    readonly bool _verticalMirroring;

    /// <summary>
    /// A list of methods that were found to be used in the IL code
    /// </summary>
    public HashSet<string> UsedMethods { get; private set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// User-defined methods (name -> IL instructions).
    /// Populated by ReadStaticVoidMain().
    /// </summary>
    public Dictionary<string, ILInstruction[]> UserMethods { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// User-defined method metadata (name -> arg count, has return value).
    /// Populated by ReadStaticVoidMain().
    /// </summary>
    public Dictionary<string, (int argCount, bool hasReturnValue)> UserMethodMetadata { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Extern methods declared with 'static extern' (name -> arg count, has return value).
    /// These map to labels in external .s assembly files (cc65 convention: _name).
    /// </summary>
    public Dictionary<string, (int argCount, bool hasReturnValue)> ExternMethods { get; } = new(StringComparer.Ordinal);

    public Transpiler(Stream stream, IList<AssemblyReader> assemblyFiles, ILogger? logger = null, bool verticalMirroring = false)
    {
        _pe = new PEReader(stream);
        _reader = _pe.GetMetadataReader();
        _assemblyFiles = assemblyFiles;
        _logger = logger ?? new NullLogger();
        _verticalMirroring = verticalMirroring;
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
        var program = BuildProgram6502(out ushort sizeOfMain, out ushort locals);
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
        writer.Write((byte)(_verticalMirroring ? 1 : 0)); // Flags6 (bit 0: 0=horizontal, 1=vertical mirroring)
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
    /// <param name="locals">Output: number of local variable bytes</param>
    /// <returns>A Program6502 containing built-ins, main program, and final built-ins</returns>
    public Program6502 BuildProgram6502(out ushort sizeOfMain, out ushort locals)
    {
        _logger.WriteLine($"Single-pass transpilation...");
        
        var instructions = ReadStaticVoidMain().ToArray();

        // Create the base program with built-ins
        var program = Program6502.CreateWithBuiltIns();

        // Register user methods with the reflection cache so Call handler knows about them
        var reflectionCache = new ReflectionCache();
        foreach (var kvp in UserMethodMetadata)
        {
            reflectionCache.RegisterUserMethod(kvp.Key, kvp.Value.argCount, kvp.Value.hasReturnValue);
        }
        // Register extern methods so Call handler can look up arg counts
        foreach (var kvp in ExternMethods)
        {
            reflectionCache.RegisterExternMethod(kvp.Key, kvp.Value.argCount, kvp.Value.hasReturnValue);
        }

        // Build main program block using label references (addresses resolved later)
        var externNames = new HashSet<string>(ExternMethods.Keys, StringComparer.Ordinal);
        var structLayouts = DetectStructLayouts();
        using var writer = new IL2NESWriter(new MemoryStream(), logger: _logger, reflectionCache: reflectionCache)
        {
            Instructions = instructions,
            UsedMethods = UsedMethods,
            UserMethodNames = new HashSet<string>(UserMethods.Keys, StringComparer.Ordinal),
            ExternMethodNames = externNames,
            WordLocals = DetectWordLocals(instructions),
            StructLayouts = structLayouts,
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

        // Add music subroutines BEFORE main (matches cc65 ROM layout)
        int musicSubroutinesSize = Program6502.CalculateMusicSubroutinesSize(UsedMethods);
        program.AddMusicSubroutines(UsedMethods);

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

        // Transpile user-defined methods into separate blocks
        int userMethodsTotalSize = 0;
        foreach (var kvp in UserMethods)
        {
            var methodName = kvp.Key;
            var methodIL = kvp.Value;
            int paramCount = UserMethodMetadata.TryGetValue(methodName, out var meta) ? meta.argCount : 0;
            using var methodWriter = new IL2NESWriter(new MemoryStream(), logger: _logger, reflectionCache: reflectionCache)
            {
                Instructions = methodIL,
                UsedMethods = UsedMethods,
                UserMethodNames = new HashSet<string>(UserMethods.Keys, StringComparer.Ordinal),
                MethodParamCount = paramCount,
                WordLocals = DetectWordLocals(methodIL),
                StructLayouts = structLayouts,
            };
            methodWriter.StartBlockBuffering();

            // If method has parameters, emit prologue to push last arg onto cc65 stack
            if (paramCount > 0)
            {
                methodWriter.EmitJSR("pusha");
            }

            for (int i = 0; i < methodWriter.Instructions.Length; i++)
            {
                methodWriter.Index = i;
                var instruction = methodWriter.Instructions[i];

                var labelName = $"instruction_{instruction.Offset:X2}";
                if (methodWriter.CurrentBlock != null)
                    methodWriter.CurrentBlock.SetNextLabel(labelName);
                methodWriter.RecordBlockCount(instruction.Offset);

                if (instruction.Integer != null)
                    methodWriter.Write(instruction, instruction.Integer.Value);
                else if (instruction.String != null)
                    methodWriter.Write(instruction, instruction.String);
                else if (instruction.Bytes != null)
                    methodWriter.Write(instruction, instruction.Bytes.Value);
                else
                    methodWriter.Write(instruction);
            }

            var methodBlock = methodWriter.GetMainBlock(methodName);
            if (methodBlock != null)
            {
                // Clean up cc65 stack for params, then return
                if (paramCount > 0)
                {
                    switch (paramCount)
                    {
                        case 1:
                            methodBlock.Emit(JSR("incsp1"));
                            UsedMethods.Add("incsp1");
                            break;
                        case 2:
                            methodBlock.Emit(JSR("incsp2"));
                            break;
                        default:
                            methodBlock.Emit(LDY((byte)paramCount));
                            methodBlock.Emit(JSR("addysp"));
                            UsedMethods.Add("addysp");
                            break;
                    }
                }
                methodBlock.Emit(new Instruction(Opcode.RTS, AddressMode.Implied));
                program.AddMainProgram(methodBlock);
                userMethodsTotalSize += methodBlock.Size;
                _logger.WriteLine($"User method '{methodName}': {methodBlock.Size} bytes ({paramCount} params)");
            }

            // Collect string/byte array data from user method writers
            foreach (var (label, data) in methodWriter.StringTable)
                writer.MergeStringTableEntry(label, data);
            foreach (var bytes in methodWriter.ByteArrays)
                writer.MergeByteArray(bytes);
        }

        // Parse and add extern code blocks from .s assembly files using ca65 assembler
        int externBlocksTotalSize = 0;
        if (ExternMethods.Count > 0)
        {
            foreach (var assemblyFile in _assemblyFiles)
            {
                if (!File.Exists(assemblyFile.Path))
                    continue;

                var ca65 = new Ca65Assembler();
                using (var reader = new StreamReader(assemblyFile.Path))
                {
                    var blocks = ca65.Assemble(reader);
                    foreach (var block in blocks)
                    {
                        program.AddBlock(block);
                        externBlocksTotalSize += block.Size;
                        _logger.WriteLine($"Extern block '{block.Label}': {block.Size} bytes");
                    }
                }

                // Register label aliases from the assembler (e.g., _famitone_init=FamiToneInit)
                // Import symbols from the assembly that need to resolve to dotnes built-ins
                foreach (var importName in ca65.Imports)
                {
                    _logger.WriteLine($"Assembly import: {importName}");
                }
            }
        }

        // Get local count from writer
        locals = (ushort)writer.LocalCount;

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
        
        // Calculate string table size from writer's tracked strings
        int stringTableSize = 0;
        foreach (var (_, data) in writer.StringTable)
        {
            stringTableSize += data.Length;
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
        ushort totalSize = (ushort)(PRG_LAST.GetAddressAfterMain(sizeOfMain) + finalBuiltInsOffset + musicSubroutinesSize + userMethodsTotalSize + externBlocksTotalSize + byteArrayTableSize + stringTableSize);
        
        program.AddFinalBuiltIns(totalSize, locals, UsedMethods);

        // Add note table data blocks BEFORE byte arrays (matches cc65 layout)
        foreach (var (label, data) in noteTableData)
        {
            program.AddProgramData(data, label);
        }

        // Add byte array data (music data etc.)
        int byteArrayIndex = 0;
        foreach (var bytes in writer.ByteArrays)
        {
            string label = $"bytearray_{byteArrayIndex}";
            program.AddProgramData(bytes.ToArray(), label);
            byteArrayIndex++;
        }
        
        // Add string table with labels for address resolution
        foreach (var (label, data) in writer.StringTable)
        {
            program.AddProgramData(data, label);
        }

        // Add destructor table LAST
        program.AddDestructorTable();

        // Final address resolution
        program.ResolveAddresses();

        // Log block addresses for diagnostics
        _logger.WriteLine($"Block layout ({program.Blocks.Count} blocks):");
        ushort diagAddr = program.BaseAddress;
        foreach (var block in program.Blocks)
        {
            _logger.WriteLine($"  ${diagAddr:X4}: [{block.Label}] {block.Size} bytes, data={block.IsDataBlock}");
            diagAddr += (ushort)block.Size;
        }

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
                UserMethodMetadata[cleanName] = (paramCount, hasReturnValue);
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

    public void Dispose()
    {
        foreach (var assembly in _assemblyFiles)
        {
            assembly.Dispose();
        }
        _pe.Dispose();
    }

    /// <summary>
    /// Pre-scan IL instructions for conv.u2 + stloc patterns to detect ushort locals.
    /// </summary>
    static HashSet<int> DetectWordLocals(ILInstruction[] instructions)
    {
        var result = new HashSet<int>();
        for (int i = 0; i < instructions.Length - 1; i++)
        {
            if (instructions[i].OpCode != ILOpCode.Conv_u2)
                continue;
            var next = instructions[i + 1];
            int? idx = next.OpCode switch
            {
                ILOpCode.Stloc_0 => 0,
                ILOpCode.Stloc_1 => 1,
                ILOpCode.Stloc_2 => 2,
                ILOpCode.Stloc_3 => 3,
                ILOpCode.Stloc_s => next.Integer,
                _ => null
            };
            if (idx.HasValue)
                result.Add(idx.Value);
        }
        return result;
    }

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
            // Skip compiler-generated types
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
}
