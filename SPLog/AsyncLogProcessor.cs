using System.Threading.Channels;

namespace SPLog;

internal sealed class AsyncLogProcessor : IDisposable
{
    private readonly SPLogOptions _options;
    private readonly ILogSink _sink;
    private readonly Channel<LogEntry> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private int _disposed;
    private long _lastWrittenTimestampTicks;
    private long _nextSequenceNumber;

    public AsyncLogProcessor(SPLogOptions options, ILogSink sink)
    {
        _options = options;
        _sink = sink;

        var channelOptions = new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };

        _channel = Channel.CreateBounded<LogEntry>(channelOptions);
        _worker = Task.Run(ProcessAsync);
    }

    public void Enqueue(LogEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            return;
        }

        _channel.Writer.WriteAsync(entry).GetAwaiter().GetResult();
    }

    public int DrainDroppedCount()
    {
        return 0;
    }

    private async Task ProcessAsync()
    {
        var buffer = new LogEntry[_options.BatchSize];
        var token = _shutdown.Token;

        try
        {
            while (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                var count = 0;

                while (count < buffer.Length && _channel.Reader.TryRead(out var entry))
                {
                    buffer[count++] = entry;
                }

                if (count == 0)
                {
                    continue;
                }

                AssignSequenceNumbersInPlace(buffer.AsSpan(0, count));
                MonotonicTimestampNormalizer.NormalizeInPlace(buffer.AsSpan(0, count), ref _lastWrittenTimestampTicks);
                await _sink.WriteBatchAsync(buffer.AsMemory(0, count), CancellationToken.None).ConfigureAwait(false);

                try
                {
                    await Task.Delay(_options.FlushIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await DrainRemainingAsync().ConfigureAwait(false);
        }
    }

    private async Task DrainRemainingAsync()
    {
        var entries = new List<LogEntry>(_options.BatchSize);

        while (_channel.Reader.TryRead(out var entry))
        {
            entries.Add(entry);
            if (entries.Count >= _options.BatchSize)
            {
                var batch = entries.ToArray();
                AssignSequenceNumbersInPlace(batch);
                MonotonicTimestampNormalizer.NormalizeInPlace(batch, ref _lastWrittenTimestampTicks);
                await _sink.WriteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
                entries.Clear();
            }
        }

        if (entries.Count > 0)
        {
            var batch = entries.ToArray();
            AssignSequenceNumbersInPlace(batch);
            MonotonicTimestampNormalizer.NormalizeInPlace(batch, ref _lastWrittenTimestampTicks);
            await _sink.WriteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void AssignSequenceNumbersInPlace(Span<LogEntry> entries)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = entries[i] with
            {
                SequenceNumber = Interlocked.Increment(ref _nextSequenceNumber)
            };
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        _worker.GetAwaiter().GetResult();
        _sink.Dispose();
        _shutdown.Dispose();
    }
}
