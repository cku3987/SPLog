namespace SPLog;

public sealed class SPLogOptions
{
    public string Name { get; set; } = "SPLog";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool UseUtcTimestamp { get; set; } = false;

    public bool IncludeThreadId { get; set; } = true;

    public bool IncludeLoggerName { get; set; } = true;

    public bool EnableConsole { get; set; } = true;

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

    public bool BlockWhenQueueFull { get; set; } = true;

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
        IncludeThreadId = source.IncludeThreadId;
        IncludeLoggerName = source.IncludeLoggerName;
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
        BlockWhenQueueFull = source.BlockWhenQueueFull;
    }

    internal SPLogOptions Clone()
    {
        return new SPLogOptions
        {
            Name = Name,
            MinimumLevel = MinimumLevel,
            UseUtcTimestamp = UseUtcTimestamp,
            IncludeThreadId = IncludeThreadId,
            IncludeLoggerName = IncludeLoggerName,
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
            FileBufferSize = FileBufferSize,
            BlockWhenQueueFull = BlockWhenQueueFull
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
