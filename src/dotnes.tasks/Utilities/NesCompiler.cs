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
        using var transpiler = new Transpiler(
            assembly,
            sources,
            logger,
            mapper: options.Mapper,
            mmc3BankedLayout: options.Mmc3BankedLayout)
        {
            LeaveAssemblyReadersOpen = true,
            OptimizeByteHelpers = options.OptimizeByteHelpers,
            OptimizePromotedByteArithmetic = options.OptimizePromotedByteArithmetic,
        };
        return transpiler.CompileProgram(out _, out _);
    }
}
