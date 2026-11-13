using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>只負責格式化與非阻塞入列，不在呼叫執行緒存取磁碟。</summary>
public sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message}{Environment.NewLine}{exception}";
        }

        provider.Enqueue(categoryName, logLevel, message);
    }
}
