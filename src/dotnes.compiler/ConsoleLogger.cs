using dotnes;

class ConsoleLogger : ILogger
{
    public void WriteLine(IFormattable message)
    {
        Console.WriteLine(message);
    }

    public void WriteStatus(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }
}