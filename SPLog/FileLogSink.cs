using System.Text;

namespace SPLog;

internal sealed class FileLogSink : ILogSink
{
    private readonly SPLogOptions _options;
    private readonly string _baseDirectory;
    private readonly string _fileNameWithoutExtension;
    private readonly string _fileExtension;
    private StreamWriter _writer;
    private FileStream _stream;
    private string _currentPeriodKey;
    private int _currentSequence;

    public FileLogSink(SPLogOptions options)
    {
        _options = options;
        var fullPath = ResolveLogPath(options.FilePath, options.Name);
        _baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        _fileExtension = Path.GetExtension(fullPath);
        Directory.CreateDirectory(_baseDirectory);

        _currentPeriodKey = GetPeriodKey(GetNow());
        _currentSequence = GetInitialSequence(_currentPeriodKey);
        (_stream, _writer) = OpenWriter(_currentPeriodKey, _currentSequence);
        CleanupOldFiles();
    }

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        var batch = entries.ToArray();
        for (var i = 0; i < batch.Length; i++)
        {
            RotateIfNeeded();
            var line = SPLogFormatter.Format(batch[i], _options);
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }

    private void RotateIfNeeded()
    {
        var now = GetNow();
        var periodKey = GetPeriodKey(now);

        if (periodKey != _currentPeriodKey)
        {
            SwitchWriter(periodKey, 0);
            CleanupOldFiles();
            return;
        }

        if (_stream.Length < _options.MaxFileSizeBytes)
        {
            return;
        }

        SwitchWriter(_currentPeriodKey, _currentSequence + 1);
        CleanupOldFiles();
    }

    private void SwitchWriter(string periodKey, int sequence)
    {
        _writer.Dispose();
        _stream.Dispose();
        _currentPeriodKey = periodKey;
        _currentSequence = sequence;
        (_stream, _writer) = OpenWriter(periodKey, sequence);
    }

    private (FileStream Stream, StreamWriter Writer) OpenWriter(string periodKey, int sequence)
    {
        var path = BuildFilePath(periodKey, sequence);
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            _options.FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var writer = new StreamWriter(stream, Encoding.UTF8, _options.FileBufferSize)
        {
            AutoFlush = false
        };

        return (stream, writer);
    }

    private string BuildFilePath(string periodKey, int sequence)
    {
        var suffix = _options.FileRollingMode switch
        {
            FileRollingMode.Daily => $"_{periodKey}",
            FileRollingMode.Hourly => $"_{periodKey}",
            _ => string.Empty
        };

        var sequenceSuffix = sequence > 0 ? $"_{sequence:D3}" : string.Empty;
        var fileName = $"{_fileNameWithoutExtension}{suffix}{sequenceSuffix}{_fileExtension}";
        return Path.Combine(_baseDirectory, fileName);
    }

    private int DetectLastSequence(string periodKey)
    {
        var pattern = _options.FileRollingMode switch
        {
            FileRollingMode.Daily => $"{_fileNameWithoutExtension}_{periodKey}*{_fileExtension}",
            FileRollingMode.Hourly => $"{_fileNameWithoutExtension}_{periodKey}*{_fileExtension}",
            _ => $"{_fileNameWithoutExtension}*{_fileExtension}"
        };

        var files = Directory.GetFiles(_baseDirectory, pattern);
        var maxSequence = -1;
        for (var i = 0; i < files.Length; i++)
        {
            var name = Path.GetFileNameWithoutExtension(files[i]);
            var expectedBaseName = BuildExpectedBaseName(periodKey);

            if (!name.StartsWith(expectedBaseName, StringComparison.Ordinal))
            {
                continue;
            }

            if (name.Length == expectedBaseName.Length)
            {
                maxSequence = Math.Max(maxSequence, 0);
                continue;
            }

            if (name.Length <= expectedBaseName.Length + 1 || name[expectedBaseName.Length] != '_')
            {
                continue;
            }

            var suffix = name[(expectedBaseName.Length + 1)..];
            if (int.TryParse(suffix, out var parsed))
            {
                maxSequence = Math.Max(maxSequence, parsed);
            }
        }

        return maxSequence;
    }

    private int GetInitialSequence(string periodKey)
    {
        var lastSequence = DetectLastSequence(periodKey);
        if (lastSequence < 0)
        {
            return 0;
        }

        return _options.FileConflictMode == FileConflictMode.CreateNew
            ? lastSequence + 1
            : lastSequence;
    }

    private string BuildExpectedBaseName(string periodKey)
    {
        return _options.FileRollingMode switch
        {
            FileRollingMode.Daily => $"{_fileNameWithoutExtension}_{periodKey}",
            FileRollingMode.Hourly => $"{_fileNameWithoutExtension}_{periodKey}",
            _ => _fileNameWithoutExtension
        };
    }

    private void CleanupOldFiles()
    {
        var pattern = $"{_fileNameWithoutExtension}*{_fileExtension}";
        var files = Directory
            .GetFiles(_baseDirectory, pattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .ThenByDescending(info => info.Name, StringComparer.Ordinal)
            .ToArray();

        for (var i = _options.MaxRollingFiles; i < files.Length; i++)
        {
            try
            {
                files[i].Delete();
            }
            catch
            {
            }
        }
    }

    private DateTime GetNow()
    {
        return _options.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now;
    }

    private string GetPeriodKey(DateTime timestamp)
    {
        return _options.FileRollingMode switch
        {
            FileRollingMode.Daily => timestamp.ToString("yyyyMMdd"),
            FileRollingMode.Hourly => timestamp.ToString("yyyyMMdd_HH"),
            _ => string.Empty
        };
    }

    private static string ResolveLogPath(string filePath, string loggerName)
    {
        var resolvedPath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(filePath, AppContext.BaseDirectory);

        if (LooksLikeDirectoryPath(filePath, resolvedPath))
        {
            return Path.Combine(resolvedPath, $"{SanitizeFileName(loggerName)}.log");
        }

        return resolvedPath;
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
        var builder = new StringBuilder(loggerName.Length);

        for (var i = 0; i < loggerName.Length; i++)
        {
            var ch = loggerName[i];
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? "SPLog" : builder.ToString();
    }
}
