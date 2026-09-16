using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using dotnes.ObjectModel;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

partial class Transpiler
{
    readonly IReadOnlyList<ManagedCodeBank> _managedCodeBanks;
    readonly int? _managedHomeBank;
    readonly Mmc3ManagedInterruptContract _managedInterruptContract;
    readonly IReadOnlyList<NativeRamCode> _nativeRamCode;
    readonly Dictionary<MethodDefinitionHandle, string> _managedMethodNames = new();
    readonly Dictionary<string, string> _methodRegions = new(StringComparer.Ordinal);
    readonly HashSet<string> _managedCallbacks = new(StringComparer.Ordinal);
    readonly HashSet<string> _managedForeground = new(StringComparer.Ordinal);
    readonly HashSet<Block> _managedBlocks = new();
    readonly HashSet<Block> _compilerOwnedBlocks = new();
    readonly Dictionary<Block, byte> _managedGateBanks = new();
    ushort _selectorShadow, _savedSelector;
    bool _buildingManagedProgram;
    const string EnterManagedBank = "__nesbank_enter";
    const string LeaveManagedBank = "__nesbank_leave";

    void ValidateManagedConfiguration()
    {
        if (_managedCodeBanks.Count == 0)
        {
            if (_managedHomeBank != null || _managedInterruptContract != Mmc3ManagedInterruptContract.None || _nativeRamCode.Count != 0)
                throw new TranspileException("Managed MMC3 home/interrupt options require NESManagedCodeBank regions.");
            return;
        }
        if (!_mmc3BankedLayout || _mapper != 4)
            throw new TranspileException("NESManagedCodeBank requires NESMapper=4 and NESMmc3BankedLayout=true.");
        if (_managedHomeBank is not int home || home < 0 || home >= _prgBanks * 2 - 2)
            throw new TranspileException("NESMmc3ManagedHomeBank must name a valid switchable physical PRG bank.");
        if (_managedInterruptContract is not (Mmc3ManagedInterruptContract.None or Mmc3ManagedInterruptContract.NonNestingChrCallbacks))
            throw new TranspileException("Unsupported managed MMC3 interrupt contract.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in _managedCodeBanks)
        {
            if (region == null || string.IsNullOrWhiteSpace(region.Name) || !names.Add(region.Name))
                throw new TranspileException("NESManagedCodeBank region names must be nonempty and unique (case-sensitive).");
            if (region.Bank < 0 || region.Bank >= _prgBanks * 2 - 2)
                throw new TranspileException($"Managed region '{region.Name}' selects invalid/reserved physical PRG bank {region.Bank}.");
            if (region.CpuAddress != Mmc3BankLayout.FirstSwitchableWindow)
                throw new TranspileException($"Managed region '{region.Name}' requires the MMC3 R6 $8000 window.");
            if (region.Offset < 0 || region.Size <= 0 ||
                region.Offset > Mmc3BankLayout.PrgBankSize - region.Size)
                throw new TranspileException($"Managed region '{region.Name}' reservation must fit within one physical 8 KiB bank.");
        }
        foreach (var region in _managedCodeBanks)
        foreach (var other in _managedCodeBanks)
            if (!ReferenceEquals(region, other) && region.Bank == other.Bank &&
                region.Offset < other.Offset + other.Size && other.Offset < region.Offset + region.Size)
                throw new TranspileException($"Managed regions '{region.Name}' and '{other.Name}' overlap in physical bank {region.Bank}.");
        var ramNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _nativeRamCode)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || !ramNames.Add(entry.Name))
                throw new TranspileException("Native RAM code contract names must be nonempty and unique.");
            if (entry.Contract != NativeRamCodeContract.ForegroundRtsPreservesMapperContext)
                throw new TranspileException($"Native RAM code '{entry.Name}' requires an explicit ForegroundRtsPreservesMapperContext contract.");
            if (entry.Address < 0x6000 || entry.Address >= 0x8000 || entry.Size <= 0 || entry.Size > 0x8000 - entry.Address)
                throw new TranspileException($"Native RAM code '{entry.Name}' must fit entirely in PRG RAM $6000-$7FFF.");
            foreach (var other in _nativeRamCode)
                if (!ReferenceEquals(entry, other) && other != null &&
                    entry.Address < (long)other.Address + other.Size && other.Address < (long)entry.Address + entry.Size)
                    throw new TranspileException($"Native RAM code contracts '{entry.Name}' and '{other.Name}' overlap.");
        }
    }

    void DiscoverManagedBanks()
    {
        _managedMethodNames.Clear();
        _methodRegions.Clear();
        var regions = new HashSet<string>(_managedCodeBanks.Select(bank => bank.Name), StringComparer.Ordinal);
        int entry = _pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var type = _reader.GetTypeDefinition(typeHandle);
            string typeName = _reader.GetString(type.Name);
            string? typeRegion = ReadCodeBankAttribute(type.GetCustomAttributes(), typeName);
            if (typeRegion != null && !regions.Contains(typeRegion))
                throw new TranspileException($"NESCodeBank '{typeRegion}' on '{typeName}' has no NESManagedCodeBank region.");
            if (typeRegion != null && ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) !=
                    (TypeAttributes.Abstract | TypeAttributes.Sealed) || type.GetGenericParameters().Count != 0))
                throw new TranspileException($"NESCodeBank class '{typeName}' must be static and nongeneric.");
            bool hasBankedMethod = false;
            bool hasInitializer = false;
            foreach (var handle in type.GetMethods())
            {
                var method = _reader.GetMethodDefinition(handle);
                string name = _reader.GetString(method.Name);
                hasInitializer |= name == ".cctor";
                string? methodRegion = ReadCodeBankAttribute(method.GetCustomAttributes(), $"{typeName}.{name}");
                string? region = methodRegion ?? typeRegion;
                if (region == null || name.StartsWith(".", StringComparison.Ordinal))
                    continue;
                if (methodRegion == null && ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 ||
                    method.RelativeVirtualAddress == 0))
                    continue;
                if (!regions.Contains(region))
                    throw new TranspileException($"NESCodeBank '{region}' on '{typeName}.{name}' has no NESManagedCodeBank region.");
                if ((method.Attributes & MethodAttributes.Static) == 0 || method.RelativeVirtualAddress == 0 ||
                    method.GetGenericParameters().Count != 0 || type.GetGenericParameters().Count != 0 ||
                    (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
                    MetadataTokens.GetToken(handle) == entry || name is "Main" or "<Main>$")
                    throw new TranspileException($"NESCodeBank requires an ordinary nongeneric static managed method, not '{typeName}.{name}'.");
                var signature = method.DecodeSignature(new NumericTypeDecoder(), null);
                if (signature.ParameterTypes.Any(t => t is not (PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte or PrimitiveTypeCode.Boolean)) ||
                    signature.ReturnType is not (PrimitiveTypeCode.Void or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte or
                        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16))
                    throw new TranspileException(
                        $"Banked method '{typeName}.{name}' requires by-value byte/sbyte/bool arguments and a supported scalar/void return; " +
                        "arrays, references, pointers, and captured closures cannot cross a managed gate.");
                string symbol = $"__nesbank_method_{MetadataTokens.GetRowNumber(handle):X8}_{name}";
                _managedMethodNames.Add(handle, symbol);
                _methodRegions.Add(symbol, region);
                hasBankedMethod = true;
            }
            if ((typeRegion != null || hasBankedMethod) && hasInitializer)
                throw new TranspileException(
                    $"Banked type '{typeName}' has unsupported implicit static initialization (.cctor). Initialize shared state explicitly from C#.");
        }
        foreach (string region in regions)
            if (!_methodRegions.Values.Contains(region, StringComparer.Ordinal))
                throw new TranspileException($"Managed region '{region}' has no annotated managed methods.");
        if (_methodRegions.Count > 0)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in _reader.FieldDefinitions)
            {
                var field = _reader.GetFieldDefinition(handle);
                if ((field.Attributes & (FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasFieldRVA)) != FieldAttributes.Static)
                    continue;
                string name = _reader.GetString(field.Name);
                if (!fields.Add(name))
                    throw new TranspileException($"Managed banking requires unambiguous shared static field identities; duplicate field '{name}'.");
            }
        }
    }

    string? ReadCodeBankAttribute(CustomAttributeHandleCollection attributes, string member)
    {
        string? result = null;
        foreach (var handle in attributes)
        {
            var attribute = _reader.GetCustomAttribute(handle);
            EntityHandle declaringType;
            if (attribute.Constructor.Kind == HandleKind.MemberReference)
                declaringType = _reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
                declaringType = _reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            else
                continue;
            string name, ns;
            if (declaringType.Kind == HandleKind.TypeReference)
            {
                var type = _reader.GetTypeReference((TypeReferenceHandle)declaringType);
                name = _reader.GetString(type.Name);
                ns = _reader.GetString(type.Namespace);
            }
            else if (declaringType.Kind == HandleKind.TypeDefinition)
            {
                var type = _reader.GetTypeDefinition((TypeDefinitionHandle)declaringType);
                name = _reader.GetString(type.Name);
                ns = _reader.GetString(type.Namespace);
            }
            else
                continue;
            if (ns != "NES" || name != "NESCodeBankAttribute")
                continue;
            var blob = _reader.GetBlobReader(attribute.Value);
            if (result != null || blob.RemainingBytes < 3 || blob.ReadUInt16() != 1)
                throw new TranspileException($"Malformed or repeated NESCodeBank metadata on '{member}'.");
            result = blob.ReadSerializedString();
            if (string.IsNullOrWhiteSpace(result) || blob.RemainingBytes != 2 || blob.ReadUInt16() != 0)
                throw new TranspileException($"NESCodeBank on '{member}' requires exactly one nonempty region name.");
        }
        return result;
    }

    bool SameManagedRegion(string caller, string callee) =>
        _methodRegions.TryGetValue(caller, out string? region) &&
        _methodRegions.TryGetValue(callee, out string? target) && region == target;

    void ValidateManagedMethods(ILInstruction[] main)
    {
        if (_methodRegions.Count == 0)
            return;
        if (!_buildingManagedProgram)
            throw new TranspileException("Managed banking requires NesCompiler.CompileBanked or the stock ROM build, not a flat Compile result.");
        if (_ambiguousByteHelperNames)
            throw new TranspileException("Managed banking cannot resolve duplicate unbanked method names.");
        _managedCallbacks.Clear();
        _managedForeground.Clear();
        _managedForeground.Add("main");
        foreach (var pair in UserMethods.Prepend(new KeyValuePair<string, ILInstruction[]>("main", main)))
        {
            bool banked = _methodRegions.ContainsKey(pair.Key);
            for (int index = 0; index < pair.Value.Length; index++)
            {
                var instruction = pair.Value[index];
                if (instruction.OpCode is ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Ldvirtftn)
                    throw new TranspileException("Indirect/virtual calls are not supported with managed banking.", pair.Key);
                if (instruction.OpCode == ILOpCode.Ldftn)
                {
                    int next = index + 1;
                    while (next < pair.Value.Length && pair.Value[next].OpCode == ILOpCode.Nop)
                        next++;
                    if (banked || next == pair.Value.Length || pair.Value[next].OpCode != ILOpCode.Call ||
                        pair.Value[next].String is not ("nmi_set_callback" or "irq_set_callback") ||
                        instruction.String is not string callback ||
                        !ExternMethods.TryGetValue(callback, out var signature) || signature.argCount != 0 || signature.hasReturnValue)
                        throw new TranspileException("Managed banking only permits direct addresses of void, parameterless native interrupt callbacks.", pair.Key);
                    if (_managedInterruptContract != Mmc3ManagedInterruptContract.NonNestingChrCallbacks)
                        throw new TranspileException("Native callbacks require NESMmc3ManagedInterruptContract=NonNestingChrCallbacks.");
                    _managedCallbacks.Add($"_{callback}");
                }
                if (instruction.OpCode != ILOpCode.Call || instruction.String is not string callee)
                    continue;
                if (banked && (callee is "nmi_set_callback" or "irq_set_callback" ||
                    UserMethods.ContainsKey(callee) && !SameManagedRegion(pair.Key, callee)))
                    throw new TranspileException($"Banked method cannot call fixed managed code or another region ('{callee}').", pair.Key);
            }
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string method)
        {
            if (active.Contains(method))
                throw new TranspileException("Recursive/reentrant calls are not supported with managed banking.", method);
            if (!visited.Add(method))
                return;
            active.Add(method);
            foreach (var call in UserMethods[method])
                if (call.OpCode == ILOpCode.Call && call.String is string callee && UserMethods.ContainsKey(callee))
                    Visit(callee);
            active.Remove(method);
        }
        foreach (string method in UserMethods.Keys)
            Visit(method);
    }

    internal BankedCompilation CompileManagedProgram(out ushort sizeOfMain, out ushort locals)
    {
        ValidateRomConfiguration();
        if (_managedCodeBanks.Count == 0)
            throw new TranspileException("CompileBanked requires at least one managed code region.");
        Program6502 program;
        _buildingManagedProgram = true;
        try
        {
            program = BuildProgram6502(out sizeOfMain, out locals, Mmc3BankLayout.FixedProgramAddress);
        }
        finally
        {
            _buildingManagedProgram = false;
        }
        _managedBlocks.Clear();
        _compilerOwnedBlocks.Clear();
        _managedGateBanks.Clear();
        foreach (var block in program.Blocks)
        {
            if (program.IsNativeBlock(block) || block.IsDataBlock)
                continue;
            if (block.Label == "main" || block.Label is string label && UserMethods.ContainsKey(label))
                _managedBlocks.Add(block);
            else
                _compilerOwnedBlocks.Add(block);
        }
        var regions = _managedCodeBanks.OrderBy(region => region.Name, StringComparer.Ordinal)
            .Select(region => new ManagedCodeRegion(region,
                new Program6502 { BaseAddress = checked((ushort)(region.CpuAddress + region.Offset)) })).ToArray();
        foreach (var region in regions)
        foreach (var block in program.Blocks.ToArray())
            if (block.Label is string label && _methodRegions.TryGetValue(label, out var name) && name == region.Placement.Name)
            {
                program.RemoveBlock(block);
                region.Program.AddBlock(block);
            }
        var exported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in program.Blocks)
        for (int index = 0; index < block.Count; index++)
        {
            var instruction = block[index];
            if (instruction.Operand is not LabelOperand label || !_methodRegions.ContainsKey(label.Label))
                continue;
            if (instruction.Opcode != Opcode.JSR)
                throw new TranspileException($"Banked method '{label.Label}' may only be entered through an ordinary call.");
            exported.Add(label.Label);
            block.Replace(index, instruction with { Operand = new LabelOperand(GateName(label.Label), OperandSize.Word) });
        }
        foreach (string method in exported.OrderBy(name => name, StringComparer.Ordinal))
        {
            var region = _managedCodeBanks.Single(region => region.Name == _methodRegions[method]);
            var gate = program.CreateBlock(GateName(method))
                .Emit(PHP()).Emit(PHA())
                .Emit(LDA((byte)region.Bank)).Emit(JSR(EnterManagedBank))
                .Emit(PLA()).Emit(PLP()).Emit(JSR(method))
                .Emit(JMP(LeaveManagedBank));
            _compilerOwnedBlocks.Add(gate);
            _managedGateBanks.Add(gate, checked((byte)region.Bank));
        }
        var enter = program.CreateBlock(EnterManagedBank)
            .Emit(PHA()).Emit(LDA_abs(_selectorShadow)).Emit(STA_abs(_savedSelector))
            .Emit(AND(0x80)).Emit(ORA(6)).Emit(STA_abs(_selectorShadow)).Emit(STA_abs(NESLib.MMC3_BANK_SELECT))
            .Emit(PLA()).Emit(STA_abs(NESLib.MMC3_BANK_DATA)).Emit(RTS());
        var leave = program.CreateBlock(LeaveManagedBank)
            .Emit(PHP()).Emit(PHA())
            .Emit(LDA_abs(_savedSelector)).Emit(AND(0x80)).Emit(ORA(6))
            .Emit(STA_abs(_selectorShadow)).Emit(STA_abs(NESLib.MMC3_BANK_SELECT))
            .Emit(LDA(checked((byte)_managedHomeBank!.Value))).Emit(STA_abs(NESLib.MMC3_BANK_DATA))
            .Emit(LDA_abs(_savedSelector)).Emit(STA_abs(_selectorShadow)).Emit(STA_abs(NESLib.MMC3_BANK_SELECT))
            .Emit(PLA()).Emit(PLP()).Emit(RTS());
        _compilerOwnedBlocks.Add(enter);
        _compilerOwnedBlocks.Add(leave);
        PatchManagedStartup(program);
        foreach (string name in new[] { NESConstants.skipNtsc, NESConstants.irq_with_callback })
        {
            var block = program.GetBlock(name);
            if (block == null)
                continue;
            int callback = Enumerable.Range(0, block.Count).Single(i => block[i].Opcode == Opcode.JSR);
            block.Insert(callback + 1, LDA_abs(_selectorShadow));
            block.Insert(callback + 2, STA_abs(NESLib.MMC3_BANK_SELECT));
        }
        program.DefineExternalLabel("__nesbank_selector", _selectorShadow);
        program.DefineExternalLabel("__nesbank_saved_selector", _savedSelector);
        program.InvalidateAddresses();
        var assets = Mmc3BankLayout.PrepareManagedPrgAssets(_prgBankAssets, _prgBanks);
        return new BankedCompilation(program, regions, assets, _prgBanks);
    }

    static string GateName(string method) => $"__nesbank_gate_{method}";

    static void DefineManagedRamLabels(Program6502 program, IL2NESWriter writer,
        IReadOnlyDictionary<string, ushort> staticFields, int mainFrameOffset)
    {
        program.DefineExternalLabel("__nesbank_main_locals", checked((ushort)(NESConstants.LocalStackBase + mainFrameOffset)));
        foreach (var field in staticFields)
            program.DefineExternalLabel($"__nesbank_static_{field.Key}", field.Value);
        var arrays = writer.Variables.Locals.Where(pair => pair.Value.Address.HasValue && pair.Value.ArraySize > 0)
            .OrderBy(pair => pair.Value.Address).ThenBy(pair => pair.Key).ToArray();
        foreach (var array in arrays)
        {
            program.DefineExternalLabel($"__nesbank_main_array_{array.Key}", checked((ushort)array.Value.Address!.Value));
            program.DefineExternalLabel($"__nesbank_main_array_{array.Key}_size", checked((ushort)array.Value.ArraySize));
        }
        if (arrays.Length > 0)
            program.DefineExternalLabel("__nesbank_main_first_array", checked((ushort)arrays[0].Value.Address!.Value));
    }

    void PatchManagedStartup(Program6502 program)
    {
        const string table = "__DESTRUCTOR_TABLE__";
        var copy = program.GetBlock("copydata") ?? throw new TranspileException("Missing copydata startup block.");
        copy.Replace(0, new Instruction(Opcode.LDA, AddressMode.Immediate_LowByte, new LowByteOperand(table)));
        copy.Replace(2, new Instruction(Opcode.LDA, AddressMode.Immediate_HighByte, new HighByteOperand(table)));
        var done = program.GetBlock("donelib") ?? throw new TranspileException("Missing donelib startup block.");
        done.Replace(2, new Instruction(Opcode.LDA, AddressMode.Immediate_LowByte, new LowByteOperand(table)));
        done.Replace(3, new Instruction(Opcode.LDX, AddressMode.Immediate_HighByte, new HighByteOperand(table)));
        var clear = program.GetBlock("clearRAM") ?? throw new TranspileException("Missing clearRAM startup block.");
        int beforeNmi = Enumerable.Range(0, clear.Count).Single(i =>
            clear[i].Opcode == Opcode.STA && clear[i].Operand is AbsoluteOperand { Address: NESConstants.PPU_CTRL });
        // Keep the original PPUCTRL value and flags while initializing the write-only mapper context.
        Instruction[] initialize =
        [
            PHP(), PHA(), LDA(6), STA_abs(_selectorShadow), STA_abs(NESLib.MMC3_BANK_SELECT),
            LDA(checked((byte)_managedHomeBank!.Value)), STA_abs(NESLib.MMC3_BANK_DATA), PLA(), PLP(),
        ];
        for (int i = 0; i < initialize.Length; i++)
            clear.Insert(beforeNmi + i, initialize[i]);
    }

    internal void PrepareManagedMapperContext(IReadOnlyList<Program6502> programs, IReadOnlyList<CompiledPrgAsset> prgAssets)
    {
        bool preservesBankSelector = ManagedMapperSafety.Prepare(programs, _managedCallbacks, _managedForeground, _methodRegions.Keys.ToArray(),
            _selectorShadow, _managedHomeBank!.Value, _nativeRamCode, _managedBlocks, _compilerOwnedBlocks, prgAssets);
        var program = programs[0];
        if (preservesBankSelector)
        {
            // Callbacks restore the published selector. Without banked selector
            // stores, R6 is still selected when the managed callee returns.
            var leave = program.GetBlock(LeaveManagedBank)!;
            for (int index = 0; index < 5; index++)
                leave.RemoveAt(2);
            program.InvalidateAddresses();
            _logger.WriteLine($"Managed banked code preserves the selector; return gates omit redundant R6 selection.");
        }
        var enter = program.GetBlock(EnterManagedBank);
        if (_managedGateBanks.Count == 0 || enter == null)
            return;
        // Gates are appended after authored code. Expanding their branch-free entry
        // sequences cannot change authored relative-branch distances.
        int growth = checked(_managedGateBanks.Count * 16 - enter.Size);
        if (program.TotalSize + growth > Mmc3BankLayout.ResetStubAddress - Mmc3BankLayout.FixedProgramAddress)
        {
            _logger.WriteLine($"Managed gates retain compact entries to fit the fixed-bank capacity.");
            return;
        }
        foreach (var pair in _managedGateBanks)
        {
            Instruction[] entry =
            [
                LDA_abs(_selectorShadow), STA_abs(_savedSelector), AND(0x80), ORA(6),
                STA_abs(_selectorShadow), STA_abs(NESLib.MMC3_BANK_SELECT),
                LDA(pair.Value), STA_abs(NESLib.MMC3_BANK_DATA),
            ];
            pair.Key.RemoveAt(3);
            pair.Key.RemoveAt(2);
            for (int index = 0; index < entry.Length; index++)
                pair.Key.Insert(index + 2, entry[index]);
        }
        program.RemoveBlock(enter);
        program.InvalidateAddresses();
        _logger.WriteLine($"Managed gates inline {_managedGateBanks.Count} entries within the fixed-bank capacity ({growth} bytes).");
    }
}
