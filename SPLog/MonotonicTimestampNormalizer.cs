namespace SPLog;

internal static class MonotonicTimestampNormalizer
{
    public static void NormalizeInPlace(Span<LogEntry> entries, ref long lastTimestampTicks)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var entryTicks = entry.Timestamp.Ticks;

            if (entryTicks < lastTimestampTicks)
            {
                entries[i] = new LogEntry(
                    Timestamp: new DateTime(lastTimestampTicks, entry.Timestamp.Kind),
                    Level: entry.Level,
                    LoggerName: entry.LoggerName,
                    ThreadId: entry.ThreadId,
                    Message: entry.Message,
                    ExceptionText: entry.ExceptionText,
                    SequenceNumber: entry.SequenceNumber);
                continue;
            }

            lastTimestampTicks = entryTicks;
        }
    }
}
