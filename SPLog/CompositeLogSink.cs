using System.Runtime.ExceptionServices;

namespace SPLog;

internal sealed class CompositeLogSink : ILogSink
{
    private readonly ILogSink[] _sinks;
    private int _disposed;

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DisposeAll(_sinks);
    }

    internal static void DisposeAll(IEnumerable<ILogSink> sinks)
    {
        List<Exception>? failures = null;
        foreach (var sink in sinks)
        {
            try
            {
                sink.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        if (failures?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures?.Count > 1)
        {
            throw new AggregateException("Multiple logging sinks failed during cleanup.", failures);
        }
    }
}
