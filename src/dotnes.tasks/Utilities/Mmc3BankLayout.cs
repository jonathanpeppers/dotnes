using dotnes.ObjectModel;

namespace dotnes;

sealed record BankedRomAsset(string Path, int Bank, int Offset, ushort? CpuAddress = null);

static class Mmc3BankLayout
{
    public const int PrgBankSize = 8192;
    public const int ChrBankSize = 1024;
    public const ushort FirstSwitchableWindow = 0x8000;
    public const ushort SecondSwitchableWindow = 0xA000;
    public const ushort FixedProgramAddress = 0xC000;
    const int InterruptVectorSize = 6;

    static readonly byte[] ResetStub =
    [
        0xA9, 0x00,       // LDA #$00: select register 0 with PRG mode 0
        0x8D, 0x00, 0x80, // STA $8000
        0x4C, 0x00, 0xC0, // JMP $C000
    ];

    public static ushort ResetStubAddress => checked((ushort)(0x10000 - InterruptVectorSize - ResetStub.Length));

    sealed record PreparedPrgAsset(
        BankedRomAsset Asset,
        ushort CpuAddress,
        Program6502? Program,
        byte[]? RawBytes,
        Dictionary<string, ushort> Labels);

    public static byte[] BuildPrgImage(
        Program6502 program,
        int prgBanks,
        IReadOnlyList<BankedRomAsset> assets,
        ushort nmiAddress,
        ushort irqAddress,
        IReadOnlyList<ManagedCodeRegion>? managedRegions = null,
        Action<IReadOnlyList<Program6502>>? prepareManagedPrograms = null,
        IReadOnlyList<CompiledPrgAsset>? compiledPrgAssets = null)
    {
        int physicalBankCount = checked(prgBanks * 2);
        if (physicalBankCount < 4)
            throw new InvalidOperationException("MMC3 banked layout requires at least 2 NESPrgBanks (four physical 8 KiB PRG banks).");
        if (program.BaseAddress != FixedProgramAddress)
            throw new InvalidOperationException($"MMC3 fixed program must be linked at ${FixedProgramAddress:X4}, not ${program.BaseAddress:X4}.");

        List<PreparedPrgAsset>? prepared = null;
        if (managedRegions != null)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in managedRegions)
            {
                BankedCompilation.ValidateRegion(region);
                if (!names.Add(region.Placement.Name))
                    throw new InvalidOperationException($"Duplicate managed code region '{region.Placement.Name}'.");
                if (region.Placement.Bank >= physicalBankCount - 2)
                    throw new InvalidOperationException(
                        $"Managed code region '{region.Placement.Name}' selects bank {region.Placement.Bank}. " +
                        $"Valid switchable banks are 0-{physicalBankCount - 3}; the last two banks are reserved for the fixed program.");
            }

            compiledPrgAssets ??= PrepareManagedPrgAssets(assets, prgBanks);
            prepared = compiledPrgAssets.Select(item => new PreparedPrgAsset(
                new BankedRomAsset(item.Placement.Path, item.Placement.Bank, item.Placement.Offset, item.Placement.CpuAddress),
                item.Placement.CpuAddress, item.Program, item.Data,
                new Dictionary<string, ushort>(StringComparer.Ordinal))).ToList();
            var programs = new List<Program6502> { program };
            programs.AddRange(managedRegions.Select(region => region.Program));
            programs.AddRange(prepared.Where(item => item.Program != null).Select(item => item.Program!));
            BankedCompilation.LinkPrograms(programs);
            if (prepareManagedPrograms != null)
            {
                prepareManagedPrograms(programs);
                BankedCompilation.LinkPrograms(programs);
            }
            BankedCompilation.ValidateSizes(program, managedRegions);
            ValidateManagedPlacements(managedRegions, compiledPrgAssets, prgBanks);
            foreach (var imageProgram in programs)
                BankedCompilation.ValidateForEmission(imageProgram);

            var labels = program.GetDefinedLabels();
            if (!labels.TryGetValue(NESConstants._nmi, out nmiAddress))
                throw new InvalidOperationException($"Required fixed label '{NESConstants._nmi}' not found.");
            if (!labels.TryGetValue(NESConstants.irq_with_callback, out irqAddress) &&
                !labels.TryGetValue(NESConstants._irq, out irqAddress))
                throw new InvalidOperationException($"Required fixed label '{NESConstants._irq}' not found.");
        }

        var programBytes = program.ToBytes();
        int fixedProgramCapacity = (PrgBankSize * 2) - ResetStub.Length - InterruptVectorSize;
        if (programBytes.Length > fixedProgramCapacity)
        {
            throw new InvalidOperationException(
                $"Transpiled program ({programBytes.Length} bytes) exceeds the MMC3 fixed-bank capacity " +
                $"({fixedProgramCapacity} bytes at $C000-$FFF1).");
        }

        var image = new byte[checked(physicalBankCount * PrgBankSize)];
        var occupied = new bool[image.Length];
        var owners = new string?[image.Length];
        int fixedProgramOffset = (physicalBankCount - 2) * PrgBankSize;
        PlaceBytes(image, occupied, owners, fixedProgramOffset, programBytes, "transpiled program");

        int vectorsOffset = image.Length - InterruptVectorSize;
        int resetStubOffset = vectorsOffset - ResetStub.Length;
        PlaceBytes(image, occupied, owners, resetStubOffset, ResetStub, "MMC3 reset stub");
        var vectors = new byte[]
        {
            (byte)nmiAddress, (byte)(nmiAddress >> 8),
            (byte)(ResetStubAddress & 0xFF), (byte)(ResetStubAddress >> 8),
            (byte)irqAddress, (byte)(irqAddress >> 8),
        };
        PlaceBytes(image, occupied, owners, vectorsOffset, vectors, "interrupt vectors");

        prepared ??= PreparePrgAssets(assets, physicalBankCount, program.GetLabels());
        var windowByBank = new Dictionary<int, ushort>();
        if (managedRegions != null)
        {
            foreach (var region in managedRegions)
            {
                var placement = region.Placement;
                windowByBank[placement.Bank] = FirstSwitchableWindow;
                var bytes = new byte[placement.Size];
                region.Program.ToBytes().CopyTo(bytes, 0);
                int destination = checked((placement.Bank * PrgBankSize) + placement.Offset);
                PlaceBytes(image, occupied, owners, destination, bytes, $"managed code region '{placement.Name}'");
            }
        }
        foreach (var item in prepared)
        {
            var asset = item.Asset;
            ushort cpuAddress = item.CpuAddress;
            if (windowByBank.TryGetValue(asset.Bank, out ushort existingWindow) && existingWindow != cpuAddress)
            {
                throw new InvalidOperationException(
                    $"Physical PRG bank {asset.Bank} has conflicting CPU windows " +
                    $"${existingWindow:X4} and ${cpuAddress:X4}.");
            }
            windowByBank[asset.Bank] = cpuAddress;
        }

        foreach (var item in prepared)
        {
            byte[] bytes;
            if (item.Program == null)
            {
                bytes = item.RawBytes!;
            }
            else
            {
                try
                {
                    bytes = item.Program.ToBytes();
                }
                catch (UnresolvedLabelException ex)
                {
                    throw new InvalidOperationException(
                        $"PRG bank asset '{item.Asset.Path}' contains an unresolved label: {ex.Message}", ex);
                }
            }

            if (bytes.Length > PrgBankSize - item.Asset.Offset)
            {
                throw new InvalidOperationException(
                    $"PRG bank asset '{item.Asset.Path}' ({bytes.Length} bytes at offset {item.Asset.Offset}) " +
                    $"exceeds physical 8 KiB bank {item.Asset.Bank}.");
            }

            int destination = checked((item.Asset.Bank * PrgBankSize) + item.Asset.Offset);
            PlaceBytes(image, occupied, owners, destination, bytes, item.Asset.Path);
        }

        return image;
    }

    internal static IReadOnlyList<CompiledPrgAsset> PrepareManagedPrgAssets(
        IReadOnlyList<BankedRomAsset> assets,
        int prgBanks)
    {
        if (prgBanks < 2)
            throw new InvalidOperationException("MMC3 banked layout requires at least 2 NESPrgBanks.");
        var prepared = PreparePrgAssets(assets, checked(prgBanks * 2), programLabels: null);
        var result = prepared.Select(item => new CompiledPrgAsset(
            new PrgBankAsset
            {
                Path = item.Asset.Path,
                Bank = item.Asset.Bank,
                CpuAddress = item.CpuAddress,
                Offset = item.Asset.Offset,
            }, item.Program, item.RawBytes)).ToArray();
        return Array.AsReadOnly(result);
    }

    internal static void ValidateManagedPlacements(
        IReadOnlyList<ManagedCodeRegion> regions,
        IReadOnlyList<CompiledPrgAsset> assets,
        int? prgBanks)
    {
        if (prgBanks.HasValue && prgBanks.Value < 2)
            throw new InvalidOperationException("MMC3 banked layout requires at least 2 NESPrgBanks.");
        int? physicalBankCount = prgBanks.HasValue ? checked(prgBanks.Value * 2) : null;
        var placements = new List<(int Bank, int Offset, int Size, ushort Window, string Owner)>();

        void AddPlacement(int bank, int offset, int size, ushort window, string owner)
        {
            if (bank < 0 || (physicalBankCount.HasValue && bank >= physicalBankCount.Value - 2))
                throw new InvalidOperationException(
                    $"Placement '{owner}' selects invalid/reserved physical PRG bank {bank}.");
            if (offset < 0 || offset >= PrgBankSize)
                throw new InvalidOperationException($"Placement '{owner}' offset must be between 0 and {PrgBankSize - 1}.");
            if (window != FirstSwitchableWindow && window != SecondSwitchableWindow)
                throw new InvalidOperationException($"Placement '{owner}' CpuAddress must be $8000 or $A000.");
            if (size > PrgBankSize - offset)
                throw new InvalidOperationException(
                    $"Placement '{owner}' ({size} bytes at offset {offset}) exceeds physical 8 KiB bank {bank}.");
            foreach (var previous in placements)
            {
                if (previous.Bank != bank)
                    continue;
                if (previous.Window != window)
                    throw new InvalidOperationException(
                        $"Physical PRG bank {bank} has conflicting CPU windows ${previous.Window:X4} and ${window:X4}.");
                if (size != 0 && previous.Size != 0 &&
                    offset < previous.Offset + previous.Size && previous.Offset < offset + size)
                    throw new InvalidOperationException($"Placement '{owner}' overlaps '{previous.Owner}'.");
            }
            placements.Add((bank, offset, size, window, owner));
        }

        foreach (var region in regions)
        {
            BankedCompilation.ValidateRegion(region);
            var placement = region.Placement;
            AddPlacement(placement.Bank, placement.Offset, placement.Size, placement.CpuAddress,
                $"managed code region '{placement.Name}'");
        }
        foreach (var asset in assets)
        {
            var placement = asset.Placement;
            AddPlacement(placement.Bank, placement.Offset, asset.Program?.TotalSize ?? asset.Data!.Length,
                placement.CpuAddress, placement.Path);
            if (asset.Program != null && asset.Program.BaseAddress != placement.CpuAddress + placement.Offset)
                throw new InvalidOperationException(
                    $"PRG bank asset '{placement.Path}' program address must match CpuAddress + Offset.");
        }
    }

    public static byte[] BuildChrImage(
        byte[] legacyChrData,
        int chrBanks,
        IReadOnlyList<BankedRomAsset> assets)
    {
        int physicalBankCount = checked(chrBanks * 8);
        var image = new byte[checked(physicalBankCount * ChrBankSize)];
        var occupied = new bool[image.Length];
        var owners = new string?[image.Length];

        if (legacyChrData.Length > image.Length)
        {
            throw new InvalidOperationException(
                $"CHR data ({legacyChrData.Length} bytes) exceeds declared CHR ROM size " +
                $"({image.Length} bytes for {chrBanks} bank(s)). Check NESChrBanks or CHR assembly files.");
        }
        PlaceBytes(image, occupied, owners, 0, legacyChrData, "legacy CHARS segments");

        foreach (var asset in assets
            .OrderBy(a => a.Bank)
            .ThenBy(a => a.Offset)
            .ThenBy(a => a.Path, StringComparer.Ordinal))
        {
            ValidateAssetPath(asset);
            if (asset.Bank < 0 || asset.Bank >= physicalBankCount)
            {
                throw new InvalidOperationException(
                    $"CHR bank asset '{asset.Path}' selects physical 1 KiB bank {asset.Bank}, " +
                    $"but valid banks are 0-{physicalBankCount - 1}.");
            }
            if (asset.Offset < 0 || asset.Offset >= ChrBankSize)
                throw new InvalidOperationException($"CHR bank asset '{asset.Path}' offset must be between 0 and {ChrBankSize - 1}.");

            byte[] bytes = ReadChrAsset(asset.Path);
            if (bytes.Length > ChrBankSize - asset.Offset)
            {
                throw new InvalidOperationException(
                    $"CHR bank asset '{asset.Path}' ({bytes.Length} bytes at offset {asset.Offset}) " +
                    $"exceeds physical 1 KiB bank {asset.Bank}.");
            }

            int destination = checked((asset.Bank * ChrBankSize) + asset.Offset);
            PlaceBytes(image, occupied, owners, destination, bytes, asset.Path);
        }

        return image;
    }

    static List<PreparedPrgAsset> PreparePrgAssets(
        IReadOnlyList<BankedRomAsset> assets,
        int physicalBankCount,
        Dictionary<string, ushort>? programLabels)
    {
        var prepared = new List<PreparedPrgAsset>();
        foreach (var asset in assets
            .OrderBy(a => a.Bank)
            .ThenBy(a => a.Offset)
            .ThenBy(a => a.Path, StringComparer.Ordinal))
        {
            ValidateAssetPath(asset);
            if (asset.Bank < 0 || asset.Bank >= physicalBankCount - 2)
            {
                throw new InvalidOperationException(
                    $"PRG bank asset '{asset.Path}' selects physical 8 KiB bank {asset.Bank}. " +
                    $"Valid switchable banks are 0-{physicalBankCount - 3}; banks {physicalBankCount - 2} and " +
                    $"{physicalBankCount - 1} are reserved for the fixed $C000/$E000 program windows.");
            }
            if (asset.Offset < 0 || asset.Offset >= PrgBankSize)
                throw new InvalidOperationException($"PRG bank asset '{asset.Path}' offset must be between 0 and {PrgBankSize - 1}.");
            ushort cpuAddress = asset.CpuAddress switch
            {
                FirstSwitchableWindow => FirstSwitchableWindow,
                SecondSwitchableWindow => SecondSwitchableWindow,
                _ => throw new InvalidOperationException(
                    $"PRG bank asset '{asset.Path}' CpuAddress must be $8000 or $A000 for a switchable MMC3 window."),
            };

            string extension = System.IO.Path.GetExtension(asset.Path);
            if (string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
            {
                prepared.Add(new PreparedPrgAsset(
                    asset,
                    cpuAddress,
                    Program: null,
                    File.ReadAllBytes(asset.Path),
                    new Dictionary<string, ushort>(StringComparer.Ordinal)));
                continue;
            }
            if (!string.Equals(extension, ".s", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"PRG bank asset '{asset.Path}' must use the .s or .bin extension.");

            var assembler = new Ca65Assembler();
            List<Block> blocks;
            using (var reader = new StreamReader(File.OpenRead(asset.Path)))
                blocks = assembler.Assemble(reader);
            if (blocks.Count == 0)
                throw new InvalidOperationException($"PRG bank asset '{asset.Path}' did not produce any code or data.");

            var assetProgram = new Program6502 { BaseAddress = (ushort)(asset.CpuAddress.Value + asset.Offset) };
            foreach (var block in blocks)
            {
                if (programLabels == null && !block.IsDataBlock)
                    assetProgram.AddNativeBlock(block);
                else
                    assetProgram.AddBlock(block);
            }
            if (programLabels != null)
                assetProgram.ResolveAndRelaxBranches();

            prepared.Add(new PreparedPrgAsset(
                asset,
                cpuAddress,
                assetProgram,
                RawBytes: null,
                programLabels == null ? new Dictionary<string, ushort>(StringComparer.Ordinal) : assetProgram.GetLabels()));
        }

        if (programLabels == null)
            return prepared;

        var labelOwners = new Dictionary<string, PreparedPrgAsset?>(StringComparer.Ordinal);
        foreach (var label in programLabels.Keys)
            labelOwners[label] = null;
        foreach (var item in prepared)
        {
            foreach (var label in item.Labels.Keys)
            {
                if (IsSyntheticAnonymousLabel(label))
                    continue;
                if (labelOwners.ContainsKey(label))
                    throw new InvalidOperationException($"PRG bank asset '{item.Asset.Path}' defines duplicate label '{label}'.");
                labelOwners[label] = item;
            }
        }

        foreach (var item in prepared)
        {
            if (item.Program == null)
                continue;

            foreach (var label in labelOwners)
            {
                if (!item.Labels.ContainsKey(label.Key))
                {
                    ushort address = label.Value == null
                        ? programLabels[label.Key]
                        : label.Value.Labels[label.Key];
                    item.Program.DefineExternalLabel(label.Key, address);
                }
            }
            item.Program.InvalidateAddresses();
            item.Program.ResolveAddresses();

            foreach (var block in item.Program.Blocks)
            {
                item.Program.Labels.CurrentScope = block.Label;
                if (block.Relocations != null)
                {
                    foreach (var (_, label) in block.Relocations)
                    {
                        if (!item.Program.Labels.TryResolve(label, out _))
                        {
                            throw new InvalidOperationException(
                                $"PRG bank asset '{item.Asset.Path}' contains an unresolved data relocation '{label}'.");
                        }
                    }
                }

                foreach (var (instruction, _) in block.InstructionsWithLabels)
                {
                    if (instruction.Operand is RelativeOperand relative &&
                        labelOwners.TryGetValue(relative.Label, out var targetOwner) &&
                        targetOwner != item)
                    {
                        throw new InvalidOperationException(
                            $"PRG bank asset '{item.Asset.Path}' has a relative branch to '{relative.Label}' " +
                            "outside its physical bank placement. Use an absolute JMP/JSR after selecting the target bank.");
                    }
                }
            }
            item.Program.Labels.CurrentScope = null;
        }

        return prepared;
    }

    static bool IsSyntheticAnonymousLabel(string label)
        => label.StartsWith("_anonymous_code_", StringComparison.Ordinal);

    static byte[] ReadChrAsset(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        if (string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllBytes(path);
        if (!string.Equals(extension, ".s", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"CHR bank asset '{path}' must use the .s or .bin extension.");

        var bytes = new List<byte>();
        using var reader = new AssemblyReader(path);
        foreach (var segment in reader.GetSegments())
        {
            if (segment.Name == "CHARS")
                bytes.AddRange(segment.Bytes);
        }
        if (bytes.Count == 0)
            throw new InvalidOperationException($"CHR bank asset '{path}' does not contain a non-empty 'CHARS' segment.");
        return bytes.ToArray();
    }

    static void ValidateAssetPath(BankedRomAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Path))
            throw new InvalidOperationException("Bank asset path cannot be empty.");
        if (!File.Exists(asset.Path))
            throw new FileNotFoundException($"Bank asset '{asset.Path}' does not exist.", asset.Path);
    }

    static void PlaceBytes(
        byte[] destination,
        bool[] occupied,
        string?[] owners,
        int offset,
        byte[] bytes,
        string owner)
    {
        if (offset < 0 || offset > destination.Length - bytes.Length)
            throw new InvalidOperationException($"Placement '{owner}' is outside the ROM image.");

        for (int i = 0; i < bytes.Length; i++)
        {
            int index = offset + i;
            if (occupied[index])
            {
                throw new InvalidOperationException(
                    $"Placement '{owner}' overlaps '{owners[index]}' at ROM offset ${index:X}.");
            }
        }

        Array.Copy(bytes, 0, destination, offset, bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
        {
            int index = offset + i;
            occupied[index] = true;
            owners[index] = owner;
        }
    }
}
