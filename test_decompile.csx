using dotnes;
var rom = new NESRomReader(File.ReadAllBytes(args[0]));
var decompiler = new Decompiler(rom, new ConsoleLogger());
Console.Write(decompiler.Decompile());
class ConsoleLogger : ILogger { public void WriteLine(string message) {} }
