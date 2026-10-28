using SPLog;

namespace SPLog.Tests;

internal static class FileTargetRegressionTests
{
    public static async Task RetentionPreservesUnrelatedFilesAsync(
        FileRollingMode rolling, FileConflictMode conflict, bool errorFile)
    {
        using var scope = new TestScope();
        var period = rolling switch
        {
            FileRollingMode.Daily => "_20000101",
            FileRollingMode.Hourly => "_20000101_00",
            _ => string.Empty
        };
        var unrelated = new List<string>
        {
            "AppAudit.log", "AppAudit_20000101.log", "App_Backup.log", "App.log.bak",
            "App_20000101_25.log", "App_20000101_001.backup.log",
            $"App{period}_1.log", $"App{period}_000.log", $"App{period}_0001.log",
            $"App{period}_+001.log", $"App{period}_999999999999.log"
        };
        if (rolling != FileRollingMode.None)
        {
            unrelated.Add("App_20001301.log");
        }
        foreach (var name in unrelated)
        {
            File.WriteAllText(Path.Combine(scope.RootDirectory, name), $"KEEP:{name}");
        }

        var oldOwnFiles = new[] { $"App{period}.log", $"App{period}_001.log", $"App{period}_002.log" };
        foreach (var name in oldOwnFiles)
        {
            var path = Path.Combine(scope.RootDirectory, name);
            File.WriteAllText(path, "OLD");
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        }

        var options = Options(Path.Combine(scope.RootDirectory, "App.log"));
        options.FileRollingMode = rolling;
        options.FileConflictMode = conflict;
        options.MaxFileSizeBytes = 128;
        options.MaxRollingFiles = 1;
        if (errorFile)
        {
            options.ErrorFile = new SPLogErrorFileOptions
            {
                FilePath = options.FilePath, FileRollingMode = rolling, FileConflictMode = conflict,
                MaxFileSizeBytes = 128, MaxRollingFiles = 1
            };
        }

        using (ILogSink sink = errorFile ? new ErrorFileSink(options) : new FileLogSink(options))
        {
            for (var i = 0; i < 3; i++)
            {
                await sink.WriteBatchAsync(new[]
                {
                    new LogEntry(DateTime.UtcNow, LogLevel.Error, "App", 1, $"NEW:{i}:" + new string('X', 256))
                }, CancellationToken.None);
                foreach (var name in unrelated)
                {
                    var path = Path.Combine(scope.RootDirectory, name);
                    TestAssert.True(File.Exists(path), $"Retention must preserve unrelated file {name}.");
                    TestAssert.Equal($"KEEP:{name}", File.ReadAllText(path), "Unrelated content must not change.");
                }
            }
        }

        var own = Directory.GetFiles(scope.RootDirectory)
            .Where(path => !unrelated.Contains(Path.GetFileName(path))).ToArray();
        TestAssert.Equal(1, own.Length, "Retention must still enforce the configured limit on owned files.");
        TestAssert.True(File.ReadAllText(own[0]).Contains("NEW:2:"), "The current file must survive cleanup.");
        foreach (var name in oldOwnFiles)
        {
            TestAssert.False(File.Exists(Path.Combine(scope.RootDirectory, name)), "Old owned files should be removed after size rolling.");
        }
    }

    public static Task PathCasingAsync(bool directoryCase, bool errorFile)
    {
        using var scope = new TestScope();
        var upperDirectory = directoryCase ? scope.CreateSubdirectory("Logs") : scope.RootDirectory;
        var lowerDirectory = directoryCase ? scope.CreateSubdirectory("logs") : scope.RootDirectory;
        var upper = Path.Combine(upperDirectory, "App.log");
        var lower = Path.Combine(lowerDirectory, directoryCase ? "App.log" : "app.log");
        var firstOptions = Options(upper);
        var secondOptions = Options(lower);
        if (errorFile)
        {
            firstOptions.EnableFile = secondOptions.EnableFile = false;
            firstOptions.ErrorFile = new SPLogErrorFileOptions { FilePath = upper, FileRollingMode = FileRollingMode.None };
            secondOptions.ErrorFile = new SPLogErrorFileOptions { FilePath = lower, FileRollingMode = FileRollingMode.None };
        }

        using (var first = SPLogFactory.Create(firstOptions))
        using (var second = SPLogFactory.Create(secondOptions))
        {
            first.Error("CASE:UPPER");
            second.Error("CASE:LOWER");
            first.Dispose();
            second.Error("CASE:LOWER:AFTER");
        }

        var upperText = File.ReadAllText(upper);
        var lowerText = File.ReadAllText(lower);
        if (Path.DirectorySeparatorChar == '\\')
        {
            TestAssert.Equal(upperText, lowerText, "Windows case aliases should share one target.");
            TestAssert.True(upperText.Contains("CASE:UPPER") && upperText.Contains("CASE:LOWER:AFTER"),
                "The shared Windows writer must preserve both loggers and its last lease.");
        }
        else
        {
            TestAssert.True(upperText.Contains("CASE:UPPER") && !upperText.Contains("CASE:LOWER"),
                "Case-distinct upper path must not receive the other logger's messages.");
            TestAssert.True(lowerText.Contains("CASE:LOWER:AFTER") && !lowerText.Contains("CASE:UPPER"),
                "Case-distinct lower path must remain independent.");
        }

        return Task.CompletedTask;
    }

    public static async Task LiteralFileNameAsync()
    {
        using var scope = new TestScope();
        var name = Path.DirectorySeparatorChar == '\\' ? "App[1]" : "App?";
        var unrelated = Path.Combine(scope.RootDirectory, "AppX.log");
        File.WriteAllText(unrelated, "KEEP");
        var options = Options(Path.Combine(scope.RootDirectory, name + ".log"));
        options.MaxRollingFiles = 1;
        using (var sink = new FileLogSink(options))
        {
            await sink.WriteBatchAsync(new[] { new LogEntry(DateTime.UtcNow, LogLevel.Information, name, 1, "LITERAL") }, CancellationToken.None);
        }
        TestAssert.Equal("KEEP", File.ReadAllText(unrelated), "File names must not be interpreted as search patterns.");
        TestAssert.True(File.ReadAllText(options.FilePath).Contains("LITERAL"), "Literal-name output must be written.");
    }

    public static async Task KeepsCurrentFileAsync()
    {
        using var scope = new TestScope();
        var current = Path.Combine(scope.RootDirectory, "App_001.log");
        var other = Path.Combine(scope.RootDirectory, "App.log");
        File.WriteAllText(current, "OLD-CURRENT");
        File.SetCreationTimeUtc(current, DateTime.UtcNow.AddDays(-2));
        File.WriteAllText(other, "NEWER-CREATION-TIME");
        using (var sink = new FileLogSink(Options(other)))
        {
            await sink.WriteBatchAsync(new[] { new LogEntry(DateTime.UtcNow, LogLevel.Information, "App", 1, "KEEP-CURRENT") }, CancellationToken.None);
        }
        TestAssert.True(File.ReadAllText(current).Contains("KEEP-CURRENT"), "The file currently receiving writes must never be selected for deletion.");
        TestAssert.False(File.Exists(other), "Retain the current file instead of a newer inactive file.");
    }

    public static async Task RolledFileCasingAsync(FileConflictMode conflict)
    {
        using var scope = new TestScope();
        var previous = Path.Combine(scope.RootDirectory, "app_1000.LOG");
        File.WriteAllText(previous, "PREVIOUS");
        var options = Options(Path.Combine(scope.RootDirectory, "App.log"));
        options.FileConflictMode = conflict;
        using (var sink = new FileLogSink(options))
        {
            await sink.WriteBatchAsync(new[] { new LogEntry(DateTime.UtcNow, LogLevel.Information, "App", 1, "NEXT") }, CancellationToken.None);
        }
        if (Path.DirectorySeparatorChar == '\\')
        {
            var expected = conflict == FileConflictMode.Append ? "App_1000.log" : "App_1001.log";
            TestAssert.True(File.ReadAllText(Path.Combine(scope.RootDirectory, expected)).Contains("NEXT"),
                "Windows sequence discovery should honor case aliases and sequences above 999.");
            TestAssert.Equal(1, Directory.GetFiles(scope.RootDirectory).Length, "Windows retention should recognize case aliases.");
        }
        else
        {
            TestAssert.Equal("PREVIOUS", File.ReadAllText(previous), "A differently cased file belongs to another target on Linux.");
            TestAssert.True(File.ReadAllText(options.FilePath).Contains("NEXT"), "A different case must not affect the new file's sequence.");
        }
    }

    private static SPLogOptions Options(string path) => new()
    {
        Name = "App", EnableConsole = false, EnableFile = true, FilePath = path,
        FileRollingMode = FileRollingMode.None, FileConflictMode = FileConflictMode.Append,
        BatchSize = 1, FlushIntervalMs = 1, MaxRollingFiles = 1
    };
}
