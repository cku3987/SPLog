using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using SPLog;

namespace SPLog.Tests;

internal static class Release126RegressionTests
{
    public static async Task ConcurrentCreateNewAsync(FileRollingMode rolling)
    {
        using var scope = new TestScope();
        const int count = 32;
        using var start = new ManualResetEventSlim(false);
        using var ready = new CountdownEvent(count);
        var tasks = Enumerable.Range(0, count).Select(index => Task.Factory.StartNew(() =>
        {
            using var logger = SPLogFactory.Create(Options(scope.RootDirectory, rolling));
            ready.Signal();
            TestAssert.True(start.Wait(TimeSpan.FromSeconds(15)), "Concurrent start timed out.");
            logger.Information($"OWNER:{index:D3}");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            TestAssert.True(ready.Wait(TimeSpan.FromSeconds(15)), "Producers must be ready.");
        }
        finally
        {
            start.Set();
        }
        await Task.WhenAll(tasks);
        var files = Directory.GetFiles(scope.RootDirectory, "*.log");
        TestAssert.Equal(count, files.Length, "Each independent CreateNew logger needs its own file.");
        var lines = files.SelectMany(File.ReadAllLines).ToArray();
        TestAssert.Equal(count, lines.Length, "Concurrent writes must not overwrite any entries.");
        for (var i = 0; i < count; i++)
            TestAssert.Equal(1, lines.Count(line => line.EndsWith($"OWNER:{i:D3}", StringComparison.Ordinal)),
                "Every producer's marker must appear exactly once.");
    }

    public static async Task RollingSkipsExistingAsync(bool errorFile, bool periodChange)
    {
        using var scope = new TestScope();
        var options = Options(scope.RootDirectory, periodChange ? FileRollingMode.Daily : FileRollingMode.None);
        options.MaxFileSizeBytes = 1;
        var period = periodChange ? "_" + DateTime.Now.ToString("yyyyMMdd") : "";
        using ILogSink sink = errorFile ? new ErrorFileSink(WithError(options)) : new FileLogSink(options);
        await Write(sink, "FIRST");
        var occupied = Path.Combine(scope.RootDirectory, $"App{period}_001.log");
        File.WriteAllText(occupied, "OTHER-WRITER");
        if (periodChange)
        {
            // Exercise the period-change branch without sleeping until midnight.
            object target = sink;
            if (errorFile)
            {
                var lease = (SharedFileTargetLease)typeof(ErrorFileSink)
                    .GetField("_lease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sink)!;
                target = lease.Target;
            }
            target.GetType().GetField("_currentPeriodKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(target, "previous-period");
        }
        await Write(sink, "SECOND");
        TestAssert.Equal("OTHER-WRITER", File.ReadAllText(occupied), "Rolling must not append to an occupied filename.");
        TestAssert.False(ReadActive(Path.Combine(scope.RootDirectory, $"App{period}.log")).Contains("SECOND"),
            "A period change must not reopen an existing base file in CreateNew mode.");
        TestAssert.True(ReadActive(Path.Combine(scope.RootDirectory, $"App{period}_002.log")).Contains("SECOND"),
            "Rolling must claim the next available filename.");
    }

    public static async Task RetentionPreservesActiveWritersAsync()
    {
        using var scope = new TestScope();
        var options = Options(scope.RootDirectory, FileRollingMode.None);
        options.MaxRollingFiles = 1;
        using (var first = new FileLogSink(options))
        {
            await Write(first, "FIRST");
            using (var second = new FileLogSink(options))
            {
                await Write(second, "SECOND");
                TestAssert.True(File.Exists(Path.Combine(scope.RootDirectory, "App.log")), "Retention must not unlink another active writer.");
                await Write(first, "FIRST-AFTER");
                TestAssert.True(ReadActive(Path.Combine(scope.RootDirectory, "App.log")).Contains("FIRST-AFTER"),
                    "An active writer must remain readable by its path.");
            }
        }
        using (var last = new FileLogSink(options)) await Write(last, "LAST");
        TestAssert.Equal(1, Directory.GetFiles(scope.RootDirectory).Length, "Closed files become eligible for later retention cleanup.");
    }

    public static async Task ErrorTimeZoneAsync(bool mainUtc, bool errorUtc)
    {
        using var scope = new TestScope();
        var options = Options(scope.RootDirectory, FileRollingMode.None);
        options.UseUtcTimestamp = mainUtc;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff K";
        options.ErrorFile = new SPLogErrorFileOptions {
            FilePath = Path.Combine(scope.RootDirectory, "errors"), UseUtcTimestamp = errorUtc,
            FileRollingMode = FileRollingMode.None
        };
        var utc = new DateTime(2026, 1, 15, 20, 30, 0, DateTimeKind.Utc);
        var entry = new LogEntry(mainUtc ? utc : utc.ToLocalTime(), LogLevel.Error, "App", 1, "ZONE");
        using (var sink = new ErrorFileSink(options))
            await sink.WriteBatchAsync(new[] { entry }, CancellationToken.None);
        var expected = (errorUtc ? utc : utc.ToLocalTime()).ToString(options.TimestampFormat);
        var actual = File.ReadAllText(Path.Combine(scope.RootDirectory, "errors", "App.log"));
        TestAssert.True(actual.StartsWith("[" + expected + "]", StringComparison.Ordinal),
            $"Error timestamp must follow its own UTC flag. Expected {expected}; actual {actual}");
    }

    public static Task AtomicCreationAsync()
    {
        using var scope = new TestScope();
        string BuildPath(int number) => Path.Combine(scope.RootDirectory, $"claim-{number}.log");
        File.WriteAllText(BuildPath(0), "EXISTING-0");
        File.WriteAllText(BuildPath(1), "EXISTING-1");
        var sequence = 0;
        using (var file = LogFileWriter.Open(BuildPath, ref sequence, FileConflictMode.CreateNew, 4096))
        {
            file.Writer.WriteLine("NEW");
            TestAssert.Equal(2, sequence, "Atomic claim must advance past occupied files.");
        }
        TestAssert.Equal("EXISTING-0", File.ReadAllText(BuildPath(0)), "Existing content must not be changed.");
        TestAssert.Equal("EXISTING-1", File.ReadAllText(BuildPath(1)), "Retries must not append to collisions.");
        sequence = 0;
        try
        {
            using var unexpected = LogFileWriter.Open(i => Path.Combine(scope.RootDirectory, "missing", $"{i}.log"),
                ref sequence, FileConflictMode.CreateNew, 4096);
            throw new InvalidOperationException("Missing parent must fail, not retry forever.");
        }
        catch (DirectoryNotFoundException) { }
        TestAssert.Equal(0, sequence, "Non-collision failures must not advance the sequence.");
        File.WriteAllText(BuildPath(int.MaxValue), "LAST");
        sequence = int.MaxValue;
        try
        {
            using var unexpected = LogFileWriter.Open(BuildPath, ref sequence, FileConflictMode.CreateNew, 4096);
            throw new InvalidOperationException("Sequence exhaustion must fail instead of wrapping.");
        }
        catch (OverflowException) { }
        TestAssert.Equal("LAST", File.ReadAllText(BuildPath(int.MaxValue)), "Overflow must not change existing data.");
        return Task.CompletedTask;
    }

    public static Task CrossProcessCreateNewAsync()
    {
        using var scope = new TestScope();
        var children = new List<Process>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var assembly = Assembly.GetExecutingAssembly().Location;
                var arguments = $"--create-new-child \"{scope.RootDirectory}\" {i}";
#if NET8_0_OR_GREATER
                var info = new ProcessStartInfo("dotnet", $"\"{assembly}\" {arguments}");
#else
                var info = new ProcessStartInfo(assembly, arguments);
#endif
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                children.Add(Process.Start(info)!);
            }
            TestAssert.True(SpinWait.SpinUntil(() => Directory.GetFiles(scope.RootDirectory, "ready-*").Length == 4,
                TimeSpan.FromSeconds(30)), "Child processes must become ready.");
            File.WriteAllText(Path.Combine(scope.RootDirectory, "start"), "go");
            foreach (var child in children)
            {
                TestAssert.True(child.WaitForExit(30000), "Child process must exit without deadlock.");
                TestAssert.Equal(0, child.ExitCode, "Child failure: " + child.StandardError.ReadToEnd());
            }
            var files = Directory.GetFiles(Path.Combine(scope.RootDirectory, "logs"), "*.log");
            TestAssert.Equal(4, files.Length, "Each process must claim its own file.");
            var lines = files.SelectMany(File.ReadAllLines).ToArray();
            TestAssert.Equal(64, lines.Length, "All child messages must survive.");
            for (var child = 0; child < 4; child++)
                for (var message = 0; message < 16; message++)
                    TestAssert.Equal(1, lines.Count(line => line.EndsWith($"CHILD:{child}:{message:D2}", StringComparison.Ordinal)),
                        "Every child marker must appear once.");
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
                child.Dispose();
            }
        }
        return Task.CompletedTask;
    }

    public static void RunCreateNewChild(string directory, int index)
    {
        using var logger = SPLogFactory.Create(Options(Path.Combine(directory, "logs"), FileRollingMode.None));
        File.WriteAllText(Path.Combine(directory, $"ready-{index}"), "ready");
        if (!SpinWait.SpinUntil(() => File.Exists(Path.Combine(directory, "start")), TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Parent did not start child writers.");
        for (var message = 0; message < 16; message++) logger.Information($"CHILD:{index}:{message:D2}");
    }

    public static Task SharedCreateNewErrorAsync()
    {
        using var scope = new TestScope();
        var options = WithError(Options(Path.Combine(scope.RootDirectory, "Shared.log"), FileRollingMode.None));
        options.EnableFile = false;
        using (var first = SPLogFactory.Create(options))
        using (var second = SPLogFactory.Create(options))
        {
            first.Error("FIRST");
            first.Dispose();
            second.Error("SECOND");
        }
        var files = Directory.GetFiles(scope.RootDirectory, "*.log");
        TestAssert.Equal(1, files.Length, "Shared ErrorFile is one logical target, even with CreateNew.");
        var text = File.ReadAllText(files[0]);
        TestAssert.True(text.Contains("FIRST") && text.Contains("SECOND"), "Shared lease lifetime must preserve both writers.");
        return Task.CompletedTask;
    }

    public static Task AlignedInterpolationAsync()
    {
        using var scope = new TestScope();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var directory = scope.CreateSubdirectory(culture);
                string? missing = null;
                int? nullable = null;
                object boxed = 42;
                var amount = 12.5m;
                var expected = $"|{42,8:D4}|{42,-8:D4}|{amount,10:N2}|{missing,4}|{nullable,-3}|{boxed,6}|{12345,2}|{42,0}|";
                using (var logger = SPLogFactory.Create(Options(directory, FileRollingMode.None)))
                {
                    logger.Log(LogLevel.Information, $"|{42,8:D4}|{42,-8:D4}|{amount,10:N2}|{missing,4}|{nullable,-3}|{boxed,6}|{12345,2}|{42,0}|");
                    logger.Information($"|{42,8:D4}|{42,-8:D4}|{amount,10:N2}|{missing,4}|{nullable,-3}|{boxed,6}|{12345,2}|{42,0}|");
#if NET8_0_OR_GREATER
                    ReadOnlySpan<char> span = "xy".AsSpan();
                    logger.Log(LogLevel.Information, $"|{span,5}|{span,-5}|{span,5:ignored}|{span:ignored}|");
#endif
                }
                var lines = File.ReadAllLines(Path.Combine(directory, "App.log"));
                TestAssert.True(lines[0].EndsWith(expected, StringComparison.Ordinal), "Log handler must match ordinary interpolation.");
                TestAssert.True(lines[1].EndsWith(expected, StringComparison.Ordinal), "Information string overload must remain compatible.");
#if NET8_0_OR_GREATER
                TestAssert.True(lines[2].EndsWith("|   xy|xy   |   xy|xy|", StringComparison.Ordinal), "Span alignment must compile and pad correctly.");
#endif
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        return Task.CompletedTask;
    }

#if NET8_0_OR_GREATER
    public static Task DisabledAlignmentAsync()
    {
        using var scope = new TestScope();
        var options = Options(scope.RootDirectory, FileRollingMode.None);
        options.MinimumLevel = LogLevel.Error;
        var evaluations = 0;
        int Evaluate() { evaluations++; return 42; }
        using (var logger = SPLogFactory.Create(options))
            logger.Log(LogLevel.Information, $"{Evaluate(),8:D4}");
        TestAssert.Equal(0, evaluations, "Disabled handler must skip argument evaluation.");
        TestAssert.Equal(0, Directory.GetFiles(scope.RootDirectory).Length, "Disabled log must not create a file.");
        return Task.CompletedTask;
    }
#endif

    private static Task Write(ILogSink sink, string message) => sink.WriteBatchAsync(
        new[] { new LogEntry(DateTime.Now, LogLevel.Error, "App", 1, message) }, CancellationToken.None).AsTask();

    private static string ReadActive(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static SPLogOptions WithError(SPLogOptions options)
    {
        options.ErrorFile = new SPLogErrorFileOptions {
            FilePath = options.FilePath, FileConflictMode = options.FileConflictMode,
            FileRollingMode = options.FileRollingMode, MaxFileSizeBytes = options.MaxFileSizeBytes,
            MaxRollingFiles = options.MaxRollingFiles
        };
        return options;
    }

    private static SPLogOptions Options(string directory, FileRollingMode rolling) => new() {
        Name = "App", EnableConsole = false, EnableFile = true, FilePath = directory,
        FileConflictMode = FileConflictMode.CreateNew, FileRollingMode = rolling,
        MaxRollingFiles = 1000, BatchSize = 1, FlushIntervalMs = 1
    };
}
