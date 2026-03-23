namespace SPLog;

public readonly record struct LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string LoggerName,
    int ThreadId,
    string Message,
    string? ExceptionText = null);
