using SPLog;

namespace SPLog.Tests;

internal sealed class ThrowingLogSink : ILogSink
{
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _failureCall;
    private int _writeCalls;
    private int _disposeCalls;

    public ThrowingLogSink(Exception failure, int failureCall = 1, Exception? disposeFailure = null)
    {
        Failure = failure;
        DisposeFailure = disposeFailure;
        _failureCall = failureCall;
    }

    public Exception Failure { get; }
    public Exception? DisposeFailure { get; }
    public Task FirstWriteStarted => _started.Task;
    public int WriteCalls => Volatile.Read(ref _writeCalls);
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _writeCalls);
        if (call == 1)
        {
            _started.TrySetResult(true);
            await _release.Task.ConfigureAwait(false);
        }

        if (call >= _failureCall)
        {
            throw Failure;
        }
    }

    public void Release() => _release.TrySetResult(true);
    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCalls);
        if (DisposeFailure is not null)
        {
            throw DisposeFailure;
        }
    }
}
