namespace dotnes;

class NullLogger : ILogger
{
    public void WriteLine(IFormattable message) { }
}
