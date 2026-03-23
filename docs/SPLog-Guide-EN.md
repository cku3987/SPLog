# SPLog User Guide

## Overview

SPLog is a lightweight logging DLL for fast, isolated logging per application part.

Recommended pattern 1: application-lifetime logger

```csharp
public static class AppLog
{
    public static SPLogger Core { get; private set; } = null!;

    public static void Initialize()
    {
        Core = SPLogFactory.Create(options =>
        {
            options.Name = "Core";
            options.EnableFile = true;
            options.FilePath = "logs";
        });
    }

    public static void Shutdown()
    {
        Core.Dispose();
    }
}
```

Use this when the logger should live for the whole application and be reused from many places.

Recommended pattern 2: short-lived logger

```csharp
using var coreLog = SPLogFactory.Create(options =>
{
    options.Name = "Core";
    options.EnableConsole = true;
    options.EnableFile = true;
    options.FilePath = "logs";
});
```

Why `using var` is recommended:

- `SPLogger` holds background resources.
- You should call `Dispose()` when logging is finished.
- The easiest and safest way is `using var`, because it automatically calls `Dispose()` at the end of the scope.
- If you do not dispose the logger, some queued log entries may remain in memory and may not be flushed to the file.

When should you call `Dispose()`:

- Call it when that logger will no longer be used.
- In practice, this usually means right before program shutdown, when a work scope finishes, or when the owning object is being destroyed.
- If one logger lives for the whole application, dispose it once at application shutdown.
- If a logger is only used in a short scope, `using var` is the safest pattern.

Example:

```csharp
void Run()
{
    using var logger = SPLogFactory.Create(options =>
    {
        options.Name = "Core";
        options.EnableFile = true;
        options.FilePath = "logs";
    });

    logger.Information($"start");
    logger.Information($"end");
} // automatic Dispose here
```

Do not dispose while you still plan to use the logger:

```csharp
var logger = SPLogFactory.Create();
logger.Information($"start");
logger.Dispose();
logger.Information($"this should not be written");
```

Recommended pattern with external config:

```csharp
using var coreLog = SPLogFactory.CreateFromJsonFile("config/splog.core.json");
```

## Exception Logging

```csharp
try
{
    ProcessRequest();
}
catch (Exception ex)
{
    coreLog.Error(ex, "request failed");
}
```

Why exception logging matters:

- `logger.Error($"failed: {ex}")` and `logger.Error(ex, "failed")` are not the same thing.
- If developers put exceptions into strings manually, the format becomes inconsistent.
- The dedicated exception overload keeps the log format consistent across the project.

What SPLog writes for exceptions:

- Your custom message
- Exception type
- Exception message
- Stack trace
- Inner exception chain

Where exception logs are written:

- To the same console output, if console logging is enabled
- To the same file output, if file logging is enabled
- In short, exception logs use the same logger targets as normal logs

Recommended exception methods:

- `logger.Warning(ex, "...")`
- `logger.Error(ex, "...")`
- `logger.Critical(ex, "...")`

## Logging String Examples

Basic string:

```csharp
logger.Information("application started");
```

String variable:

```csharp
var message = "network connected";
logger.Information(message);
```

Interpolated string:

```csharp
var userId = 1201;
logger.Information($"user connected: {userId}");
```

Multiple values:

```csharp
var ip = "10.0.0.1";
var port = 443;
logger.Information($"connected to {ip}:{port}");
```

Warning and error text:

```csharp
logger.Warning("response delay detected");
logger.Error("request failed");
```

Exception logging:

```csharp
try
{
    RunProcess();
}
catch (Exception ex)
{
    logger.Error(ex, "process failed");
}
```

## External Config

```csharp
using var logger = SPLogFactory.CreateFromJsonFile("docs/splog.sample.json");
```

You can also save the current options to JSON:

```csharp
var options = new SPLogOptions
{
    Name = "Core",
    EnableFile = true,
    FilePath = "logs"
};

SPLogConfiguration.SaveToJsonFile(options, "config/splog.core.json");
```

If your goal is to update an existing options object in memory, use `Update` instead of `Save`:

```csharp
SPLogConfiguration.Update(options);
SPLogConfiguration.UpdateFromJsonFile(options, "config/splog.core.json");
```

Important difference:

- `SPLogFactory.Create(options => { ... })` uses a temporary options object inside that lambda.
- You do not hold that object directly after logger creation unless you created a separate `SPLogOptions` variable yourself.
- `Update(...)` and `UpdateFromJsonFile(...)` are for cases where you already have your own `SPLogOptions` object in a variable.

Example:

```csharp
var options = new SPLogOptions();
SPLogConfiguration.UpdateFromJsonFile(options, "config/splog.core.json");

using var logger = SPLogFactory.Create(options);
```

When you save, SPLog first normalizes and validates the option values, then writes that normalized version to JSON.
The same normalized values are also copied back into the original `SPLogOptions` instance in memory.
That means the `options` object you already have is updated to the saved, normalized state.

## File Path Rules

- If `FilePath` is a folder such as `logs`, SPLog automatically creates `<Name>.log` inside it.
- Relative paths are resolved from the executable folder: `AppContext.BaseDirectory`.
- Absolute paths such as `D:\Logs\custom.log` are used exactly as given.

Examples:

- `FilePath = "logs"` and `Name = "Core"` -> `logs/Core_20260313.log`
- `FilePath = "logs"` and `Name = "Network"` -> `logs/Network_20260313.log`
- `FilePath = @"D:\Logs\custom.log"` -> `D:\Logs\custom_20260313.log`

## Rolling Behavior

- `Daily`: `Core_20260313.log`
- `Hourly`: `Core_20260313_14.log`
- Size rolling on top of time rolling: `Core_20260313_14_001.log`
- `FileConflictMode = Append`: if the current target file already exists, SPLog continues writing into that file.
- `FileConflictMode = CreateNew`: if the current target file already exists, SPLog creates a new distinct file when the logger starts.

Examples with `Name = "Core"` and `FilePath = "logs"`:

- `Daily + Append` -> first start: `Core_20260313.log`, next start on the same day: still `Core_20260313.log`
- `Daily + CreateNew` -> first start: `Core_20260313.log`, next start on the same day: `Core_20260313_001.log`, then `Core_20260313_002.log`
- `Hourly + CreateNew` -> first start in that hour: `Core_20260313_14.log`, next start in the same hour: `Core_20260313_14_001.log`
- If size rolling overlaps with `CreateNew`, the numbering continues. Example: start with `Core_20260313_001.log`, then if that file exceeds `MaxFileSizeBytes`, SPLog rolls to `Core_20260313_002.log`, then `Core_20260313_003.log`.
- If size rolling overlaps with `Append`, SPLog appends to the current file first and then rolls to the next numbered file when the size limit is reached.

## SPLogOptions

| Option | Default | Choices | Detailed description |
|---|---:|---|---|
| `Name` | `SPLog` | Any non-empty text | This is the logger name written into each log line. It is also used as the automatic file name when `FilePath` is a folder. Example: if `Name = "Core"` and `FilePath = "logs"`, the file starts as `logs/Core.log`. |
| `MinimumLevel` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` | Decides which log levels are kept. `Trace` keeps everything. `Debug` is useful during development. `Information` is the usual default for normal app events. `Warning` keeps only suspicious or problematic events. `Error` keeps only failures. `Critical` keeps only severe failures. `None` disables all logging. |
| `UseUtcTimestamp` | `false` | `true`, `false` | When `false`, logs use local machine time, which is easier to read on one machine. When `true`, logs use UTC, which is better when several servers or regions write logs and you need one common time standard. |
| `IncludeThreadId` | `true` | `true`, `false` | Adds the managed thread ID to each line. Keep this `true` when multiple tasks or threads run at the same time and you may need to track which thread produced a message. Turn it `false` if you want slightly cleaner log lines and thread information is not useful. |
| `IncludeLoggerName` | `true` | `true`, `false` | Adds the logger name to each log line. Keep this `true` if you use more than one logger or want to know which subsystem produced the message. Turn it `false` only if you want shorter lines and already separate logs by file. |
| `EnableConsole` | `true` | `true`, `false` | Writes logs to the console window. Good during development, testing, or command-line tools. In Windows services or background apps, this may not be useful. |
| `EnableFile` | `false` | `true`, `false` | Writes logs to files. In most real applications this should be `true`. If both `EnableConsole` and `EnableFile` are `false`, logger creation fails because there is nowhere to write logs. |
| `FilePath` | `logs` | Folder path or full file path | This is the base log location. If you pass a folder path like `logs`, SPLog creates `<Name>.log` automatically. If you pass a full file path like `D:\Logs\custom.log`, SPLog uses that file name directly. Relative paths use the executable folder as the base. |
| `FileConflictMode` | `Append` | `Append`, `CreateNew` | Controls what happens when the current target file already exists. `Append` keeps writing to the existing file. `CreateNew` starts a new distinguishable file by adding a numbered suffix such as `_001`, `_002`. The first file still uses the normal base name, and the numbered suffix starts only when another file for the same period already exists. |
| `FileRollingMode` | `Daily` | `None`, `Daily`, `Hourly` | Controls time-based file splitting. `None` means no date or time suffix. `Daily` creates one logical file per day using `yyyyMMdd`. `Hourly` creates one logical file per hour using `yyyyMMdd_HH`. If you expect very high log volume, `Hourly` is usually easier to manage. |
| `MaxFileSizeBytes` | `10485760` | Any positive integer | Maximum file size before SPLog rolls to the next numbered file. Default is 10 MB. Larger values create fewer files but each file becomes heavier. Smaller values create more files but make upload, inspection, and cleanup easier. |
| `MaxRollingFiles` | `14` | Any positive integer | How many recent rolled files are kept. Older files are deleted automatically. Increase this if you want longer history on disk. Decrease it if disk space matters more than long retention. |
| `QueueCapacity` | `8192` | Any positive integer | Number of log entries that may wait in memory before background writing catches up. If this is too small, bursts may drop logs. If this is too large, memory usage rises. For normal desktop/server apps, the default is reasonable. |
| `BatchSize` | `10` | Any positive integer | Maximum number of log entries written together in one background batch. The default `10` is a practical balance for normal use. SPLog does not wait until all 10 entries exist; if fewer entries are available, it writes the smaller batch. Larger values usually improve write performance because the logger touches the file fewer times. |
| `FlushIntervalMs` | `100` | Any positive integer | Background flush interval in milliseconds. `100` means roughly every 0.1 seconds. Smaller values make logs appear on disk sooner, but increase I/O frequency. Larger values reduce I/O overhead, but logs stay in memory a little longer before reaching the file. |
| `FileBufferSize` | `65536` | `1024` or larger | Buffer size used by file writing. Bigger buffers can reduce repeated small writes and help performance. The default is already large enough for most cases. |
| `BlockWhenQueueFull` | `true` | `true`, `false` | Controls what happens when the queue is full. The default `true` means the app waits for queue space so logs are less likely to be lost. If you change it to `false`, the app keeps running without waiting, but some new logs may be dropped during heavy bursts. |

## Notes

- Always dispose each logger to flush remaining queued entries.
- `using var logger = ...` is the recommended pattern for this reason.
- Avoid multiple logger instances writing to the same file path unless shared append behavior is intentional.
- Current design uses one background worker per logger instance.
