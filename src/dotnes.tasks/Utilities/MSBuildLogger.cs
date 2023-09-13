namespace dotnes;

class MSBuildLogger : ILogger
{
    readonly TaskLoggingHelper _logger;

    public MSBuildLogger(TaskLoggingHelper logger) => _logger = logger;

    public void WriteLine(string message) => _logger.LogMessage(message);
}
