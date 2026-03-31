using SPLog;

namespace SPLog.StressRunner;

internal static class SPLogOptionsExtensions
{
    public static SPLogOptions CloneForStressRun(this SPLogOptions options, string runDirectory)
    {
        var resolvedFilePath = options.EnableFile
            ? ResolveStressFilePath(options.FilePath, runDirectory)
            : options.FilePath;

        var clone = new SPLogOptions
        {
            Name = options.Name,
            MinimumLevel = options.MinimumLevel,
            UseUtcTimestamp = options.UseUtcTimestamp,
            TimestampFormat = options.TimestampFormat,
            IncludeThreadId = options.IncludeThreadId,
            IncludeLoggerName = options.IncludeLoggerName,
            IncludeSequenceNumber = options.IncludeSequenceNumber,
            EnableConsole = options.EnableConsole,
            EnableFile = options.EnableFile,
            FilePath = resolvedFilePath,
            FileConflictMode = options.FileConflictMode,
            FileRollingMode = options.FileRollingMode,
            MaxFileSizeBytes = options.MaxFileSizeBytes,
            MaxRollingFiles = options.MaxRollingFiles,
            QueueCapacity = options.QueueCapacity,
            BatchSize = options.BatchSize,
            FlushIntervalMs = options.FlushIntervalMs,
            FileBufferSize = options.FileBufferSize
        };

        clone.Validate();
        return clone;
    }

    public static string GetResolvedBaseFilePath(this SPLogOptions options, string runDirectory)
    {
        var resolvedPath = ResolveStressFilePath(options.FilePath, runDirectory);

        if (LooksLikeDirectoryPath(options.FilePath, resolvedPath))
        {
            return Path.Combine(resolvedPath, $"{SanitizeFileName(options.Name)}.log");
        }

        return resolvedPath;
    }

    private static string ResolveStressFilePath(string filePath, string runDirectory)
    {
        return Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(filePath, runDirectory);
    }

    private static bool LooksLikeDirectoryPath(string originalPath, string resolvedPath)
    {
        if (originalPath.EndsWith(Path.DirectorySeparatorChar) || originalPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return true;
        }

        if (Directory.Exists(resolvedPath))
        {
            return true;
        }

        return string.IsNullOrEmpty(Path.GetExtension(resolvedPath));
    }

    private static string SanitizeFileName(string loggerName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(loggerName.Length);

        for (var i = 0; i < loggerName.Length; i++)
        {
            var ch = loggerName[i];
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? "SPLog" : builder.ToString();
    }
}
