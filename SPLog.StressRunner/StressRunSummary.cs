namespace SPLog.StressRunner;

public sealed class StressRunSummary
{
    public string RunDirectory { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset EndedAt { get; set; }

    public TimeSpan PlannedDuration { get; set; }

    public TimeSpan ActualDuration { get; set; }

    public int ProducerCount { get; set; }

    public long ProducedMessages { get; set; }

    public long DroppedMessages { get; set; }

    public long ValidatedFileMessageLines { get; set; }

    public int LogFileCount { get; set; }

    public long TotalLogBytes { get; set; }

    public long PeakWorkingSetBytes { get; set; }

    public bool LineCountValidationPassed { get; set; }

    public bool DroppedMessageValidationPassed { get; set; }
}
