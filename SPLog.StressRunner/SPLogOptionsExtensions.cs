using SPLog;

namespace SPLog.StressRunner;

internal static class SPLogOptionsExtensions
{
    public static SPLogOptions CloneForStressRun(this SPLogOptions options, string logDirectory)
    {
        var clone = new SPLogOptions
        {
            Name = options.Name,
            MinimumLevel = options.MinimumLevel,
            UseUtcTimestamp = options.UseUtcTimestamp,
            IncludeThreadId = options.IncludeThreadId,
            IncludeLoggerName = options.IncludeLoggerName,
            EnableConsole = options.EnableConsole,
            EnableFile = options.EnableFile,
            FilePath = options.EnableFile ? logDirectory : options.FilePath,
            FileConflictMode = options.FileConflictMode,
            FileRollingMode = options.FileRollingMode,
            MaxFileSizeBytes = options.MaxFileSizeBytes,
            MaxRollingFiles = options.MaxRollingFiles,
            QueueCapacity = options.QueueCapacity,
            BatchSize = options.BatchSize,
            FlushIntervalMs = options.FlushIntervalMs,
            FileBufferSize = options.FileBufferSize,
            BlockWhenQueueFull = options.BlockWhenQueueFull
        };

        clone.Validate();
        return clone;
    }
}
