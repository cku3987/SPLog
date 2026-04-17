namespace SPLog;

internal sealed class ConsoleLogSink : ILogSink
{
    private readonly SPLogOptions _options;

    public ConsoleLogSink(SPLogOptions options)
    {
        _options = options;
    }

    public ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        var span = entries.Span;
        for (var i = 0; i < span.Length; i++)
        {
            var entry = span[i];
            var line = SPLogFormatter.Format(entry, _options);
            Console.WriteLine(line);
        }

        return default;
    }

    public void Dispose()
    {
    }
}
