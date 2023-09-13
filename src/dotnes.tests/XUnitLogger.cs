using System.Diagnostics;
using Xunit.Abstractions;

namespace dotnes.tests;

class XUnitLogger : ILogger
{
    readonly ITestOutputHelper _output;

    public XUnitLogger(ITestOutputHelper output) => _output = output;

    public void WriteLine(string message)
    {
        Debug.WriteLine(message);
        _output.WriteLine(message);
    }
}
