using Microsoft.Extensions.Logging;

namespace TimeMachine;

/*public partial class HazelSerilogLogger(ILogger logger) : Hazel.ILogger
{
    public void WriteVerbose(string msg)
        => LogDebug(logger, msg);

    public void WriteError(string msg)
        => LogError(logger, msg);

    public void WriteWarning(string msg)
        => LogWarning(logger, msg);

    public void WriteInfo(string msg)
        => LogMessage(logger, msg);

    [LoggerMessage(LogLevel.Debug, "{Message}")]
    static partial void LogDebug(ILogger logger, string message);

    [LoggerMessage(LogLevel.Error, "{Error}")]
    static partial void LogError(ILogger logger, string error);

    [LoggerMessage(LogLevel.Warning, "{Warning}")]
    static partial void LogWarning(ILogger logger, string warning);

    [LoggerMessage(LogLevel.Information, "{Message}")]
    static partial void LogMessage(ILogger logger, string Message);
}*/
