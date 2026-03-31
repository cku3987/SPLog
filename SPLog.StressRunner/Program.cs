using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPLog;
using SPLog.StressRunner;

const string DefaultConfigRelativePath = @"codex\SPLog-StressRunner.sample.json";

var arguments = ArgumentMap.Parse(args);
if (arguments.ShowHelp)
{
    PrintUsage();
    return 0;
}

var serializerOptions = CreateSerializerOptions();

if (!string.IsNullOrWhiteSpace(arguments.WriteSamplePath))
{
    var samplePath = ResolveInputPath(arguments.WriteSamplePath);
    var sampleDirectory = Path.GetDirectoryName(samplePath);
    if (!string.IsNullOrWhiteSpace(sampleDirectory))
    {
        Directory.CreateDirectory(sampleDirectory);
    }

    var sample = CreateSampleConfiguration();
    File.WriteAllText(samplePath, JsonSerializer.Serialize(sample, serializerOptions), Encoding.UTF8);
    Console.WriteLine($"Sample configuration written to: {samplePath}");
    return 0;
}

var (configuration, configurationSource) = LoadConfiguration(arguments.ConfigPath, serializerOptions);
ApplyOverrides(configuration, arguments);

var runDirectory = PrepareRunDirectory(configuration);
var metricsCsvPath = Path.Combine(runDirectory, "metrics.csv");
var summaryPath = Path.Combine(runDirectory, "summary.json");

Directory.CreateDirectory(runDirectory);
File.WriteAllText(metricsCsvPath, "TimestampUtc,ElapsedSeconds,TotalProduced,TotalDropped,WorkingSetBytes,ManagedBytes\n", Encoding.UTF8);

var scenarioConfigurations = BuildScenarioConfigurations(configuration);
ValidateScenarioConfigurations(scenarioConfigurations);

var runtimes = CreateRuntimes(scenarioConfigurations, runDirectory);

var startedAt = DateTimeOffset.UtcNow;
var stopwatch = Stopwatch.StartNew();
using var cancellation = new CancellationTokenSource(configuration.Duration);
var peakWorkingSetBytes = 0L;
var process = Process.GetCurrentProcess();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine("SPLog.StressRunner started.");
Console.WriteLine($"Run directory: {runDirectory}");
Console.WriteLine($"Configuration source: {configurationSource}");
Console.WriteLine($"Planned duration: {configuration.Duration}");
Console.WriteLine($"Status interval: {configuration.StatusInterval}");
Console.WriteLine($"Scenario count: {runtimes.Length}");

for (var i = 0; i < runtimes.Length; i++)
{
    var runtime = runtimes[i];
    Console.WriteLine($"  [{i + 1}] scenario={runtime.Scenario.ScenarioName} logger={runtime.Logger.Name} producers={runtime.Scenario.ProducerCount} target={runtime.ResolvedBaseFilePath}");
}

var producerTasks = runtimes
    .SelectMany(runtime => Enumerable
        .Range(0, runtime.Scenario.ProducerCount)
        .Select(producerIndex => Task.Run(() => ProduceLogs(runtime, producerIndex, cancellation.Token))))
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

        DrainDroppedMessages(runtimes);

        process.Refresh();
        UpdatePeak(ref peakWorkingSetBytes, process.WorkingSet64);
        var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        var totalProduced = runtimes.Sum(static runtime => runtime.ProducedMessages);
        var totalDropped = runtimes.Sum(static runtime => runtime.DroppedMessages);

        var snapshot = $"{DateTimeOffset.UtcNow:O},{stopwatch.Elapsed.TotalSeconds:F0},{totalProduced},{totalDropped},{process.WorkingSet64},{managedBytes}{Environment.NewLine}";
        File.AppendAllText(metricsCsvPath, snapshot, Encoding.UTF8);

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] elapsed={stopwatch.Elapsed:c} produced={totalProduced:N0} dropped={totalDropped:N0} workingSet={process.WorkingSet64 / (1024 * 1024)}MB");
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

DrainDroppedMessages(runtimes);

for (var i = 0; i < runtimes.Length; i++)
{
    runtimes[i].Dispose();
}

stopwatch.Stop();

process.Refresh();
UpdatePeak(ref peakWorkingSetBytes, process.WorkingSet64);

var scenarioSummaries = runtimes
    .Select(BuildScenarioSummary)
    .ToList();

var allLogFiles = runtimes
    .SelectMany(runtime => GetScenarioLogFiles(runtime.ResolvedBaseFilePath))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

var summary = new StressRunSummary
{
    RunDirectory = runDirectory,
    StartedAt = startedAt,
    EndedAt = DateTimeOffset.UtcNow,
    PlannedDuration = configuration.Duration,
    ActualDuration = stopwatch.Elapsed,
    ProducerCount = runtimes.Sum(static runtime => runtime.Scenario.ProducerCount),
    ProducedMessages = scenarioSummaries.Sum(static summary => summary.ProducedMessages),
    DroppedMessages = scenarioSummaries.Sum(static summary => summary.DroppedMessages),
    ValidatedFileMessageLines = scenarioSummaries.Sum(static summary => summary.ValidatedFileMessageLines),
    LogFileCount = allLogFiles.Length,
    TotalLogBytes = allLogFiles.Select(static path => new FileInfo(path).Length).Sum(),
    PeakWorkingSetBytes = Interlocked.Read(ref peakWorkingSetBytes),
    LineCountValidationPassed = scenarioSummaries.All(static summary => summary.LineCountValidationPassed),
    DroppedMessageValidationPassed = scenarioSummaries.All(static summary => summary.DroppedMessageValidationPassed),
    ScenarioSummaries = scenarioSummaries
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

for (var i = 0; i < summary.ScenarioSummaries.Count; i++)
{
    var scenarioSummary = summary.ScenarioSummaries[i];
    Console.WriteLine($"  Scenario={scenarioSummary.ScenarioName} logger={scenarioSummary.LoggerName} produced={scenarioSummary.ProducedMessages:N0} dropped={scenarioSummary.DroppedMessages:N0} validatedLines={scenarioSummary.ValidatedFileMessageLines:N0}");
}

if (!summary.LineCountValidationPassed)
{
    Console.WriteLine("Validation failed: at least one scenario did not match file line counts.");
}

if (!summary.DroppedMessageValidationPassed)
{
    Console.WriteLine("Validation failed: dropped messages were detected in at least one scenario.");
}

return summary.LineCountValidationPassed && summary.DroppedMessageValidationPassed ? 0 : 1;

static void ProduceLogs(StressLoggerRuntime runtime, int producerIndex, CancellationToken cancellationToken)
{
    var scenario = runtime.Scenario;
    var payload = new string('X', Math.Max(0, scenario.MessagePayloadLength));

    while (!cancellationToken.IsCancellationRequested)
    {
        for (var i = 0; i < scenario.MessagesPerBurst && !cancellationToken.IsCancellationRequested; i++)
        {
            var scenarioSequence = runtime.NextSequence();
            var callUtc = DateTime.UtcNow;
            runtime.Logger.Information($"{scenario.MessagePrefix}SEQ={scenarioSequence}|SCENARIO={scenario.ScenarioName}|LOGGER={runtime.Logger.Name}|P={producerIndex}|CALLUTC={callUtc:O}|{payload}");
        }

        if (scenario.PausePerBurstMs > 0)
        {
            Task.Delay(scenario.PausePerBurstMs, cancellationToken).GetAwaiter().GetResult();
        }
        else
        {
            Thread.Yield();
        }
    }
}

static (StressRunnerConfiguration Configuration, string Source) LoadConfiguration(string? configPath, JsonSerializerOptions serializerOptions)
{
    if (!string.IsNullOrWhiteSpace(configPath))
    {
        var fullPath = ResolveExistingInputPath(configPath);
        var json = File.ReadAllText(fullPath);
        var loaded = JsonSerializer.Deserialize<StressRunnerConfiguration>(json, serializerOptions)
                     ?? throw new InvalidOperationException("Failed to deserialize stress runner configuration.");
        return (loaded, $"config file: {fullPath}");
    }

    var defaultConfigPath = TryResolveExistingInputPath(DefaultConfigRelativePath);
    if (!string.IsNullOrWhiteSpace(defaultConfigPath))
    {
        var json = File.ReadAllText(defaultConfigPath);
        var loaded = JsonSerializer.Deserialize<StressRunnerConfiguration>(json, serializerOptions)
                     ?? throw new InvalidOperationException("Failed to deserialize stress runner configuration.");
        return (loaded, $"default config file: {defaultConfigPath}");
    }

    return (new StressRunnerConfiguration(), "built-in defaults");
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

    var scenarios = BuildScenarioConfigurations(configuration);

    if (arguments.ProducerCount is > 0)
    {
        for (var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].ProducerCount = arguments.ProducerCount.Value;
        }
    }

    if (arguments.MessagePayloadLength is >= 0)
    {
        for (var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].MessagePayloadLength = arguments.MessagePayloadLength.Value;
        }
    }

    if (arguments.MessagesPerBurst is > 0)
    {
        for (var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].MessagesPerBurst = arguments.MessagesPerBurst.Value;
        }
    }

    if (arguments.PausePerBurstMs is >= 0)
    {
        for (var i = 0; i < scenarios.Count; i++)
        {
            scenarios[i].PausePerBurstMs = arguments.PausePerBurstMs.Value;
        }
    }
}

static List<StressLoggerScenarioConfiguration> BuildScenarioConfigurations(StressRunnerConfiguration configuration)
{
    if (configuration.LoggerScenarios.Count > 0)
    {
        return configuration.LoggerScenarios;
    }

    return
    [
        new StressLoggerScenarioConfiguration
        {
            ScenarioName = configuration.RunName,
            ProducerCount = configuration.ProducerCount,
            MessagesPerBurst = configuration.MessagesPerBurst,
            PausePerBurstMs = configuration.PausePerBurstMs,
            MessagePayloadLength = configuration.MessagePayloadLength,
            ValidateLineCountAfterRun = configuration.ValidateLineCountAfterRun,
            FailIfDroppedMessages = configuration.FailIfDroppedMessages,
            CountOnlyMessagePrefixMatches = configuration.CountOnlyMessagePrefixMatches,
            MessagePrefix = configuration.MessagePrefix,
            LogOptions = configuration.LogOptions
        }
    ];
}

static void ValidateScenarioConfigurations(IReadOnlyList<StressLoggerScenarioConfiguration> scenarios)
{
    if (scenarios.Count == 0)
    {
        throw new InvalidOperationException("At least one logger scenario is required.");
    }

    var duplicateScenarioNames = scenarios
        .GroupBy(static scenario => scenario.ScenarioName, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(group => group.Count() > 1);

    if (duplicateScenarioNames is not null)
    {
        throw new InvalidOperationException($"Scenario names must be unique. Duplicate: {duplicateScenarioNames.Key}");
    }

    var duplicatePrefixes = scenarios
        .GroupBy(static scenario => scenario.MessagePrefix, StringComparer.Ordinal)
        .FirstOrDefault(group => group.Count() > 1);

    if (duplicatePrefixes is not null)
    {
        throw new InvalidOperationException($"MessagePrefix values must be unique for validation. Duplicate: {duplicatePrefixes.Key}");
    }

    for (var i = 0; i < scenarios.Count; i++)
    {
        var scenario = scenarios[i];

        if (string.IsNullOrWhiteSpace(scenario.ScenarioName))
        {
            throw new InvalidOperationException("ScenarioName is required.");
        }

        if (string.IsNullOrWhiteSpace(scenario.MessagePrefix))
        {
            throw new InvalidOperationException($"MessagePrefix is required for scenario '{scenario.ScenarioName}'.");
        }

        if (scenario.SharedLoggerKey is not null && string.IsNullOrWhiteSpace(scenario.SharedLoggerKey))
        {
            throw new InvalidOperationException($"SharedLoggerKey cannot be blank for scenario '{scenario.ScenarioName}'.");
        }

        if (scenario.CategoryPath is not null && string.IsNullOrWhiteSpace(scenario.CategoryPath))
        {
            throw new InvalidOperationException($"CategoryPath cannot be blank for scenario '{scenario.ScenarioName}'.");
        }

        if (scenario.ProducerCount <= 0)
        {
            throw new InvalidOperationException($"ProducerCount must be greater than zero for scenario '{scenario.ScenarioName}'.");
        }

        if (scenario.MessagesPerBurst <= 0)
        {
            throw new InvalidOperationException($"MessagesPerBurst must be greater than zero for scenario '{scenario.ScenarioName}'.");
        }

        if (scenario.MessagePayloadLength < 0)
        {
            throw new InvalidOperationException($"MessagePayloadLength cannot be negative for scenario '{scenario.ScenarioName}'.");
        }
    }
}

static StressLoggerRuntime[] CreateRuntimes(
    IReadOnlyList<StressLoggerScenarioConfiguration> scenarios,
    string runDirectory)
{
    var contexts = new Dictionary<string, SharedLoggerContext>(StringComparer.OrdinalIgnoreCase);
    var runtimes = new StressLoggerRuntime[scenarios.Count];

    for (var i = 0; i < scenarios.Count; i++)
    {
        runtimes[i] = CreateRuntime(scenarios[i], runDirectory, contexts);
    }

    return runtimes;
}

static StressLoggerRuntime CreateRuntime(
    StressLoggerScenarioConfiguration scenario,
    string runDirectory,
    IDictionary<string, SharedLoggerContext> contexts)
{
    var contextKey = string.IsNullOrWhiteSpace(scenario.SharedLoggerKey)
        ? $"__independent__{scenario.ScenarioName}"
        : scenario.SharedLoggerKey.Trim();

    if (!contexts.TryGetValue(contextKey, out var context))
    {
        var rootOptions = scenario.LogOptions.CloneForStressRun(runDirectory);
        var resolvedBaseFilePath = scenario.LogOptions.GetResolvedBaseFilePath(runDirectory);
        var rootLogger = SPLogFactory.Create(rootOptions);
        context = new SharedLoggerContext(contextKey, rootOptions, resolvedBaseFilePath, rootLogger);
        contexts[contextKey] = context;
    }
    else
    {
        ValidateSharedLoggerCompatibility(context, scenario, runDirectory);
    }

    var logger = CreateScenarioLogger(context.RootLogger, scenario.CategoryPath);
    return new StressLoggerRuntime(
        scenario,
        context.RootOptions,
        context.ResolvedBaseFilePath,
        logger,
        context.RootLogger);
}

static void DrainDroppedMessages(IEnumerable<StressLoggerRuntime> runtimes)
{
    foreach (var runtime in runtimes)
    {
        runtime.DrainDroppedMessages();
    }
}

static StressLoggerScenarioSummary BuildScenarioSummary(StressLoggerRuntime runtime)
{
    var scenarioLogFiles = GetScenarioLogFiles(runtime.ResolvedBaseFilePath);
    var validatedLines = runtime.Scenario.ValidateLineCountAfterRun
        ? CountMessageLines(scenarioLogFiles, runtime.Scenario.MessagePrefix, runtime.Scenario.CountOnlyMessagePrefixMatches)
        : -1;

    return new StressLoggerScenarioSummary
    {
        ScenarioName = runtime.Scenario.ScenarioName,
        SharedLoggerKey = runtime.Scenario.SharedLoggerKey,
        CategoryPath = runtime.Scenario.CategoryPath,
        LoggerName = runtime.Logger.Name,
        ResolvedBaseFilePath = runtime.ResolvedBaseFilePath,
        ProducerCount = runtime.Scenario.ProducerCount,
        ProducedMessages = runtime.ProducedMessages,
        DroppedMessages = runtime.DroppedMessages,
        ValidatedFileMessageLines = validatedLines,
        LogFileCount = scenarioLogFiles.Length,
        TotalLogBytes = scenarioLogFiles.Select(static path => new FileInfo(path).Length).Sum(),
        LineCountValidationPassed = !runtime.Scenario.ValidateLineCountAfterRun || validatedLines == runtime.ProducedMessages,
        DroppedMessageValidationPassed = !runtime.Scenario.FailIfDroppedMessages || runtime.DroppedMessages == 0
    };
}

static string[] GetScenarioLogFiles(string resolvedBaseFilePath)
{
    var directory = Path.GetDirectoryName(resolvedBaseFilePath);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
    {
        return [];
    }

    var baseName = Path.GetFileNameWithoutExtension(resolvedBaseFilePath);
    var extension = Path.GetExtension(resolvedBaseFilePath);
    var pattern = $"{baseName}*{extension}";
    return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
}

static long CountMessageLines(IEnumerable<string> logFiles, string prefix, bool onlyPrefixMatches)
{
    var total = 0L;
    foreach (var file in logFiles)
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

static StressRunnerConfiguration CreateSampleConfiguration()
{
    return new StressRunnerConfiguration
    {
        RunName = "LongRun",
        OutputRoot = "artifacts/stress",
        LogSubdirectory = "logs",
        Duration = TimeSpan.FromDays(3),
        StatusInterval = TimeSpan.FromSeconds(30),
        LoggerScenarios =
        [
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "AppRoot",
                SharedLoggerKey = "AppRoot",
                ProducerCount = 2,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|APPROOT|",
                LogOptions = new SPLogOptions
                {
                    Name = "App",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "app",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            },
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "AppCore",
                SharedLoggerKey = "AppRoot",
                CategoryPath = "Core",
                ProducerCount = 4,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|CORE|",
                LogOptions = new SPLogOptions
                {
                    Name = "App",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "app",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            },
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "AppNetwork",
                SharedLoggerKey = "AppRoot",
                CategoryPath = "Network",
                ProducerCount = 2,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|NETWORK|",
                LogOptions = new SPLogOptions
                {
                    Name = "App",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "app",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            },
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "AppStorage",
                SharedLoggerKey = "AppRoot",
                CategoryPath = "Storage",
                ProducerCount = 2,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|STORAGE|",
                LogOptions = new SPLogOptions
                {
                    Name = "App",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "app",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            },
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "Audit",
                ProducerCount = 2,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|AUDIT|",
                LogOptions = new SPLogOptions
                {
                    Name = "Audit",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "audit",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            },
            new StressLoggerScenarioConfiguration
            {
                ScenarioName = "Metrics",
                ProducerCount = 2,
                MessagesPerBurst = 100,
                MessagePayloadLength = 128,
                MessagePrefix = "LONGRUN|METRICS|",
                LogOptions = new SPLogOptions
                {
                    Name = "Metrics",
                    MinimumLevel = LogLevel.Information,
                    TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff",
                    EnableConsole = false,
                    IncludeSequenceNumber = true,
                    EnableFile = true,
                    FilePath = "metrics",
                    FileConflictMode = FileConflictMode.Append,
                    FileRollingMode = FileRollingMode.Hourly,
                    MaxFileSizeBytes = 50 * 1024 * 1024,
                    MaxRollingFiles = 500,
                    QueueCapacity = 8192,
                    BatchSize = 10,
                    FlushIntervalMs = 100,
                    FileBufferSize = 65536
                }
            }
        ]
    };
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

static string ResolveInputPath(string path)
{
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(path, Directory.GetCurrentDirectory());
}

static string ResolveExistingInputPath(string path)
{
    if (Path.IsPathRooted(path))
    {
        return Path.GetFullPath(path);
    }

    foreach (var baseDirectory in EnumerateSearchBaseDirectories())
    {
        var candidate = Path.GetFullPath(path, baseDirectory);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return ResolveInputPath(path);
}

static string? TryResolveExistingInputPath(string path)
{
    if (Path.IsPathRooted(path))
    {
        var fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) ? fullPath : null;
    }

    foreach (var baseDirectory in EnumerateSearchBaseDirectories())
    {
        var candidate = Path.GetFullPath(path, baseDirectory);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static IEnumerable<string> EnumerateSearchBaseDirectories()
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var startDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directoryInfo = new DirectoryInfo(startDirectory);
        while (directoryInfo is not null)
        {
            if (seen.Add(directoryInfo.FullName))
            {
                yield return directoryInfo.FullName;
            }

            directoryInfo = directoryInfo.Parent;
        }
    }
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
    Console.WriteLine("  --write-sample <path>       Write a multi-scenario sample configuration file and exit.");
    Console.WriteLine("  --duration <d.hh:mm:ss>     Override run duration for all scenarios. Default is 3.00:00:00.");
    Console.WriteLine("  --status <hh:mm:ss>         Override status interval.");
    Console.WriteLine("  --producers <count>         Override producer count for all scenarios.");
    Console.WriteLine("  --payload <length>          Override payload length for all scenarios.");
    Console.WriteLine("  --burst <count>             Override messages written per burst for all scenarios.");
    Console.WriteLine("  --pause-ms <ms>             Override pause between bursts for all scenarios.");
    Console.WriteLine("  --help                      Show this help.");
}

static void ValidateSharedLoggerCompatibility(
    SharedLoggerContext context,
    StressLoggerScenarioConfiguration scenario,
    string runDirectory)
{
    var candidateOptions = scenario.LogOptions.CloneForStressRun(runDirectory);
    var candidateResolvedBaseFilePath = scenario.LogOptions.GetResolvedBaseFilePath(runDirectory);

    if (!SPLogOptionsMatch(context.RootOptions, candidateOptions))
    {
        throw new InvalidOperationException(
            $"All scenarios that share SharedLoggerKey '{context.Key}' must use the same root SPLogOptions.");
    }

    if (!string.Equals(context.ResolvedBaseFilePath, candidateResolvedBaseFilePath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"All scenarios that share SharedLoggerKey '{context.Key}' must resolve to the same file target.");
    }
}

static bool SPLogOptionsMatch(SPLogOptions left, SPLogOptions right)
{
    return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
           && left.MinimumLevel == right.MinimumLevel
           && left.UseUtcTimestamp == right.UseUtcTimestamp
           && string.Equals(left.TimestampFormat, right.TimestampFormat, StringComparison.Ordinal)
           && left.IncludeThreadId == right.IncludeThreadId
           && left.IncludeLoggerName == right.IncludeLoggerName
           && left.EnableConsole == right.EnableConsole
           && left.EnableFile == right.EnableFile
           && string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase)
           && left.FileConflictMode == right.FileConflictMode
           && left.FileRollingMode == right.FileRollingMode
           && left.MaxFileSizeBytes == right.MaxFileSizeBytes
           && left.MaxRollingFiles == right.MaxRollingFiles
           && left.QueueCapacity == right.QueueCapacity
           && left.BatchSize == right.BatchSize
           && left.FlushIntervalMs == right.FlushIntervalMs
           && left.FileBufferSize == right.FileBufferSize;
}

static SPLogger CreateScenarioLogger(SPLogger rootLogger, string? categoryPath)
{
    if (string.IsNullOrWhiteSpace(categoryPath))
    {
        return rootLogger;
    }

    var current = rootLogger;
    var segments = categoryPath.Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    for (var i = 0; i < segments.Length; i++)
    {
        current = current.CreateCategory(segments[i]);
    }

    return current;
}

file sealed class SharedLoggerContext(
    string key,
    SPLogOptions rootOptions,
    string resolvedBaseFilePath,
    SPLogger rootLogger)
{
    public string Key { get; } = key;

    public SPLogOptions RootOptions { get; } = rootOptions;

    public string ResolvedBaseFilePath { get; } = resolvedBaseFilePath;

    public SPLogger RootLogger { get; } = rootLogger;
}
