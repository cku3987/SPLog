namespace SPLog;

internal sealed class FileLogSink : ILogSink
{
    private readonly SPLogOptions _options;
    private readonly string _baseDirectory;
    private readonly string _fileNameWithoutExtension;
    private readonly string _fileExtension;
    private readonly LogFileSet _files;
    private readonly SharedFileTargetLease? _sharedLease;
    private LogFileWriter? _file;
    private string _currentPeriodKey = string.Empty;
    private int _currentSequence;

    public FileLogSink(SPLogOptions options)
    {
        _options = options;
        var fullPath = FilePathResolver.ResolveLogPath(options.FilePath, options.Name);
        _baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        _fileExtension = Path.GetExtension(fullPath);
        _files = new LogFileSet(_baseDirectory, _fileNameWithoutExtension, _fileExtension, options.FileRollingMode);
        if (options.FileConflictMode == FileConflictMode.Append)
        {
            _sharedLease = SharedFileTargetRegistry.Acquire(options);
        }
    }

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        var batch = entries.ToArray();
        if (_sharedLease is not null)
        {
            var lines = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                lines[i] = SPLogFormatter.Format(batch[i], _options);
            }

            await _sharedLease.Target.WriteBatchAsync(lines, cancellationToken).ConfigureAwait(false);
            return;
        }

        for (var i = 0; i < batch.Length; i++)
        {
            var line = SPLogFormatter.Format(batch[i], _options);
            EnsureWriter();
            RotateIfNeeded();
            cancellationToken.ThrowIfCancellationRequested();
            await _file!.Writer.WriteLineAsync(line).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_file is not null)
        {
            await _file.Writer.FlushAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_sharedLease is not null)
        {
            _sharedLease.Dispose();
            return;
        }

        _file?.Dispose();
    }

    private void RotateIfNeeded()
    {
        if (_file is null)
        {
            return;
        }

        var now = GetNow();
        var periodKey = GetPeriodKey(now);

        if (periodKey != _currentPeriodKey)
        {
            SwitchWriter(periodKey, 0);
            CleanupOldFiles();
            return;
        }

        if (_file.Stream.Length < _options.MaxFileSizeBytes)
        {
            return;
        }

        SwitchWriter(_currentPeriodKey, checked(_currentSequence + 1));
        CleanupOldFiles();
    }

    private void SwitchWriter(string periodKey, int sequence)
    {
        _file?.Dispose();
        _file = null;
        _currentPeriodKey = periodKey;
        _currentSequence = sequence;
        _file = OpenWriter(periodKey);
    }

    private void EnsureWriter()
    {
        if (_file is not null)
        {
            return;
        }

        Directory.CreateDirectory(_baseDirectory);
        _currentPeriodKey = GetPeriodKey(GetNow());
        _currentSequence = GetInitialSequence(_currentPeriodKey);
        _file = OpenWriter(_currentPeriodKey);
        CleanupOldFiles();
    }

    private LogFileWriter OpenWriter(string periodKey)
    {
        return LogFileWriter.Open(sequence => BuildFilePath(periodKey, sequence),
            ref _currentSequence, _options.FileConflictMode, _options.FileBufferSize);
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

    private int GetInitialSequence(string periodKey)
    {
        var lastSequence = _files.DetectLastSequence(periodKey);
        if (lastSequence < 0)
        {
            return 0;
        }

        return _options.FileConflictMode == FileConflictMode.CreateNew
            ? checked(lastSequence + 1)
            : lastSequence;
    }

    private void CleanupOldFiles()
    {
        _files.Cleanup(_options.MaxRollingFiles, BuildFilePath(_currentPeriodKey, _currentSequence));
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
}
