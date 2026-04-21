using System.Text;
using SPLog;
using SPLog.Tests;

var tests = new (string Name, Func<Task> Execute)[]
{
    ("Dispose flushes queued messages", DisposeFlushesQueuedMessagesAsync),
    ("BatchSize does not wait for a full batch", BatchSizeDoesNotWaitForFullBatchAsync),
    ("Queue backpressure waits instead of throwing", QueueBackpressureWaitsInsteadOfThrowingAsync),
    ("MinimumLevel filters lower levels", MinimumLevelFiltersMessagesAsync),
    ("Category loggers share the same writer and keep hierarchical names", CategoryLoggersShareWriterAsync),
    ("Sequence numbers can be included for ordering diagnostics", SequenceNumbersCanBeIncludedAsync),
    ("Timestamp format can be customized", TimestampFormatCanBeCustomizedAsync),
    ("Timestamp normalization keeps output order monotonic", TimestampNormalizationKeepsMonotonicOrderAsync),
    ("Exception logging writes exception details", ExceptionLoggingWritesDetailsAsync),
    ("Append reuses the current target file", AppendReusesExistingFileAsync),
    ("CreateNew creates the next suffixed file", CreateNewCreatesSuffixedFileAsync),
    ("CreateNew and size rolling keep sequence numbering", CreateNewAndSizeRollingContinueSequenceAsync),
    ("UpdateFromJsonFile updates an existing options object", UpdateFromJsonFileUpdatesOptionsAsync)
};

var passed = 0;

foreach (var test in tests)
{
    try
    {
        await test.Execute().ConfigureAwait(false);
        Console.WriteLine($"[PASS] {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] {test.Name}");
        Console.WriteLine(ex);
    }
}

Console.WriteLine();
Console.WriteLine($"Completed {tests.Length} tests. Passed: {passed}, Failed: {tests.Length - passed}");
return passed == tests.Length ? 0 : 1;

static Task DisposeFlushesQueuedMessagesAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "FlushTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
               options.BatchSize = 10;
               options.FlushIntervalMs = 5_000;
           }))
    {
        logger.Information("FLUSH|1");
        logger.Information("FLUSH|2");
        logger.Information("FLUSH|3");
    }

    TestAssert.Equal(3, TestHelpers.CountMessageLines(logDirectory, "FLUSH|"), "Dispose should flush remaining queued entries.");
    return Task.CompletedTask;
}

static async Task BatchSizeDoesNotWaitForFullBatchAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using var logger = SPLogFactory.Create(options =>
    {
        options.Name = "BatchTest";
        options.EnableConsole = false;
        options.EnableFile = true;
        options.FilePath = logDirectory;
        options.FileRollingMode = FileRollingMode.None;
        options.BatchSize = 10;
        options.FlushIntervalMs = 50;
    });

    logger.Information("BATCH|1");
    logger.Information("BATCH|2");
    logger.Information("BATCH|3");

    await Task.Delay(300).ConfigureAwait(false);

    TestAssert.Equal(3, TestHelpers.CountMessageLines(logDirectory, "BATCH|"), "SPLog should write a partial batch without waiting for all 10 entries.");
}

static async Task QueueBackpressureWaitsInsteadOfThrowingAsync()
{
    using var sink = new BlockingLogSink();
    using (var processor = new AsyncLogProcessor(
               new SPLogOptions
               {
                   Name = "QueueBackpressureTest",
                   BatchSize = 1,
                   QueueCapacity = 1,
                   FlushIntervalMs = 0
               },
               sink))
    {
        processor.Enqueue(new LogEntry(DateTime.UtcNow, LogLevel.Information, "QueueBackpressureTest", 1, "QUEUE|1"));
        await sink.FirstWriteStarted.ConfigureAwait(false);

        processor.Enqueue(new LogEntry(DateTime.UtcNow, LogLevel.Information, "QueueBackpressureTest", 1, "QUEUE|2"));

        var blockedEnqueue = Task.Run(() =>
            processor.Enqueue(new LogEntry(DateTime.UtcNow, LogLevel.Information, "QueueBackpressureTest", 1, "QUEUE|3")));

        await Task.Delay(100).ConfigureAwait(false);

        TestAssert.False(
            blockedEnqueue.IsCompleted,
            "When the queue is full, Enqueue should wait instead of throwing or finishing immediately.");

        sink.ReleaseFirstWrite();
        await blockedEnqueue.ConfigureAwait(false);
    }

    TestAssert.Equal(3, sink.WrittenEntries, "All queued messages should still be delivered after backpressure is released.");
}

static Task MinimumLevelFiltersMessagesAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "LevelTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
               options.MinimumLevel = LogLevel.Warning;
           }))
    {
        logger.Information("LEVEL|INFO");
        logger.Warning("LEVEL|WARN");
        logger.Error("LEVEL|ERR");
    }

    var content = TestHelpers.ReadAllLogText(logDirectory);
    TestAssert.False(content.Contains("LEVEL|INFO", StringComparison.Ordinal), "Information messages should be filtered out.");
    TestAssert.True(content.Contains("LEVEL|WARN", StringComparison.Ordinal), "Warning should be written.");
    TestAssert.True(content.Contains("LEVEL|ERR", StringComparison.Ordinal), "Error should be written.");
    return Task.CompletedTask;
}

static Task CategoryLoggersShareWriterAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using var rootLogger = SPLogFactory.Create(options =>
    {
        options.Name = "App";
        options.EnableConsole = false;
        options.EnableFile = true;
        options.FilePath = logDirectory;
        options.FileRollingMode = FileRollingMode.None;
    });

    var networkLogger = rootLogger.CreateCategory("Network");
    var socketLogger = networkLogger.CreateCategory("Socket");

    networkLogger.Information("CAT|NETWORK");
    socketLogger.Warning("CAT|SOCKET");
    networkLogger.Dispose();
    rootLogger.Information("CAT|ROOT");

    var files = TestHelpers.GetLogFiles(logDirectory);
    TestAssert.Equal(1, files.Length, "Category loggers should share the same underlying file writer.");

    var content = TestHelpers.ReadAllLogText(logDirectory);
    TestAssert.True(content.Contains("[App.Network]", StringComparison.Ordinal), "Child category logger should include the combined logger name.");
    TestAssert.True(content.Contains("[App.Network.Socket]", StringComparison.Ordinal), "Nested category logger should include the full hierarchical logger name.");
    TestAssert.True(content.Contains("[App]", StringComparison.Ordinal), "Root logger should keep the base logger name.");
    TestAssert.True(content.Contains("CAT|ROOT", StringComparison.Ordinal), "Disposing a category logger should not stop the root logger.");
    return Task.CompletedTask;
}

static Task SequenceNumbersCanBeIncludedAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "SequenceHeaderTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.IncludeSequenceNumber = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
           }))
    {
        logger.Information("ORDER|1");
        logger.Information("ORDER|2");
        logger.Information("ORDER|3");
    }

    var content = TestHelpers.ReadAllLogText(logDirectory);
    TestAssert.True(content.Contains("[Q:1]", StringComparison.Ordinal), "Sequence header should include the first queue order number.");
    TestAssert.True(content.Contains("[Q:2]", StringComparison.Ordinal), "Sequence header should include the second queue order number.");
    TestAssert.True(content.Contains("[Q:3]", StringComparison.Ordinal), "Sequence header should include the third queue order number.");
    return Task.CompletedTask;
}

static Task TimestampFormatCanBeCustomizedAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "PrecisionTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
               options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff";
           }))
    {
        logger.Information("PRECISION|1");
    }

    var content = TestHelpers.ReadAllLogText(logDirectory);
    TestAssert.True(
        System.Text.RegularExpressions.Regex.IsMatch(content, @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{5}\]"),
        "The formatted timestamp should use the configured custom format.");
    return Task.CompletedTask;
}

static Task TimestampNormalizationKeepsMonotonicOrderAsync()
{
    var baseTime = new DateTime(2026, 3, 31, 15, 15, 35, 523, DateTimeKind.Local);
    var entries = new[]
    {
        new LogEntry(baseTime, LogLevel.Information, "App.Core", 7, "MSG|1"),
        new LogEntry(baseTime.AddMilliseconds(-7), LogLevel.Information, "App", 10, "MSG|2"),
        new LogEntry(baseTime.AddMilliseconds(1), LogLevel.Information, "App.Network", 11, "MSG|3")
    };

    long lastTimestampTicks = 0;
    MonotonicTimestampNormalizer.NormalizeInPlace(entries, ref lastTimestampTicks);

    TestAssert.True(entries[0].Timestamp <= entries[1].Timestamp, "Normalized timestamps should not move backward between entry 1 and entry 2.");
    TestAssert.True(entries[1].Timestamp <= entries[2].Timestamp, "Normalized timestamps should not move backward between entry 2 and entry 3.");
    TestAssert.Equal(baseTime, entries[1].Timestamp, "An earlier timestamp should be clamped to the last written timestamp.");
    return Task.CompletedTask;
}

static Task ExceptionLoggingWritesDetailsAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "ExceptionTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
           }))
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "EX|failure");
        }
    }

    var content = TestHelpers.ReadAllLogText(logDirectory);
    TestAssert.True(content.Contains("EX|failure", StringComparison.Ordinal), "Custom exception message should be written.");
    TestAssert.True(content.Contains("InvalidOperationException", StringComparison.Ordinal), "Exception type should be written.");
    TestAssert.True(content.Contains("boom", StringComparison.Ordinal), "Exception message should be written.");
    return Task.CompletedTask;
}

static Task AppendReusesExistingFileAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    TestHelpers.CreateLoggerAndWrite(logDirectory, "AppendTest", FileConflictMode.Append, "APPEND|1");
    TestHelpers.CreateLoggerAndWrite(logDirectory, "AppendTest", FileConflictMode.Append, "APPEND|2");

    var files = TestHelpers.GetLogFiles(logDirectory);
    TestAssert.Equal(1, files.Length, "Append should keep using the existing target file.");
    TestAssert.Equal("AppendTest.log", Path.GetFileName(files[0]), "Append mode should stay on the base filename when no size rolling occurs.");
    TestAssert.Equal(2, TestHelpers.CountMessageLines(logDirectory, "APPEND|"), "Both messages should be in the same file.");
    return Task.CompletedTask;
}

static Task CreateNewCreatesSuffixedFileAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    TestHelpers.CreateLoggerAndWrite(logDirectory, "CreateNewTest", FileConflictMode.CreateNew, "NEW|1");
    TestHelpers.CreateLoggerAndWrite(logDirectory, "CreateNewTest", FileConflictMode.CreateNew, "NEW|2");

    var fileNames = TestHelpers.GetLogFiles(logDirectory).Select(Path.GetFileName).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    TestAssert.SequenceEqual(
        new[] { "CreateNewTest.log", "CreateNewTest_001.log" },
        fileNames,
        "CreateNew should create the next suffixed file on the next logger start.");
    TestAssert.Equal(2, TestHelpers.CountMessageLines(logDirectory, "NEW|"), "Both messages should be written across the created files.");
    return Task.CompletedTask;
}

static Task CreateNewAndSizeRollingContinueSequenceAsync()
{
    using var scope = new TestScope();
    var logDirectory = scope.CreateSubdirectory("logs");

    TestHelpers.CreateLoggerAndWrite(logDirectory, "SequenceTest", FileConflictMode.CreateNew, "SEQ|base");

    var payload = new string('X', 200);

    using (var logger = SPLogFactory.Create(options =>
           {
               options.Name = "SequenceTest";
               options.EnableConsole = false;
               options.EnableFile = true;
               options.FilePath = logDirectory;
               options.FileRollingMode = FileRollingMode.None;
               options.FileConflictMode = FileConflictMode.CreateNew;
               options.BatchSize = 1;
               options.MaxFileSizeBytes = 100;
           }))
    {
        logger.Information($"SEQ|001|{payload}");
        logger.Information($"SEQ|002|{payload}");
    }

    var fileNames = TestHelpers.GetLogFiles(logDirectory).Select(Path.GetFileName).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    TestAssert.SequenceEqual(
        new[] { "SequenceTest.log", "SequenceTest_001.log", "SequenceTest_002.log" },
        fileNames,
        "CreateNew and size rolling should continue using the same numeric sequence.");
    return Task.CompletedTask;
}

static Task UpdateFromJsonFileUpdatesOptionsAsync()
{
    using var scope = new TestScope();
    var jsonPath = Path.Combine(scope.RootDirectory, "config.json");
    var json = """
               {
                 "Name": "  Reloaded  ",
                 "EnableConsole": false,
                 "EnableFile": true,
                 "FilePath": "logs",
                 "FileConflictMode": "CreateNew",
                 "BatchSize": 12
               }
               """;

    File.WriteAllText(jsonPath, json, Encoding.UTF8);

    var options = new SPLogOptions
    {
        Name = "Original",
        EnableConsole = true,
        EnableFile = false,
        FilePath = "old"
    };

    SPLogConfiguration.UpdateFromJsonFile(options, jsonPath);

    TestAssert.Equal("Reloaded", options.Name, "UpdateFromJsonFile should normalize and replace the Name.");
    TestAssert.False(options.EnableConsole, "UpdateFromJsonFile should replace console setting.");
    TestAssert.True(options.EnableFile, "UpdateFromJsonFile should replace file setting.");
    TestAssert.Equal("logs", options.FilePath, "UpdateFromJsonFile should normalize and replace FilePath.");
    TestAssert.Equal(FileConflictMode.CreateNew, options.FileConflictMode, "UpdateFromJsonFile should replace enum values.");
    TestAssert.Equal(12, options.BatchSize, "UpdateFromJsonFile should replace BatchSize.");
    return Task.CompletedTask;
}
