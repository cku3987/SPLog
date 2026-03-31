using System.Text;
using SPLog;

namespace SPLog.Tests;

internal static class TestHelpers
{
    public static void CreateLoggerAndWrite(string logDirectory, string loggerName, FileConflictMode fileConflictMode, string message)
    {
        using var logger = SPLogFactory.Create(options =>
        {
            options.Name = loggerName;
            options.EnableConsole = false;
            options.EnableFile = true;
            options.FilePath = logDirectory;
            options.FileRollingMode = FileRollingMode.None;
            options.FileConflictMode = fileConflictMode;
        });

        logger.Information(message);
    }

    public static string[] GetLogFiles(string logDirectory)
    {
        return Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly);
    }

    public static int CountMessageLines(string logDirectory, string marker)
    {
        var total = 0;
        foreach (var file in Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            foreach (var line in ReadLinesShared(file))
            {
                if (line.Contains(marker, StringComparison.Ordinal))
                {
                    total++;
                }
            }
        }

        return total;
    }

    public static string ReadAllLogText(string logDirectory)
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
