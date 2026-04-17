using System.Runtime.CompilerServices;

namespace SPLog;

public sealed class SPLogger : IDisposable
{
    private readonly SPLogOptions _options;
    private readonly AsyncLogProcessor _processor;
    private readonly string _loggerName;
    private readonly bool _ownsProcessor;

    internal SPLogger(SPLogOptions options, AsyncLogProcessor processor)
        : this(options, processor, options.Name, ownsProcessor: true)
    {
    }

    private SPLogger(SPLogOptions options, AsyncLogProcessor processor, string loggerName, bool ownsProcessor)
    {
        _options = options;
        _processor = processor;
        _loggerName = loggerName;
        _ownsProcessor = ownsProcessor;
    }

    public string Name => _loggerName;

    public bool IsEnabled(LogLevel level) => level >= _options.MinimumLevel && level != LogLevel.None;

    public SPLogger CreateCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            throw new ArgumentException("Category name is required.", nameof(categoryName));
        }

        var normalizedCategoryName = categoryName.Trim();
        var childLoggerName = string.IsNullOrWhiteSpace(_loggerName)
            ? normalizedCategoryName
            : $"{_loggerName}.{normalizedCategoryName}";

        return new SPLogger(_options, _processor, childLoggerName, ownsProcessor: false);
    }

    public void Log(LogLevel level, string message)
    {
        Log(level, message, null);
    }

    public void Log(LogLevel level, string message, Exception? exception)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        _processor.Enqueue(new LogEntry(
            Timestamp: _options.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now,
            Level: level,
            LoggerName: _loggerName,
            ThreadId: Environment.CurrentManagedThreadId,
            Message: message,
            ExceptionText: exception is null ? null : ExceptionDetails.Build(exception)));
    }

#if NET8_0_OR_GREATER
    public void Log(
        LogLevel level,
        [InterpolatedStringHandlerArgument("", "level")] ref SPLogInterpolatedStringHandler handler)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        Log(level, handler.GetFormattedText());
    }
#endif

    public void Trace(string message) => Log(LogLevel.Trace, message);

    public void Debug(string message) => Log(LogLevel.Debug, message);

    public void Information(string message) => Log(LogLevel.Information, message);

    public void Warning(string message) => Log(LogLevel.Warning, message);

    public void Error(string message) => Log(LogLevel.Error, message);

    public void Critical(string message) => Log(LogLevel.Critical, message);

    public void Warning(Exception exception, string message) => Log(LogLevel.Warning, message, exception);

    public void Error(Exception exception, string message) => Log(LogLevel.Error, message, exception);

    public void Critical(Exception exception, string message) => Log(LogLevel.Critical, message, exception);

    public int DrainDroppedCount() => _processor.DrainDroppedCount();

    public void Dispose()
    {
        if (_ownsProcessor)
        {
            _processor.Dispose();
        }
    }
}
