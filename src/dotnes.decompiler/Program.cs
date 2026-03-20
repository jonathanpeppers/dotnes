using dotnes;

var logger = new ConsoleLogger();

if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("Usage: dotnes-decompiler <input.nes> [output-directory]");
    Console.WriteLine();
    Console.WriteLine("Decompiles a .nes ROM file into a C# project that can be");
    Console.WriteLine("transpiled through dotnes to produce an equivalent ROM.");
    return;
}

string inputPath = args[0];
string outputDir = args.Length > 1 ? args[1] : Path.GetFileNameWithoutExtension(inputPath);

if (!File.Exists(inputPath))
{
    logger.WriteError($"The input file '{inputPath}' does not exist.");
    return;
}

logger.WriteStatus("Running decompiler.");
logger.WriteStatus($"Input: {inputPath}");
logger.WriteStatus($"Output: {outputDir}");

// Read the ROM
using var input = File.OpenRead(inputPath);
var rom = new NESRomReader(input);

logger.WriteStatus($"PRG banks: {rom.PrgBanks}, CHR banks: {rom.ChrBanks}, Mapper: {rom.Mapper}");
logger.WriteStatus($"Mirroring: {(rom.VerticalMirroring ? "Vertical" : "Horizontal")}");
logger.WriteStatus($"Vectors: NMI=${rom.NmiVector:X4} RESET=${rom.ResetVector:X4} IRQ=${rom.IrqVector:X4}");

// Decompile
var decompiler = new Decompiler(rom, logger);
string csharpCode = decompiler.Decompile();

// Create output directory
Directory.CreateDirectory(outputDir);

// Write C# source
string projectName = Path.GetFileNameWithoutExtension(inputPath);
string csPath = Path.Combine(outputDir, "Program.cs");
File.WriteAllText(csPath, csharpCode);
logger.WriteStatus($"Wrote {csPath}");

// Write .csproj
string csprojPath = Path.Combine(outputDir, $"{projectName}.csproj");
File.WriteAllText(csprojPath, decompiler.GenerateCsproj(projectName));
logger.WriteStatus($"Wrote {csprojPath}");

// Write CHR ROM as .s file(s)
if (rom.ChrBanks > 1)
{
    // Multi-bank: output numbered files (e.g., chr_generic_0.s, chr_generic_1.s)
    for (int bank = 0; bank < rom.ChrBanks; bank++)
    {
        string chrPath = Path.Combine(outputDir, $"chr_generic_{bank}.s");
        File.WriteAllText(chrPath, rom.GenerateChrAssembly(bank));
        logger.WriteStatus($"Wrote {chrPath}");
    }
}
else if (rom.ChrBanks == 1)
{
    string chrPath = Path.Combine(outputDir, "chr_generic.s");
    File.WriteAllText(chrPath, rom.GenerateChrAssembly());
    logger.WriteStatus($"Wrote {chrPath}");
}
else
{
    // CHR RAM (ChrBanks == 0): extract tile data from vram_write() calls
    var chrRamData = decompiler.GetChrRamTileData();
    if (chrRamData.Count > 0)
    {
        string chrPath = Path.Combine(outputDir, "chr_generic.s");
        File.WriteAllText(chrPath, GenerateChrRamAssembly(chrRamData));
        logger.WriteStatus($"Wrote {chrPath} (extracted from CHR RAM uploads)");
    }
    else
    {
        logger.WriteStatus("Note: CHR RAM ROM with no detected tile uploads. Tile data must be provided separately.");
    }
}

logger.WriteStatus("Decompilation complete.");

/// <summary>
/// Merge CHR RAM tile uploads into a single .s assembly file.
/// Uploads to $0000 go into the first 4KB, uploads to $1000 into the second 4KB.
/// </summary>
static string GenerateChrRamAssembly(IReadOnlyList<(ushort PpuAddress, byte[] Data)> uploads)
{
    // Build a combined 8KB CHR buffer
    const int chrSize = 8192;
    var chr = new byte[chrSize];

    foreach (var (ppuAddr, data) in uploads)
    {
        int destOffset = ppuAddr;
        if (destOffset >= chrSize)
            continue;
        int copyLen = Math.Min(data.Length, chrSize - destOffset);
        if (copyLen > 0)
            Array.Copy(data, 0, chr, destOffset, copyLen);
    }

    return NESRomReader.GenerateChrAssemblyFromData(chr, 0, chrSize);
}
