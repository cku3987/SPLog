namespace SPLog;

public sealed class SPLogOptions
{
    public string Name { get; set; } = "SPLog";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool UseUtcTimestamp { get; set; } = false;

    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

    public bool IncludeThreadId { get; set; } = true;

    public bool IncludeLoggerName { get; set; } = true;

    public bool EnableConsole { get; set; } = true;

    public bool IncludeSequenceNumber { get; set; } = false;

    public bool EnableFile { get; set; } = false;

    public string FilePath { get; set; } = "logs";

    public FileConflictMode FileConflictMode { get; set; } = FileConflictMode.Append;

    public FileRollingMode FileRollingMode { get; set; } = FileRollingMode.Daily;

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxRollingFiles { get; set; } = 14;

    public int QueueCapacity { get; set; } = 8192;

    public int BatchSize { get; set; } = 10;

    public int FlushIntervalMs { get; set; } = 100;

    public int FileBufferSize { get; set; } = 65536;

    internal void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "SPLog" : Name.Trim();
        FilePath = string.IsNullOrWhiteSpace(FilePath) ? "logs" : FilePath.Trim();
    }

    internal void CopyFrom(SPLogOptions source)
    {
        Name = source.Name;
        MinimumLevel = source.MinimumLevel;
        UseUtcTimestamp = source.UseUtcTimestamp;
        TimestampFormat = source.TimestampFormat;
        IncludeThreadId = source.IncludeThreadId;
        IncludeLoggerName = source.IncludeLoggerName;
        IncludeSequenceNumber = source.IncludeSequenceNumber;
        EnableConsole = source.EnableConsole;
        EnableFile = source.EnableFile;
        FilePath = source.FilePath;
        FileConflictMode = source.FileConflictMode;
        FileRollingMode = source.FileRollingMode;
        MaxFileSizeBytes = source.MaxFileSizeBytes;
        MaxRollingFiles = source.MaxRollingFiles;
        QueueCapacity = source.QueueCapacity;
        BatchSize = source.BatchSize;
        FlushIntervalMs = source.FlushIntervalMs;
        FileBufferSize = source.FileBufferSize;
    }

    internal SPLogOptions Clone()
    {
        return new SPLogOptions
        {
            Name = Name,
            MinimumLevel = MinimumLevel,
            UseUtcTimestamp = UseUtcTimestamp,
            TimestampFormat = TimestampFormat,
            IncludeThreadId = IncludeThreadId,
            IncludeLoggerName = IncludeLoggerName,
            IncludeSequenceNumber = IncludeSequenceNumber,
            EnableConsole = EnableConsole,
            EnableFile = EnableFile,
            FilePath = FilePath,
            FileConflictMode = FileConflictMode,
            FileRollingMode = FileRollingMode,
            MaxFileSizeBytes = MaxFileSizeBytes,
            MaxRollingFiles = MaxRollingFiles,
            QueueCapacity = QueueCapacity,
            BatchSize = BatchSize,
            FlushIntervalMs = FlushIntervalMs,
            FileBufferSize = FileBufferSize
        };
    }

    public void Validate()
    {
        Normalize();

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Logger name is required.", nameof(Name));
        }

        if (QueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity));
        }

        if (BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        }

        if (FlushIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FlushIntervalMs));
        }

        if (FileBufferSize < 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(FileBufferSize));
        }

        if (string.IsNullOrWhiteSpace(TimestampFormat))
        {
            throw new ArgumentException("TimestampFormat is required.", nameof(TimestampFormat));
        }

        try
        {
            _ = DateTime.Now.ToString(TimestampFormat);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("TimestampFormat must be a valid .NET DateTime format string.", nameof(TimestampFormat), ex);
        }

        if (MaxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFileSizeBytes));
        }

        if (MaxRollingFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRollingFiles));
        }

        if (EnableFile && string.IsNullOrWhiteSpace(FilePath))
        {
            throw new ArgumentException("FilePath is required when file logging is enabled.", nameof(FilePath));
        }
    }
}
