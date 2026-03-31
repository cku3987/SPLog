namespace SPLog.Tests;

internal sealed class TestScope : IDisposable
{
    public string RootDirectory { get; }

    public TestScope()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "SPLogTests",
            $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(RootDirectory);
    }

    public string CreateSubdirectory(string name)
    {
        var path = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
