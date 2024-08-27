using dotnes;

var logger = new ConsoleLogger();

string targetPath = "";
string assemblyFile = "";
string outputPath = "";

if (args.Length < 2
    || string.IsNullOrWhiteSpace(args[0])
    || string.IsNullOrWhiteSpace(args[1]))
{
    Console.WriteLine("Usage: dotnes.exe mydll.dll chr_generic.s");
    return;
}

targetPath = args[0];
assemblyFile = args[1];

if (!File.Exists(targetPath))
{
    logger.WriteError($"The target path '{targetPath}' does not exist.");
    return;
}

if (!File.Exists(assemblyFile))
{
    logger.WriteError($"The assembly file '{assemblyFile}' does not exist.");
    return;
}

outputPath = Path.ChangeExtension(targetPath, "nes");

logger.WriteStatus("Running compiler.");
logger.WriteStatus($"Target Path: {targetPath}");
logger.WriteStatus($"Assembly File: {assemblyFile}");
logger.WriteStatus($"Output Path: {outputPath}");

var transpiler = new TranspileToNES
{
    DiagnosticLogging = true,
    AssemblyFiles = [assemblyFile],
    TargetPath = targetPath,
    OutputPath = outputPath,
    Logger = logger
};
transpiler.Execute();