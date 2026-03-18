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

partial class Transpiler : IDisposable
{
    readonly PEReader _pe;
    readonly MetadataReader _reader;
    readonly IList<AssemblyReader> _assemblyFiles;
    readonly ILogger _logger;
    readonly string _mirroring;
    readonly bool _battery;
    readonly int _mapper;
    readonly int _prgBanks;
    readonly int _chrBanks;

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
    /// User-defined method metadata (name -> arg count, has return value, param types).
    /// Populated by ReadStaticVoidMain().
    /// </summary>
    public Dictionary<string, (int argCount, bool hasReturnValue, bool[] isArrayParam)> UserMethodMetadata { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Extern methods declared with 'static extern' (name -> arg count, has return value).
    /// These map to labels in external .s assembly files (cc65 convention: _name).
    /// </summary>
    public Dictionary<string, (int argCount, bool hasReturnValue)> ExternMethods { get; } = new(StringComparer.Ordinal);

    public Transpiler(Stream stream, IList<AssemblyReader> assemblyFiles, ILogger? logger = null, string mirroring = "Horizontal", int mapper = 0, int prgBanks = 2, int chrBanks = 1, bool battery = false)
    {
        _pe = new PEReader(stream);
        _reader = _pe.GetMetadataReader();
        _assemblyFiles = assemblyFiles;
        _logger = logger ?? new NullLogger();
        _mirroring = mirroring;
        _battery = battery;
        _mapper = mapper;
        _prgBanks = prgBanks;
        _chrBanks = chrBanks;
    }

    public void Write(Stream stream)
    {
        byte[]? chrData = null;

        if (_chrBanks > 0)
        {
            if (_assemblyFiles.Count == 0)
                throw new InvalidOperationException("At least one assembly file with a 'CHARS' segment must be present!");

            // Collect CHARS segments from all assembly files and concatenate them.
            // This supports both single-file CHR (e.g., chr_generic.s with one bank)
            // and multi-file CHR (e.g., chr_slideshow_0.s + chr_slideshow_1.s for CNROM).
            // Each CHARS segment is padded to the 8 KB bank boundary so that multiple
            // banks align correctly (important for mappers like CNROM that switch CHR banks).
            var chrBytes = new List<byte>();
            foreach (var assemblyFile in _assemblyFiles)
            {
                foreach (var segment in assemblyFile.GetSegments())
                {
                    if (segment.Name == "CHARS")
                    {
                        chrBytes.AddRange(segment.Bytes);

                        // Pad this segment to the next 8 KB boundary
                        int remainder = segment.Bytes.Length % NESWriter.CHR_ROM_BLOCK_SIZE;
                        if (remainder > 0)
                        {
                            int bankPadding = NESWriter.CHR_ROM_BLOCK_SIZE - remainder;
                            chrBytes.AddRange(new byte[bankPadding]);
                        }
                    }
                }
            }

            if (chrBytes.Count == 0)
                throw new InvalidOperationException("At least one 'CHARS' segment must be present in the assembly files!");

            chrData = chrBytes.ToArray();
        }

        _logger.WriteLine($"Building program...");

        // Build the complete program using single-pass transpilation
        var program = BuildProgram6502(out ushort sizeOfMain, out ushort locals);
        var programBytes = program.ToBytes();

        _logger.WriteLine($"Size of main: {sizeOfMain}, locals: {locals}");

        // Write the NES ROM
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        // Write NES header (16 bytes)
        _logger.WriteLine($"Writing header (mapper={_mapper}, PRG={_prgBanks}, CHR={_chrBanks})...");
        writer.Write('N');
        writer.Write('E');
        writer.Write('S');
        writer.Write((byte)0x1A);
        writer.Write((byte)_prgBanks); // PRG_ROM_SIZE (in 16KB units)
        writer.Write((byte)_chrBanks); // CHR_ROM_SIZE (in 8KB units, 0 = CHR RAM)
        // Flags6: bit 0 = mirroring, bit 1 = battery-backed SRAM, bits 4-7 = mapper lower nibble
        byte flags6 = (byte)((string.Equals(_mirroring, "Vertical", StringComparison.OrdinalIgnoreCase) ? 1 : 0) | (_battery ? 0x02 : 0) | ((_mapper & 0x0F) << 4));
        writer.Write(flags6);
        // Flags7: bits 4-7 = mapper upper nibble
        byte flags7 = (byte)((_mapper & 0xF0));
        writer.Write(flags7);
        writer.Write((byte)0); // Flags8
        writer.Write((byte)0); // Flags9
        writer.Write((byte)0); // Flags10
        // Pad header to 16 bytes
        for (int i = 0; i < 5; i++)
            writer.Write((byte)0);

        // Write PRG ROM
        _logger.WriteLine($"Writing PRG ROM ({programBytes.Length} bytes)...");
        writer.Write(programBytes);

        // Total PRG ROM size = _prgBanks * 16KB
        int totalPrgSize = _prgBanks * NESWriter.PRG_ROM_BLOCK_SIZE;
        const int VECTOR_ADDRESSES_SIZE = 6;
        int prgPadding = totalPrgSize - programBytes.Length - VECTOR_ADDRESSES_SIZE;
        for (int i = 0; i < prgPadding; i++)
            writer.Write((byte)0);

        // Write vectors: NMI, RESET, IRQ (little-endian) at end of last PRG bank
        // Resolve vector addresses from program labels for correct layout
        var labels = program.GetLabels();
        ushort nmi_data = labels.TryGetValue(NESConstants._nmi, out var nmiAddr) ? nmiAddr : (ushort)0x80BC;
        ushort reset_data = NESConstants.PrgRomStart;
        // Use irq_with_callback handler when irq_set_callback is used, otherwise default _irq handler
        ushort irq_data = labels.TryGetValue(NESConstants.irq_with_callback, out var irqCbAddr) ? irqCbAddr
            : labels.TryGetValue(NESConstants._irq, out var irqAddr) ? irqAddr : (ushort)0x8202;
        writer.Write((byte)(nmi_data & 0xFF));
        writer.Write((byte)(nmi_data >> 8));
        writer.Write((byte)(reset_data & 0xFF));
        writer.Write((byte)(reset_data >> 8));
        writer.Write((byte)(irq_data & 0xFF));
        writer.Write((byte)(irq_data >> 8));

        // Write CHR ROM (skip when chrBanks=0, which means CHR RAM mode)
        if (_chrBanks > 0 && chrData != null)
        {
            int totalChrSize = _chrBanks * NESWriter.CHR_ROM_BLOCK_SIZE;
            if (chrData.Length > totalChrSize)
                throw new InvalidOperationException($"CHR data ({chrData.Length} bytes) exceeds declared CHR ROM size ({totalChrSize} bytes for {_chrBanks} bank(s)). Check NESChrBanks or CHR assembly files.");

            _logger.WriteLine($"Writing CHR ROM ({chrData.Length} bytes)...");
            writer.Write(chrData);
            
            // Pad CHR ROM to total CHR size (_chrBanks * 8KB)
            int chrPadding = totalChrSize - chrData.Length;
            for (int i = 0; i < chrPadding; i++)
                writer.Write((byte)0);
        }
        else
        {
            _logger.WriteLine($"CHR RAM mode (no CHR ROM)...");
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

        // Pre-allocate user-defined static fields so all methods share the same addresses
        var (staticFields, wordStaticFields, staticFieldBytes) = PreAllocateStaticFields(instructions);

        using var writer = new IL2NESWriter(new MemoryStream(), logger: _logger, reflectionCache: reflectionCache)
        {
            Instructions = instructions,
            UsedMethods = UsedMethods,
            UserMethodNames = new HashSet<string>(UserMethods.Keys, StringComparer.Ordinal),
            ExternMethodNames = externNames,
            WordLocals = DetectWordLocals(instructions, reflectionCache),
            StructLayouts = structLayouts,
            StaticFieldAddresses = staticFields,
            WordStaticFields = wordStaticFields,
            LocalCount = staticFieldBytes,
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
        // Each method's locals must use unique addresses to prevent collisions in nested calls
        int mainLocalCount = writer.LocalCount;
        var methodFrameOffsets = ComputeMethodFrameOffsets(UserMethods, reflectionCache, mainLocalCount, structLayouts);
        int userMethodsTotalSize = 0;
        foreach (var kvp in UserMethods.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var methodName = kvp.Key;
            var methodIL = kvp.Value;
            int paramCount = UserMethodMetadata.TryGetValue(methodName, out var meta) ? meta.argCount : 0;
            bool[] isArrayParam = meta.isArrayParam ?? Array.Empty<bool>();
            using var methodWriter = new IL2NESWriter(new MemoryStream(), logger: _logger, reflectionCache: reflectionCache)
            {
                Instructions = methodIL,
                UsedMethods = UsedMethods,
                UserMethodNames = new HashSet<string>(UserMethods.Keys, StringComparer.Ordinal),
                MethodParamCount = paramCount,
                ParamIsArray = isArrayParam,
                MethodName = methodName,
                WordLocals = DetectWordLocals(methodIL, reflectionCache),
                StructLayouts = structLayouts,
                ByteArrayLabelStartIndex = writer.ByteArrays.Count,
                StringLabelStartIndex = writer.StringTable.Count,
                LocalCount = methodFrameOffsets[methodName],
                StaticFieldAddresses = staticFields,
                WordStaticFields = wordStaticFields,
            };
            methodWriter.StartBlockBuffering();

            // If method has parameters, emit prologue to push last arg onto cc65 stack
            if (paramCount > 0)
            {
                // byte[] params are 16-bit pointers: use pushax (2 bytes) instead of pusha (1 byte)
                if (isArrayParam.Length > 0 && isArrayParam[paramCount - 1])
                    methodWriter.EmitJSR("pushax");
                else
                    methodWriter.EmitJSR("pusha");
            }

            for (int i = 0; i < methodWriter.Instructions.Length; i++)
            {
                methodWriter.Index = i;
                var instruction = methodWriter.Instructions[i];

                var labelName = $"{methodName}_instruction_{instruction.Offset:X2}";                if (methodWriter.CurrentBlock != null)
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
                // byte[] params take 2 bytes on the cc65 stack, byte params take 1
                if (paramCount > 0)
                {
                    int stackBytes = 0;
                    for (int p = 0; p < paramCount; p++)
                        stackBytes += (p < isArrayParam.Length && isArrayParam[p]) ? 2 : 1;

                    switch (stackBytes)
                    {
                        case 1:
                            methodBlock.Emit(JSR("incsp1"));
                            UsedMethods.Add("incsp1");
                            break;
                        case 2:
                            methodBlock.Emit(JSR("incsp2"));
                            break;
                        default:
                            methodBlock.Emit(LDY((byte)stackBytes));
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
        foreach (var kvp in writer.UShortArrays.OrderBy(x => x.Key, StringComparer.Ordinal))
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

        // Scan emitted blocks for JSR pusha/pushax label references.
        // These may be emitted for byte array/string parameter passing even in programs
        // that use decsp4. We must detect actual usage AFTER code generation (not during
        // EmitJSR) because block buffering can remove instructions before flushing.
        foreach (var block in program.Blocks)
        {
            foreach (var (instr, _) in block.InstructionsWithLabels)
            {
                if (instr.Opcode == Opcode.JSR && instr.Operand is LabelOperand lo
                    && lo.Label is "pusha" or "pushax")
                {
                    UsedMethods.Add(lo.Label);
                }
            }
        }

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

    public void Dispose()
    {
        foreach (var assembly in _assemblyFiles)
        {
            assembly.Dispose();
        }
        _pe.Dispose();
    }
}