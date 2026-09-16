using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Compiles a .NET assembly to the same 6502 program model used by ROM builds.
/// </summary>
public static class NesCompiler
{
    /// <summary>
    /// Compiles an assembly without writing an iNES header, graphics, or interrupt vectors.
    /// External labels can be bound on the returned program before emitting bytes.
    /// </summary>
    /// <param name="assembly">Readable, seekable PE/IL stream positioned at the start of the assembly.</param>
    /// <param name="options">Program target and layout options, or null for the defaults.</param>
    /// <param name="assemblyFiles">Optional native sources accepted by the ca65-compatible assembler.</param>
    /// <param name="logger">Optional compiler diagnostic logger.</param>
    /// <returns>A self-contained program with labels assigned, ready for external binding and branch relaxation.</returns>
    /// <remarks>
    /// The caller owns all inputs. Neither the assembly stream nor the assembly readers are
    /// disposed, even on failure. Their positions may advance; reset the assembly stream before
    /// reuse. AssemblyReader caches its source for reuse. Compiler exceptions propagate unchanged.
    /// </remarks>
    public static Program6502 Compile(
        Stream assembly,
        CompilationOptions? options = null,
        IEnumerable<AssemblyReader>? assemblyFiles = null,
        ILogger? logger = null)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (!assembly.CanRead || !assembly.CanSeek)
            throw new ArgumentException("The assembly stream must be readable and seekable.", nameof(assembly));

        var sources = assemblyFiles?.ToList() ?? new List<AssemblyReader>();
        if (sources.Any(source => source == null))
            throw new ArgumentException("Assembly sources must not contain null readers.", nameof(assemblyFiles));

        options ??= new CompilationOptions();
        if (options.PrgBankAssets.Count != 0)
            throw new ArgumentException("PRG asset models require CompileBanked; Compile returns only a flat program.", nameof(options));
        using var transpiler = new Transpiler(
            assembly,
            sources,
            logger,
            mapper: options.Mapper,
            prgBanks: options.PrgBanks,
            mmc3BankedLayout: options.Mmc3BankedLayout,
            managedCodeBanks: options.ManagedCodeBanks.ToArray(),
            mmc3ManagedHomeBank: options.Mmc3ManagedHomeBank,
            mmc3ManagedInterruptContract: options.Mmc3ManagedInterruptContract,
            nativeRamCode: options.NativeRamCode.ToArray())
        {
            LeaveAssemblyReadersOpen = true,
            OptimizeByteHelpers = options.OptimizeByteHelpers,
        };
        return transpiler.CompileProgram(out _, out _);
    }

    /// <summary>
    /// Compiles explicitly annotated managed methods into MMC3 regions and fixed code.
    /// The returned models use the same gates, RAM allocation and linking as stock ROM builds.
    /// Native callback and mapper effects require source-visible bodies.
    /// </summary>
    /// <remarks>
    /// The caller owns all inputs. This method does not write an iNES image or CHR data.
    /// Use the stock package targets for ROM packaging; use this result to inspect emitted code.
    /// </remarks>
    public static BankedCompilation CompileBanked(
        Stream assembly,
        CompilationOptions options,
        IEnumerable<AssemblyReader>? assemblyFiles = null,
        ILogger? logger = null)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (!assembly.CanRead || !assembly.CanSeek)
            throw new ArgumentException("The assembly stream must be readable and seekable.", nameof(assembly));
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        var sources = assemblyFiles?.ToList() ?? new List<AssemblyReader>();
        if (sources.Any(source => source == null))
            throw new ArgumentException("Assembly sources must not contain null readers.", nameof(assemblyFiles));
        using var transpiler = new Transpiler(
            assembly, sources, logger,
            mapper: options.Mapper, prgBanks: options.PrgBanks,
            mmc3BankedLayout: options.Mmc3BankedLayout,
            prgBankAssets: GetPrgAssets(options),
            managedCodeBanks: options.ManagedCodeBanks.ToArray(),
            mmc3ManagedHomeBank: options.Mmc3ManagedHomeBank,
            mmc3ManagedInterruptContract: options.Mmc3ManagedInterruptContract,
            nativeRamCode: options.NativeRamCode.ToArray())
        {
            LeaveAssemblyReadersOpen = true,
            OptimizeByteHelpers = options.OptimizeByteHelpers,
        };
        var result = transpiler.CompileManagedProgram(out _, out _);
        result.ResolveAndRelax();
        transpiler.PrepareManagedMapperContext(result.GetPrograms(), result.PrgAssets);
        result.ResolveAndRelax();
        return result;
    }

    static BankedRomAsset[] GetPrgAssets(CompilationOptions options)
    {
        var assets = new List<BankedRomAsset>();
        foreach (var asset in options.PrgBankAssets)
        {
            if (asset == null)
                throw new ArgumentException("PRG assets must not contain null entries.", nameof(options));
            assets.Add(new BankedRomAsset(asset.Path, asset.Bank, asset.Offset, asset.CpuAddress));
        }
        return assets.ToArray();
    }
}
