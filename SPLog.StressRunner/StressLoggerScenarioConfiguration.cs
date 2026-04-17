using SPLog;

namespace SPLog.StressRunner;

public sealed class StressLoggerScenarioConfiguration
{
    public string ScenarioName { get; set; } = "Primary";

    public string? SharedLoggerKey { get; set; }

    public string? CategoryPath { get; set; }

    public int ProducerCount { get; set; } = Math.Max(2, Environment.ProcessorCount);

    public int MessagesPerBurst { get; set; } = 100;

    public int PausePerBurstMs { get; set; }

    public int MessagePayloadLength { get; set; } = 128;

    public bool ValidateLineCountAfterRun { get; set; } = true;

    public bool FailIfDroppedMessages { get; set; } = true;

    public bool CountOnlyMessagePrefixMatches { get; set; } = true;

    public string MessagePrefix { get; set; } = "LONGRUN|PRIMARY|";

    public SPLogOptions LogOptions { get; set; } = new()
    {
        Name = "Primary",
        MinimumLevel = LogLevel.Information,
        EnableConsole = false,
        EnableFile = true,
        FilePath = "logs",
        FileConflictMode = FileConflictMode.Append,
        FileRollingMode = FileRollingMode.Hourly,
        MaxFileSizeBytes = 10 * 1024 * 1024,
        MaxRollingFiles = 100,
        QueueCapacity = 8192,
        BatchSize = 10,
        FlushIntervalMs = 100,
        FileBufferSize = 65536
    };
}
