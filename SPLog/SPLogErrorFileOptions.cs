namespace SPLog;

public sealed class SPLogErrorFileOptions
{
    public string FilePath { get; set; } = "errors/error.log";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    public bool UseUtcTimestamp { get; set; } = false;

    public FileConflictMode FileConflictMode { get; set; } = FileConflictMode.Append;

    public FileRollingMode FileRollingMode { get; set; } = FileRollingMode.Daily;

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxRollingFiles { get; set; } = 14;

    public int FileBufferSize { get; set; } = 65536;

    internal void Normalize()
    {
        FilePath = string.IsNullOrWhiteSpace(FilePath) ? "errors/error.log" : FilePath.Trim();
    }

    internal void CopyFrom(SPLogErrorFileOptions source)
    {
        FilePath = source.FilePath;
        MinimumLevel = source.MinimumLevel;
        UseUtcTimestamp = source.UseUtcTimestamp;
        FileConflictMode = source.FileConflictMode;
        FileRollingMode = source.FileRollingMode;
        MaxFileSizeBytes = source.MaxFileSizeBytes;
        MaxRollingFiles = source.MaxRollingFiles;
        FileBufferSize = source.FileBufferSize;
    }

    internal SPLogErrorFileOptions Clone()
    {
        return new SPLogErrorFileOptions
        {
            FilePath = FilePath,
            MinimumLevel = MinimumLevel,
            UseUtcTimestamp = UseUtcTimestamp,
            FileConflictMode = FileConflictMode,
            FileRollingMode = FileRollingMode,
            MaxFileSizeBytes = MaxFileSizeBytes,
            MaxRollingFiles = MaxRollingFiles,
            FileBufferSize = FileBufferSize
        };
    }

    public void Validate()
    {
        Normalize();

        if (string.IsNullOrWhiteSpace(FilePath))
        {
            throw new ArgumentException("Error file path is required.", nameof(FilePath));
        }

        if (MinimumLevel < LogLevel.Trace || MinimumLevel > LogLevel.Critical)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumLevel));
        }

        if (MaxFileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFileSizeBytes));
        }

        if (MaxRollingFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRollingFiles));
        }

        if (FileBufferSize < 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(FileBufferSize));
        }
    }
}
