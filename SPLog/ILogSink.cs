namespace SPLog;

public interface ILogSink : IDisposable
{
    ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken);
}
