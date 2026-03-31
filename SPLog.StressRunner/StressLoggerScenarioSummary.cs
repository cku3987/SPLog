namespace SPLog.StressRunner;

public sealed class StressLoggerScenarioSummary
{
    public string ScenarioName { get; set; } = string.Empty;

    public string? SharedLoggerKey { get; set; }

    public string? CategoryPath { get; set; }

    public string LoggerName { get; set; } = string.Empty;

    public string ResolvedBaseFilePath { get; set; } = string.Empty;

    public int ProducerCount { get; set; }

    public long ProducedMessages { get; set; }

    public long DroppedMessages { get; set; }

    public long ValidatedFileMessageLines { get; set; }

    public int LogFileCount { get; set; }

    public long TotalLogBytes { get; set; }

    public bool LineCountValidationPassed { get; set; }

    public bool DroppedMessageValidationPassed { get; set; }
}
