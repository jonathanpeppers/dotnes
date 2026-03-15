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

// Write CHR ROM as .s file (if present)
if (rom.ChrBanks > 0)
{
    string chrPath = Path.Combine(outputDir, "chr_generic.s");
    File.WriteAllText(chrPath, rom.GenerateChrAssembly());
    logger.WriteStatus($"Wrote {chrPath}");
}

logger.WriteStatus("Decompilation complete.");
