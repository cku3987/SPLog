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

        WaitForCompletion(_channel.Writer.WriteAsync(entry));
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
            // Dispose completes the writer to wake an idle reader. Only the
            // interval delay uses cancellation; sink cancellation is a failure.
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
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
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }

            // Drain only on normal shutdown. Retrying after a sink failure could
            // duplicate a partially written batch or hide the original error.
            await DrainRemainingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Wake blocked producers and reject future writes before exiting.
            _channel.Writer.TryComplete(ex);
            throw;
        }
        finally
        {
            _channel.Writer.TryComplete();
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

    private static void WaitForCompletion(ValueTask operation)
    {
        if (operation.IsCompleted)
        {
            operation.GetAwaiter().GetResult();
            return;
        }

        operation.AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        finally
        {
            try
            {
                _sink.Dispose();
            }
            finally
            {
                _shutdown.Dispose();
            }
        }
    }
}
