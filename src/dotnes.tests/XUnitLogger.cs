using Xunit.Abstractions;

namespace dotnes.tests;

class XUnitLogger : ILogger
{
    readonly ITestOutputHelper _output;

    public XUnitLogger(ITestOutputHelper output) => _output = output;

    public void WriteLine(string message) => _output.WriteLine(message);
}
