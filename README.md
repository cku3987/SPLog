# SPLog Decision Notes

This file is a working record of the decisions made during SPLog development so future sessions can resume quickly.

## Current recommended usage patterns

### 1. Application-lifetime global logger

This is the current primary pattern.

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

Summary:

- Create once at app startup
- Reuse globally
- Dispose once at app shutdown

### 2. Short-lived scoped logger

```csharp
using var logger = SPLogFactory.Create(options =>
{
    options.Name = "Core";
    options.EnableFile = true;
    options.FilePath = "logs";
});
```

Summary:

- Use inside a function or short work scope
- Automatically disposes when the scope ends

## Dispose rules

- `Create()` starts logging immediately.
- There is no separate `Start()`.
- Always call `Dispose()` when the logger will no longer be used.
- If you do not dispose it, queued log entries may not be flushed to file.
- Global loggers should be disposed at application shutdown.
- Short-lived loggers should usually use `using var`.

## Relationship between options and logger instances

- `SPLogOptions` only holds configuration values.
- `SPLogger` is the actual runtime logging object.
- `SPLogConfiguration.UpdateFromJsonFile(options, path)` updates an existing `SPLogOptions` instance.
- Updating an options object does not automatically reconfigure an already-created `SPLogger`.

To apply updated settings:

1. Dispose the current logger
2. Update the options object
3. Create a new logger from the updated options

## External configuration direction

Currently available APIs:

- `SPLogFactory.CreateFromJsonFile(path)`
- `SPLogConfiguration.LoadFromJson(json)`
- `SPLogConfiguration.LoadFromJsonFile(path)`
- `SPLogConfiguration.SaveToJson(options)`
- `SPLogConfiguration.SaveToJsonFile(options, path)`
- `SPLogConfiguration.Update(options)`
- `SPLogConfiguration.UpdateFromJson(options, json)`
- `SPLogConfiguration.UpdateFromJsonFile(options, path)`

Agreed save behavior:

- Saving first normalizes and validates the values
- The normalized values are written to JSON
- The same normalized values are copied back into the original `SPLogOptions` object in memory

## File path rules

- Relative paths are resolved from the executable folder
- The base path is `AppContext.BaseDirectory`
- Absolute paths are used as-is
- If `FilePath = "logs"`, SPLog automatically creates `<Name>.log`
- If `FilePath = @"D:\Logs\custom.log"`, SPLog uses that filename directly

Examples:

- `Name = "Core"`, `FilePath = "logs"` -> `logs/Core_20260313.log`
- `FilePath = @"D:\Logs\custom.log"` -> `D:\Logs\custom_20260313.log`

## Exception logging direction

Exceptions should use dedicated overloads.

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

Reasons:

- The format stays consistent
- Exception type, message, stack trace, and `InnerException` chain can be written cleanly
- Exception logs go to the same targets as normal logs

## Logging string usage

Common supported patterns:

```csharp
logger.Information("application started");

var message = "network connected";
logger.Information(message);

var userId = 1201;
logger.Information($"user connected: {userId}");

logger.Error("request failed");
logger.Error(ex, "request failed");
```

The earlier interpolation-handler-related call friction was resolved by adding normal string overloads.

## Rolling and file conflict handling

Current time-based rolling modes:

- `FileRollingMode.None`
- `FileRollingMode.Daily`
- `FileRollingMode.Hourly`

Current file conflict modes:

- `FileConflictMode.Append`
- `FileConflictMode.CreateNew`

Behavior rules:

- The first file always uses the normal base name
- `CreateNew` only starts adding `_001`, `_002`, and so on when a file for the same period already exists
- Size rolling and `CreateNew` share the same sequence numbering

Examples:

- `Daily + Append`
  - First start: `Core_20260313.log`
  - Next start on the same day: still `Core_20260313.log`
- `Daily + CreateNew`
  - First start: `Core_20260313.log`
  - Next start on the same day: `Core_20260313_001.log`
  - Next start after that: `Core_20260313_002.log`
- `CreateNew` plus size rollover
  - `Core_20260313_001.log`
  - `Core_20260313_002.log`
  - `Core_20260313_003.log`

## Current defaults

Current code defaults:

- `Name = "SPLog"`
- `MinimumLevel = Information`
- `UseUtcTimestamp = false`
- `IncludeThreadId = true`
- `IncludeLoggerName = true`
- `EnableConsole = true`
- `EnableFile = false`
- `FilePath = "logs"`
- `FileConflictMode = Append`
- `FileRollingMode = Daily`
- `MaxFileSizeBytes = 10485760`
- `MaxRollingFiles = 14`
- `QueueCapacity = 8192`
- `BatchSize = 10`
- `FlushIntervalMs = 100`
- `FileBufferSize = 65536`
- `BlockWhenQueueFull = true`

Intent behind current defaults:

- `BlockWhenQueueFull = true` to prefer log retention over dropping entries
- `BatchSize = 10` for better practical performance
- `BatchSize` means maximum batch size, not minimum queued count

## Removed or intentionally skipped options

- `MaxMessageLength` removed
- `IncludeScopes` removed
- `SingleFile` mode intentionally not added for the current project needs

## Documentation decisions

Current formats:

- HTML guides
- Markdown guides

RTF decision:

- Removed because of Korean encoding/display issues
- HTML is the Word-friendly replacement

Documentation direction:

- Beginner-friendly explanations
- Default values included
- All choice-based options explained
- Clear distinction between load/save/update configuration APIs
- Exception logging explained
- String logging examples included

## Main document paths

- [SPLog-Guide-KO.html](C:/Users/user/source/repos/SPLog/docs/SPLog-Guide-KO.html)
- [SPLog-Guide-EN.html](C:/Users/user/source/repos/SPLog/docs/SPLog-Guide-EN.html)
- [SPLog-Guide-KO.md](C:/Users/user/source/repos/SPLog/docs/SPLog-Guide-KO.md)
- [SPLog-Guide-EN.md](C:/Users/user/source/repos/SPLog/docs/SPLog-Guide-EN.md)
- [splog.sample.json](C:/Users/user/source/repos/SPLog/docs/splog.sample.json)

## Current build output

Release build output:

- [SPLog.dll](C:/Users/user/source/repos/SPLog/SPLog/bin/Release/net8.0/SPLog.dll)