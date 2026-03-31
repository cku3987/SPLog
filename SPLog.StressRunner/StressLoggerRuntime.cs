using SPLog;

namespace SPLog.StressRunner;

internal sealed class StressLoggerRuntime : IDisposable
{
    private long _producedMessages;
    private long _droppedMessages;
    private readonly SPLogger _disposeTarget;

    public StressLoggerRuntime(
        StressLoggerScenarioConfiguration scenario,
        SPLogOptions logOptions,
        string resolvedBaseFilePath,
        SPLogger logger,
        SPLogger disposeTarget)
    {
        Scenario = scenario;
        LogOptions = logOptions;
        ResolvedBaseFilePath = resolvedBaseFilePath;
        Logger = logger;
        _disposeTarget = disposeTarget;
    }

    public StressLoggerScenarioConfiguration Scenario { get; }

    public SPLogOptions LogOptions { get; }

    public string ResolvedBaseFilePath { get; }

    public SPLogger Logger { get; }

    public long ProducedMessages => Interlocked.Read(ref _producedMessages);

    public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

    public long NextSequence()
    {
        return Interlocked.Increment(ref _producedMessages);
    }

    public void DrainDroppedMessages()
    {
        var drained = Logger.DrainDroppedCount();
        if (drained > 0)
        {
            Interlocked.Add(ref _droppedMessages, drained);
        }
    }

    public void Dispose()
    {
        _disposeTarget.Dispose();
    }
}
