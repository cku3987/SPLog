namespace SPLog;

internal sealed class ErrorFileSink : ILogSink
{
    private readonly SPLogOptions _formatOptions;
    private readonly SPLogErrorFileOptions _errorOptions;
    private readonly SharedFileTargetLease _lease;

    public ErrorFileSink(SPLogOptions formatOptions)
    {
        _formatOptions = formatOptions.Clone();
        _errorOptions = formatOptions.ErrorFile?.Clone()
            ?? throw new InvalidOperationException("ErrorFile options are required.");
        _formatOptions.UseUtcTimestamp = _errorOptions.UseUtcTimestamp;
        _lease = SharedFileTargetRegistry.Acquire(_errorOptions, formatOptions.Name);
    }

    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        var batch = entries.ToArray();
        string[]? lines = null;
        var count = 0;

        for (var i = 0; i < batch.Length; i++)
        {
            var entry = batch[i];
            if (entry.Level < _errorOptions.MinimumLevel || entry.Level == LogLevel.None)
            {
                continue;
            }

            if (lines is null)
            {
                lines = new string[batch.Length];
            }

            lines[count++] = SPLogFormatter.Format(entry, _formatOptions);
        }

        if (count == 0 || lines is null)
        {
            return;
        }

        if (count != lines.Length)
        {
            Array.Resize(ref lines, count);
        }

        await _lease.Target.WriteBatchAsync(lines, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _lease.Dispose();
    }
}
