using System.Collections.Generic;
using System.Text;

namespace SPLog;

internal static class SharedFileTargetRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, SharedFileTargetEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static SharedFileTargetLease Acquire(SPLogErrorFileOptions options, string loggerName)
    {
        var targetOptions = SharedFileTargetOptions.Create(options, loggerName);

        lock (Sync)
        {
            if (Entries.TryGetValue(targetOptions.ResolvedBaseFilePath, out var existing))
            {
                if (!existing.Options.Matches(targetOptions))
                {
                    throw new InvalidOperationException(
                        $"Shared error file '{targetOptions.ResolvedBaseFilePath}' must use the same error file settings across loggers.");
                }

                existing.ReferenceCount++;
                return new SharedFileTargetLease(targetOptions.ResolvedBaseFilePath, existing.Target);
            }

            var target = new SharedFileTarget(targetOptions);
            Entries[targetOptions.ResolvedBaseFilePath] = new SharedFileTargetEntry(targetOptions, target);
            return new SharedFileTargetLease(targetOptions.ResolvedBaseFilePath, target);
        }
    }

    internal static void Release(string key)
    {
        SharedFileTarget? targetToDispose = null;

        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out var existing))
            {
                return;
            }

            existing.ReferenceCount--;
            if (existing.ReferenceCount == 0)
            {
                Entries.Remove(key);
                targetToDispose = existing.Target;
            }
        }

        targetToDispose?.Dispose();
    }

    private sealed class SharedFileTargetEntry
    {
        public SharedFileTargetEntry(SharedFileTargetOptions options, SharedFileTarget target)
        {
            Options = options;
            Target = target;
            ReferenceCount = 1;
        }

        public SharedFileTargetOptions Options { get; }

        public SharedFileTarget Target { get; }

        public int ReferenceCount { get; set; }
    }
}

internal sealed class SharedFileTargetLease : IDisposable
{
    private readonly string _key;
    private int _disposed;

    public SharedFileTargetLease(string key, SharedFileTarget target)
    {
        _key = key;
        Target = target;
    }

    public SharedFileTarget Target { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SharedFileTargetRegistry.Release(_key);
    }
}

internal sealed class SharedFileTargetOptions
{
    public string ResolvedBaseFilePath { get; init; } = string.Empty;

    public bool UseUtcTimestamp { get; init; }

    public FileConflictMode FileConflictMode { get; init; }

    public FileRollingMode FileRollingMode { get; init; }

    public long MaxFileSizeBytes { get; init; }

    public int MaxRollingFiles { get; init; }

    public int FileBufferSize { get; init; }

    public static SharedFileTargetOptions Create(SPLogErrorFileOptions options, string loggerName)
    {
        return new SharedFileTargetOptions
        {
            ResolvedBaseFilePath = FilePathResolver.ResolveLogPath(options.FilePath, loggerName),
            UseUtcTimestamp = options.UseUtcTimestamp,
            FileConflictMode = options.FileConflictMode,
            FileRollingMode = options.FileRollingMode,
            MaxFileSizeBytes = options.MaxFileSizeBytes,
            MaxRollingFiles = options.MaxRollingFiles,
            FileBufferSize = options.FileBufferSize
        };
    }

    public bool Matches(SharedFileTargetOptions other)
    {
        return UseUtcTimestamp == other.UseUtcTimestamp
               && FileConflictMode == other.FileConflictMode
               && FileRollingMode == other.FileRollingMode
               && MaxFileSizeBytes == other.MaxFileSizeBytes
               && MaxRollingFiles == other.MaxRollingFiles
               && FileBufferSize == other.FileBufferSize;
    }
}

internal sealed class SharedFileTarget : IDisposable
{
    private readonly SharedFileTargetOptions _options;
    private readonly string _baseDirectory;
    private readonly string _fileNameWithoutExtension;
    private readonly string _fileExtension;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StreamWriter? _writer;
    private FileStream? _stream;
    private string _currentPeriodKey = string.Empty;
    private int _currentSequence;
    private int _disposed;

    public SharedFileTarget(SharedFileTargetOptions options)
    {
        _options = options;
        _baseDirectory = Path.GetDirectoryName(options.ResolvedBaseFilePath) ?? Directory.GetCurrentDirectory();
        _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(options.ResolvedBaseFilePath);
        _fileExtension = Path.GetExtension(options.ResolvedBaseFilePath);
    }

    public async ValueTask WriteBatchAsync(string[] lines, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            for (var i = 0; i < lines.Length; i++)
            {
                EnsureWriter();
                RotateIfNeeded();
                cancellationToken.ThrowIfCancellationRequested();
                await _writer!.WriteLineAsync(lines[i]).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_writer is not null)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _gate.Wait();
        try
        {
            _writer?.Dispose();
            _stream?.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void RotateIfNeeded()
    {
        if (_stream is null)
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

        if (_stream.Length < _options.MaxFileSizeBytes)
        {
            return;
        }

        SwitchWriter(_currentPeriodKey, _currentSequence + 1);
        CleanupOldFiles();
    }

    private void SwitchWriter(string periodKey, int sequence)
    {
        _writer?.Dispose();
        _stream?.Dispose();
        _currentPeriodKey = periodKey;
        _currentSequence = sequence;
        (_stream, _writer) = OpenWriter(periodKey, sequence);
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(_baseDirectory);
        _currentPeriodKey = GetPeriodKey(GetNow());
        _currentSequence = GetInitialSequence(_currentPeriodKey);
        (_stream, _writer) = OpenWriter(_currentPeriodKey, _currentSequence);
        CleanupOldFiles();
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

            var suffix = name.Substring(expectedBaseName.Length + 1);
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

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SharedFileTarget));
        }
    }
}
