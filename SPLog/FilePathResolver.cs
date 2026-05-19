using System.Text;

namespace SPLog;

internal static class FilePathResolver
{
    public static string ResolveLogPath(string filePath, string loggerName)
    {
        var resolvedPath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, filePath));

        if (LooksLikeDirectoryPath(filePath, resolvedPath))
        {
            return Path.Combine(resolvedPath, $"{SanitizeFileName(loggerName)}.log");
        }

        return resolvedPath;
    }

    private static bool LooksLikeDirectoryPath(string originalPath, string resolvedPath)
    {
        if (originalPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || originalPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
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
