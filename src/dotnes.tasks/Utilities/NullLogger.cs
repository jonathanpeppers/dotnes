namespace dotnes;

class NullLogger : ILogger
{
    public void WriteLine(string message) { }
}
