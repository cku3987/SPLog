namespace SPLog;

internal sealed class CompositeLogSink : ILogSink
{
    private readonly ILogSink[] _sinks;

    public CompositeLogSink(ILogSink[] sinks)
    {
        _sinks = sinks;
    }

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _sinks.Length; i++)
        {
            await _sinks[i].WriteBatchAsync(entries, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _sinks.Length; i++)
        {
            _sinks[i].Dispose();
        }
    }
}
