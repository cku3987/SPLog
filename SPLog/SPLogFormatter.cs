using System.Text;

namespace SPLog;

internal static class SPLogFormatter
{
    public static string Format(LogEntry entry, SPLogOptions options)
    {
        var builder = new StringBuilder(128 + entry.Message.Length);
        var timestamp = options.UseUtcTimestamp ? entry.Timestamp.ToUniversalTime() : entry.Timestamp;
        builder.Append('[');
        builder.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append("] [");
        builder.Append(entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "UNK"
        });
        builder.Append(']');

        if (options.IncludeLoggerName)
        {
            builder.Append(" [");
            builder.Append(entry.LoggerName);
            builder.Append(']');
        }

        if (options.IncludeThreadId)
        {
            builder.Append(" [T:");
            builder.Append(entry.ThreadId);
            builder.Append(']');
        }

        builder.Append(' ');
        builder.Append(entry.Message);

        if (!string.IsNullOrWhiteSpace(entry.ExceptionText))
        {
            builder.AppendLine();
            builder.Append(entry.ExceptionText);
        }

        return builder.ToString();
    }
}
