using SPLog;

namespace SPLog.Tests;

internal sealed class BlockingLogSink : ILogSink
{
    private readonly TaskCompletionSource<bool> _firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writtenEntries;

    public Task FirstWriteStarted => _firstWriteStarted.Task;

    public int WrittenEntries => Volatile.Read(ref _writtenEntries);

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        Interlocked.Add(ref _writtenEntries, entries.Length);

        if (_firstWriteStarted.TrySetResult(true))
        {
            await _releaseFirstWrite.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    public void ReleaseFirstWrite()
    {
        _releaseFirstWrite.TrySetResult(true);
    }

    public void Dispose()
    {
        ReleaseFirstWrite();
    }
}
