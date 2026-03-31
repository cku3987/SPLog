using SPLog;

namespace SPLog.StressRunner;

public sealed class StressRunnerConfiguration
{
    public string RunName { get; set; } = "LongRun";

    public string OutputRoot { get; set; } = "artifacts/stress";

    public string LogSubdirectory { get; set; } = "logs";

    public TimeSpan Duration { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan StatusInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int ProducerCount { get; set; } = Math.Max(2, Environment.ProcessorCount);

    public int MessagesPerBurst { get; set; } = 100;

    public int PausePerBurstMs { get; set; }

    public int MessagePayloadLength { get; set; } = 128;

    public bool ValidateLineCountAfterRun { get; set; } = true;

    public bool FailIfDroppedMessages { get; set; } = true;

    public bool CountOnlyMessagePrefixMatches { get; set; } = true;

    public string MessagePrefix { get; set; } = "LONGRUN|";

    public SPLogOptions LogOptions { get; set; } = new()
    {
        Name = "LongRun",
        MinimumLevel = LogLevel.Information,
        EnableConsole = false,
        EnableFile = true,
        FilePath = "logs",
        FileConflictMode = FileConflictMode.Append,
        FileRollingMode = FileRollingMode.Hourly,
        MaxFileSizeBytes = 50 * 1024 * 1024,
        MaxRollingFiles = 500,
        QueueCapacity = 8192,
        BatchSize = 10,
        FlushIntervalMs = 100,
        FileBufferSize = 65536,
        BlockWhenQueueFull = true
    };
}
