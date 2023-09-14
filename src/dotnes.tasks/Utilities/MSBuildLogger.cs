namespace dotnes;

class MSBuildLogger : ILogger
{
    readonly TaskLoggingHelper _logger;

    public MSBuildLogger(TaskLoggingHelper logger) => _logger = logger;

    public void WriteLine(IFormattable message) => _logger.LogMessage(message.ToString());
}
