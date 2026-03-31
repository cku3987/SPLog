using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPLog;
using SPLog.StressRunner;

var arguments = ArgumentMap.Parse(args);
if (arguments.ShowHelp)
{
    PrintUsage();
    return 0;
}

var serializerOptions = CreateSerializerOptions();

if (!string.IsNullOrWhiteSpace(arguments.WriteSamplePath))
{
    var samplePath = ResolvePath(arguments.WriteSamplePath);
    var sampleDirectory = Path.GetDirectoryName(samplePath);
    if (!string.IsNullOrWhiteSpace(sampleDirectory))
    {
        Directory.CreateDirectory(sampleDirectory);
    }

    var sample = new StressRunnerConfiguration();
    File.WriteAllText(samplePath, JsonSerializer.Serialize(sample, serializerOptions), Encoding.UTF8);
    Console.WriteLine($"Sample configuration written to: {samplePath}");
    return 0;
}

var configuration = LoadConfiguration(arguments.ConfigPath, serializerOptions);
ApplyOverrides(configuration, arguments);

var runDirectory = PrepareRunDirectory(configuration);
var metricsCsvPath = Path.Combine(runDirectory, "metrics.csv");
var summaryPath = Path.Combine(runDirectory, "summary.json");
var logDirectory = Path.Combine(runDirectory, configuration.LogSubdirectory);

Directory.CreateDirectory(runDirectory);
Directory.CreateDirectory(logDirectory);
File.WriteAllText(metricsCsvPath, "TimestampUtc,ElapsedSeconds,Produced,Dropped,WorkingSetBytes,ManagedBytes\n", Encoding.UTF8);

var logOptions = configuration.LogOptions.CloneForStressRun(logDirectory);
var payload = new string('X', Math.Max(0, configuration.MessagePayloadLength));
var startedAt = DateTimeOffset.UtcNow;
var stopwatch = Stopwatch.StartNew();
using var cancellation = new CancellationTokenSource(configuration.Duration);
var producedMessages = 0L;
var droppedMessages = 0L;
var peakWorkingSetBytes = 0L;
var process = Process.GetCurrentProcess();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var logger = SPLogFactory.Create(logOptions);

Console.WriteLine("SPLog.StressRunner started.");
Console.WriteLine($"Run directory: {runDirectory}");
Console.WriteLine($"Planned duration: {configuration.Duration}");
Console.WriteLine($"Producer count: {configuration.ProducerCount}");
Console.WriteLine($"Log directory: {logDirectory}");

var producerTasks = Enumerable
    .Range(0, configuration.ProducerCount)
    .Select(index => Task.Run(() => ProduceLogs(index, logger, configuration, payload, cancellation.Token, () => Interlocked.Increment(ref producedMessages))))
    .ToArray();

var statusTask = Task.Run(async () =>
{
    while (!cancellation.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(configuration.StatusInterval, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        var drained = logger.DrainDroppedCount();
        if (drained > 0)
        {
            Interlocked.Add(ref droppedMessages, drained);
        }

        process.Refresh();
        UpdatePeak(ref peakWorkingSetBytes, process.WorkingSet64);
        var managedBytes = GC.GetTotalMemory(forceFullCollection: false);

        var snapshot = $"{DateTimeOffset.UtcNow:O},{stopwatch.Elapsed.TotalSeconds:F0},{Interlocked.Read(ref producedMessages)},{Interlocked.Read(ref droppedMessages)},{process.WorkingSet64},{managedBytes}{Environment.NewLine}";
        File.AppendAllText(metricsCsvPath, snapshot, Encoding.UTF8);

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] elapsed={stopwatch.Elapsed:c} produced={Interlocked.Read(ref producedMessages):N0} dropped={Interlocked.Read(ref droppedMessages):N0} workingSet={process.WorkingSet64 / (1024 * 1024)}MB");
    }
});

try
{
    await Task.WhenAll(producerTasks).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
}

cancellation.Cancel();

try
{
    await statusTask.ConfigureAwait(false);
}
catch (OperationCanceledException)
{
}

var finalDrained = logger.DrainDroppedCount();
if (finalDrained > 0)
{
    Interlocked.Add(ref droppedMessages, finalDrained);
}

logger.Dispose();
stopwatch.Stop();

var validatedLines = configuration.ValidateLineCountAfterRun
    ? CountMessageLines(logDirectory, configuration.MessagePrefix, configuration.CountOnlyMessagePrefixMatches)
    : -1;

var logFiles = Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly);
var totalLogBytes = logFiles.Select(static path => new FileInfo(path).Length).Sum();
process.Refresh();
UpdatePeak(ref peakWorkingSetBytes, process.WorkingSet64);

var summary = new StressRunSummary
{
    RunDirectory = runDirectory,
    StartedAt = startedAt,
    EndedAt = DateTimeOffset.UtcNow,
    PlannedDuration = configuration.Duration,
    ActualDuration = stopwatch.Elapsed,
    ProducerCount = configuration.ProducerCount,
    ProducedMessages = Interlocked.Read(ref producedMessages),
    DroppedMessages = Interlocked.Read(ref droppedMessages),
    ValidatedFileMessageLines = validatedLines,
    LogFileCount = logFiles.Length,
    TotalLogBytes = totalLogBytes,
    PeakWorkingSetBytes = Interlocked.Read(ref peakWorkingSetBytes),
    LineCountValidationPassed = !configuration.ValidateLineCountAfterRun || validatedLines == Interlocked.Read(ref producedMessages),
    DroppedMessageValidationPassed = !configuration.FailIfDroppedMessages || Interlocked.Read(ref droppedMessages) == 0
};

File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, serializerOptions), Encoding.UTF8);

Console.WriteLine();
Console.WriteLine("Run completed.");
Console.WriteLine($"Produced messages: {summary.ProducedMessages:N0}");
Console.WriteLine($"Dropped messages: {summary.DroppedMessages:N0}");
Console.WriteLine($"Validated file message lines: {summary.ValidatedFileMessageLines:N0}");
Console.WriteLine($"Log files: {summary.LogFileCount:N0}");
Console.WriteLine($"Total log bytes: {summary.TotalLogBytes:N0}");
Console.WriteLine($"Peak working set: {summary.PeakWorkingSetBytes / (1024 * 1024)}MB");
Console.WriteLine($"Summary file: {summaryPath}");
Console.WriteLine($"Metrics CSV: {metricsCsvPath}");

if (!summary.LineCountValidationPassed)
{
    Console.WriteLine("Validation failed: file message line count does not match the produced message count.");
}

if (!summary.DroppedMessageValidationPassed)
{
    Console.WriteLine("Validation failed: dropped messages were detected.");
}

return summary.LineCountValidationPassed && summary.DroppedMessageValidationPassed ? 0 : 1;

static void ProduceLogs(int producerIndex, SPLogger logger, StressRunnerConfiguration configuration, string payload, CancellationToken cancellationToken, Func<long> nextSequence)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        for (var i = 0; i < configuration.MessagesPerBurst && !cancellationToken.IsCancellationRequested; i++)
        {
            var sequence = nextSequence();
            logger.Information($"{configuration.MessagePrefix}SEQ={sequence}|P={producerIndex}|UTC={DateTime.UtcNow:O}|{payload}");
        }

        if (configuration.PausePerBurstMs > 0)
        {
            Task.Delay(configuration.PausePerBurstMs, cancellationToken).GetAwaiter().GetResult();
        }
        else
        {
            Thread.Yield();
        }
    }
}

static StressRunnerConfiguration LoadConfiguration(string? configPath, JsonSerializerOptions serializerOptions)
{
    if (string.IsNullOrWhiteSpace(configPath))
    {
        return new StressRunnerConfiguration();
    }

    var fullPath = ResolvePath(configPath);
    var json = File.ReadAllText(fullPath);
    return JsonSerializer.Deserialize<StressRunnerConfiguration>(json, serializerOptions)
           ?? throw new InvalidOperationException("Failed to deserialize stress runner configuration.");
}

static void ApplyOverrides(StressRunnerConfiguration configuration, ArgumentMap arguments)
{
    if (!string.IsNullOrWhiteSpace(arguments.Duration))
    {
        configuration.Duration = TimeSpan.Parse(arguments.Duration);
    }

    if (!string.IsNullOrWhiteSpace(arguments.StatusInterval))
    {
        configuration.StatusInterval = TimeSpan.Parse(arguments.StatusInterval);
    }

    if (arguments.ProducerCount is > 0)
    {
        configuration.ProducerCount = arguments.ProducerCount.Value;
    }

    if (arguments.MessagePayloadLength is >= 0)
    {
        configuration.MessagePayloadLength = arguments.MessagePayloadLength.Value;
    }

    if (arguments.MessagesPerBurst is > 0)
    {
        configuration.MessagesPerBurst = arguments.MessagesPerBurst.Value;
    }

    if (arguments.PausePerBurstMs is >= 0)
    {
        configuration.PausePerBurstMs = arguments.PausePerBurstMs.Value;
    }
}

static string PrepareRunDirectory(StressRunnerConfiguration configuration)
{
    var root = ResolvePath(configuration.OutputRoot);
    Directory.CreateDirectory(root);
    var runName = SanitizePathComponent(configuration.RunName);
    return Path.Combine(root, $"{runName}_{DateTime.Now:yyyyMMdd_HHmmss}");
}

static string ResolvePath(string path)
{
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(path, AppContext.BaseDirectory);
}

static string SanitizePathComponent(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var builder = new StringBuilder(value.Length);

    foreach (var ch in value)
    {
        builder.Append(invalidChars.Contains(ch) ? '_' : ch);
    }

    return string.IsNullOrWhiteSpace(builder.ToString()) ? "LongRun" : builder.ToString();
}

static long CountMessageLines(string logDirectory, string prefix, bool onlyPrefixMatches)
{
    var total = 0L;
    foreach (var file in Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
    {
        foreach (var line in File.ReadLines(file))
        {
            if (onlyPrefixMatches)
            {
                if (line.Contains(prefix, StringComparison.Ordinal))
                {
                    total++;
                }
            }
            else
            {
                total++;
            }
        }
    }

    return total;
}

static void UpdatePeak(ref long target, long candidate)
{
    long current;
    do
    {
        current = Interlocked.Read(ref target);
        if (candidate <= current)
        {
            return;
        }
    }
    while (Interlocked.CompareExchange(ref target, candidate, current) != current);
}

static JsonSerializerOptions CreateSerializerOptions()
{
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

static void PrintUsage()
{
    Console.WriteLine("SPLog.StressRunner usage:");
    Console.WriteLine("  --config <path>             Load a JSON configuration file.");
    Console.WriteLine("  --write-sample <path>       Write a sample configuration file and exit.");
    Console.WriteLine("  --duration <d.hh:mm:ss>     Override run duration. Default is 3.00:00:00.");
    Console.WriteLine("  --status <hh:mm:ss>         Override status interval.");
    Console.WriteLine("  --producers <count>         Override producer count.");
    Console.WriteLine("  --payload <length>          Override payload length.");
    Console.WriteLine("  --burst <count>             Override messages written per burst.");
    Console.WriteLine("  --pause-ms <ms>             Override pause between bursts.");
    Console.WriteLine("  --help                      Show this help.");
}
