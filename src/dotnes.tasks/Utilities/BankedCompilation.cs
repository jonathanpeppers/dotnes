using dotnes.ObjectModel;

namespace dotnes;

/// <summary>A native assembly or binary asset placed in a switchable MMC3 PRG bank.</summary>
public sealed class PrgBankAsset
{
    /// <summary>The path to an assembly (.s) or raw binary (.bin) file.</summary>
    public string Path { get; set; } = "";

    /// <summary>The zero-based physical 8 KiB bank, excluding the final two fixed banks.</summary>
    public int Bank { get; set; }

    /// <summary>The CPU window base address, $8000 or $A000.</summary>
    public ushort CpuAddress { get; set; } = 0x8000;

    /// <summary>The byte offset within the physical bank. Defaults to zero.</summary>
    public int Offset { get; set; }
}

/// <summary>A prepared PRG asset whose model is shared by compilation and ROM emission.</summary>
public sealed class CompiledPrgAsset
{
    internal CompiledPrgAsset(PrgBankAsset placement, Program6502? program, byte[]? data)
    {
        if (placement == null)
            throw new ArgumentNullException(nameof(placement));
        if ((program == null) == (data == null))
            throw new ArgumentException("A compiled PRG asset must contain either a program or binary data.");
        Placement = new PrgBankAsset
        {
            Path = placement.Path,
            Bank = placement.Bank,
            CpuAddress = placement.CpuAddress,
            Offset = placement.Offset,
        };
        Program = program;
        Data = data?.ToArray();
    }

    /// <summary>The placement configuration captured when the asset was prepared.</summary>
    public PrgBankAsset Placement { get; }

    /// <summary>The editable assembly model, or null for a raw binary asset.</summary>
    public Program6502? Program { get; }

    /// <summary>The editable raw binary bytes, or null for an assembly asset.</summary>
    public byte[]? Data { get; }
}

/// <summary>A managed code image and its reserved physical MMC3 placement.</summary>
public sealed class ManagedCodeRegion
{
    /// <summary>Creates a region, copying its placement configuration.</summary>
    public ManagedCodeRegion(ManagedCodeBank placement, Program6502 program)
    {
        if (placement == null)
            throw new ArgumentNullException(nameof(placement));
        if (program == null)
            throw new ArgumentNullException(nameof(program));
        Placement = new ManagedCodeBank
        {
            Name = placement.Name,
            Bank = placement.Bank,
            CpuAddress = placement.CpuAddress,
            Offset = placement.Offset,
            Size = placement.Size,
        };
        Program = program;
    }

    /// <summary>The placement configuration captured for this region.</summary>
    public ManagedCodeBank Placement { get; }

    /// <summary>The editable code and data model for this region.</summary>
    public Program6502 Program { get; }
}

/// <summary>Coordinated fixed and switchable managed 6502 program images.</summary>
public sealed class BankedCompilation
{
    readonly int? prgBanks;

    internal BankedCompilation(
        Program6502 fixedProgram,
        IReadOnlyList<ManagedCodeRegion> regions,
        IReadOnlyList<CompiledPrgAsset>? prgAssets = null,
        int? prgBanks = null)
    {
        if (fixedProgram == null)
            throw new ArgumentNullException(nameof(fixedProgram));
        if (regions == null)
            throw new ArgumentNullException(nameof(regions));
        FixedProgram = fixedProgram;
        Regions = Array.AsReadOnly(regions.ToArray());
        PrgAssets = Array.AsReadOnly(prgAssets?.ToArray() ?? Array.Empty<CompiledPrgAsset>());
        this.prgBanks = prgBanks;
    }

    /// <summary>The fixed $C000 program, including runtime code and shared tables.</summary>
    public Program6502 FixedProgram { get; }

    /// <summary>
    /// Prepared fixed native assembly blocks, including their data, in emission order.
    /// These are the same editable instances held by FixedProgram, not reassembled copies.
    /// Runtime startup, dispatchers, managed methods, and gates are not included.
    /// </summary>
    public IReadOnlyList<Block> FixedNativeBlocks =>
        Array.AsReadOnly(FixedProgram.Blocks.Where(FixedProgram.IsNativeBlock).ToArray());

    /// <summary>The managed images in their declared regions.</summary>
    public IReadOnlyList<ManagedCodeRegion> Regions { get; }

    /// <summary>Prepared native assembly and binary assets, sharing their emission models.</summary>
    public IReadOnlyList<CompiledPrgAsset> PrgAssets { get; }

    /// <summary>
    /// Re-links all images after model edits, refreshing imports after branch relaxation.
    /// Unbound external symbols may be supplied with Program6502.DefineExternalLabel
    /// before emitting bytes.
    /// </summary>
    public void ResolveAndRelax()
    {
        if (FixedProgram.BaseAddress != Mmc3BankLayout.FixedProgramAddress)
            throw new InvalidOperationException("The managed banking fixed program must start at $C000.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in Regions)
        {
            ValidateRegion(region);
            if (!names.Add(region.Placement.Name))
                throw new InvalidOperationException($"Duplicate managed code region '{region.Placement.Name}'.");
        }
        LinkPrograms(GetPrograms());
        ValidateSizes(FixedProgram, Regions);
        Mmc3BankLayout.ValidateManagedPlacements(Regions, PrgAssets, prgBanks);
    }

    internal IReadOnlyList<Program6502> GetPrograms()
    {
        var programs = new List<Program6502> { FixedProgram };
        programs.AddRange(Regions.Select(region => region.Program));
        programs.AddRange(PrgAssets.Where(asset => asset.Program != null).Select(asset => asset.Program!));
        return programs;
    }

    internal static void ValidateRegion(ManagedCodeRegion region)
    {
        var placement = region.Placement;
        if (string.IsNullOrWhiteSpace(placement.Name))
            throw new InvalidOperationException("Managed code region name cannot be empty.");
        if (placement.Bank < 0 || placement.Bank > byte.MaxValue)
            throw new InvalidOperationException($"Managed code region '{placement.Name}' Bank must be between 0 and 255.");
        if (placement.CpuAddress != Mmc3BankLayout.FirstSwitchableWindow)
            throw new InvalidOperationException($"Managed code region '{placement.Name}' CpuAddress must be $8000 (MMC3 R6).");
        if (placement.Offset < 0 || placement.Offset >= Mmc3BankLayout.PrgBankSize ||
            placement.Size <= 0 || placement.Size > Mmc3BankLayout.PrgBankSize - placement.Offset)
            throw new InvalidOperationException(
                $"Managed code region '{placement.Name}' Offset and Size must reserve a nonempty range within an 8 KiB PRG bank.");
        if (region.Program.BaseAddress != placement.CpuAddress + placement.Offset)
            throw new InvalidOperationException(
                $"Managed code region '{placement.Name}' program address must match CpuAddress + Offset.");
    }

    internal static void ValidateSizes(Program6502 fixedProgram, IReadOnlyList<ManagedCodeRegion> regions)
    {
        int fixedCapacity = Mmc3BankLayout.ResetStubAddress - Mmc3BankLayout.FixedProgramAddress;
        if (fixedProgram.TotalSize > fixedCapacity)
            throw new InvalidOperationException(
                $"Transpiled program ({fixedProgram.TotalSize} bytes) exceeds the MMC3 fixed-bank capacity " +
                $"({fixedCapacity} bytes at $C000-$FFF1).");
        foreach (var region in regions)
        {
            if (region.Program.TotalSize > region.Placement.Size)
                throw new InvalidOperationException(
                    $"Managed code region '{region.Placement.Name}' ({region.Program.TotalSize} bytes) " +
                    $"exceeds its reserved Size ({region.Placement.Size} bytes).");
        }
    }

    internal static void LinkPrograms(IReadOnlyList<Program6502> programs)
    {
        if (programs.Distinct().Count() != programs.Count)
            throw new InvalidOperationException("Each linked image must have its own Program6502 model.");
        foreach (var program in programs)
        {
            program.ValidateAddressRange();
            program.SetLinkedLabels(new Dictionary<string, ushort>());
        }

        Dictionary<string, (Program6502 Owner, ushort Address)>? previous = null;
        while (true)
        {
            var owners = new Dictionary<string, (Program6502 Owner, ushort Address)>(StringComparer.Ordinal);
            foreach (var program in programs)
            {
                program.ValidateAddressRange();
                program.ResolveAddresses();
                foreach (var label in program.GetDefinedLabels())
                {
                    if (IsPrivateLabel(label.Key))
                        continue;
                    if (owners.ContainsKey(label.Key))
                        throw new InvalidOperationException($"Linked programs define duplicate label '{label.Key}'.");
                    owners.Add(label.Key, (program, label.Value));
                }
            }

            foreach (var program in programs)
            {
                var imports = new Dictionary<string, ushort>(StringComparer.Ordinal);
                foreach (var label in owners)
                {
                    if (label.Value.Owner != program)
                        imports.Add(label.Key, label.Value.Address);
                }
                program.SetLinkedLabels(imports);
                program.ResolveAddresses();
                ValidateRelativeBranches(program, owners);
            }

            bool grew = false;
            foreach (var program in programs)
            {
                int size = program.TotalSize;
                program.ResolveAndRelaxBranches();
                grew |= size != program.TotalSize;
            }
            if (!grew && previous != null && owners.Count == previous.Count &&
                owners.All(label => previous.TryGetValue(label.Key, out var value) && value == label.Value))
                return;
            previous = owners;
        }
    }

    static bool IsPrivateLabel(string name)
        => name.StartsWith("_anonymous_code_", StringComparison.Ordinal) || name.StartsWith("@", StringComparison.Ordinal);

    static void ValidateRelativeBranches(
        Program6502 program,
        IReadOnlyDictionary<string, (Program6502 Owner, ushort Address)> owners)
    {
        var local = program.GetDefinedLabels();
        foreach (var block in program.Blocks)
        {
            foreach (var (instruction, _) in block.InstructionsWithLabels)
            {
                if (instruction.Operand is not RelativeOperand relative)
                    continue;
                string name = relative.Label;
                if (name.StartsWith("@", StringComparison.Ordinal) && block.Label != null &&
                    local.ContainsKey($"{block.Label}:{name}"))
                    name = $"{block.Label}:{name}";
                if (local.ContainsKey(name))
                    continue;
                program.Labels.CurrentScope = block.Label;
                if (owners.ContainsKey(name) || program.Labels.TryResolve(relative.Label, out _))
                    throw new InvalidOperationException(
                        $"Program at ${program.BaseAddress:X4} has a relative branch to '{relative.Label}' " +
                        "outside its physical bank placement. Use an absolute JMP/JSR after selecting the target bank.");
            }
        }
        program.Labels.CurrentScope = null;
    }

    internal static void ValidateForEmission(Program6502 program)
    {
        try
        {
            foreach (var block in program.Blocks)
            {
                program.Labels.CurrentScope = block.Label;
                if (block.Relocations == null)
                    continue;
                foreach (var (offset, label) in block.Relocations)
                {
                    if (block.RawData == null || offset < 0 || offset > block.RawData.Length - 2)
                        throw new InvalidOperationException($"Invalid data relocation '{label}' at offset {offset}.");
                    if (!program.Labels.TryResolve(label, out _))
                        throw new InvalidOperationException($"Program contains an unresolved data relocation '{label}'.");
                }
            }
        }
        finally
        {
            program.Labels.CurrentScope = null;
        }
    }
}
