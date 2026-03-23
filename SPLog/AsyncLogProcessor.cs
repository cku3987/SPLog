using System.Threading.Channels;

namespace SPLog;

internal sealed class AsyncLogProcessor : IDisposable
{
    private readonly SPLogOptions _options;
    private readonly ILogSink _sink;
    private readonly Channel<LogEntry> _channel;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private int _droppedMessages;

    public AsyncLogProcessor(SPLogOptions options, ILogSink sink)
    {
        _options = options;
        _sink = sink;

        var channelOptions = new BoundedChannelOptions(options.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.BlockWhenQueueFull ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropWrite
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

        if (_options.BlockWhenQueueFull)
        {
            _channel.Writer.WriteAsync(entry).AsTask().GetAwaiter().GetResult();
            return;
        }

        Interlocked.Increment(ref _droppedMessages);
    }

    public int DrainDroppedCount()
    {
        return Interlocked.Exchange(ref _droppedMessages, 0);
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

                await _sink.WriteBatchAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);

                await Task.Delay(_options.FlushIntervalMs, token).ConfigureAwait(false);
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
                await _sink.WriteBatchAsync(entries.ToArray(), CancellationToken.None).ConfigureAwait(false);
                entries.Clear();
            }
        }

        if (entries.Count > 0)
        {
            await _sink.WriteBatchAsync(entries.ToArray(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        _worker.GetAwaiter().GetResult();
        _sink.Dispose();
        _shutdown.Dispose();
    }
}
