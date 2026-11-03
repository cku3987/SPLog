using System.Text;

namespace SPLog;

internal sealed class LogFileWriter : IDisposable
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> ActivePaths = new(FilePathResolver.Comparer);
    private readonly string _path;
    private int _disposed;

    private LogFileWriter(string path, FileStream stream, StreamWriter writer)
    {
        _path = path;
        Stream = stream;
        Writer = writer;
    }

    public FileStream Stream { get; }
    public StreamWriter Writer { get; }

    public static LogFileWriter Open(Func<int, string> buildPath, ref int sequence, FileConflictMode conflict, int bufferSize)
    {
        while (true)
        {
            var path = buildPath(sequence);
            lock (Sync)
            {
                FileStream stream;
                try
                {
                    stream = new FileStream(path,
                        conflict == FileConflictMode.CreateNew ? FileMode.CreateNew : FileMode.Append,
                        FileAccess.Write,
                        conflict == FileConflictMode.CreateNew ? FileShare.Read : FileShare.ReadWrite,
                        bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (IOException ex) when (conflict == FileConflictMode.CreateNew && IsAlreadyExists(ex))
                {
                    sequence = checked(sequence + 1);
                    continue;
                }

                try
                {
                    var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize) { AutoFlush = false };
                    ActivePaths.TryGetValue(path, out var count);
                    ActivePaths[path] = count + 1;
                    return new LogFileWriter(path, stream, writer);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
        }
    }

    private static bool IsAlreadyExists(IOException exception)
    {
        // Win32 ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS, or Unix EEXIST.
        var code = exception.HResult & 0xffff;
        return Path.DirectorySeparatorChar == '\\' ? code == 80 || code == 183 : code == 17;
    }

    public static void DeleteIfInactive(FileInfo file)
    {
        lock (Sync)
        {
            if (!ActivePaths.ContainsKey(file.FullName))
                file.Delete();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            Writer.Dispose();
        }
        finally
        {
            try { Stream.Dispose(); }
            finally
            {
                lock (Sync)
                {
                    var count = ActivePaths[_path];
                    if (count == 1) ActivePaths.Remove(_path);
                    else ActivePaths[_path] = count - 1;
                }
            }
        }
    }
}
