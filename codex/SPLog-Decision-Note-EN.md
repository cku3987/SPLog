# SPLog Decision Notes

This file keeps the main project decisions so future sessions can resume quickly.

## Main direction

- Prioritize performance and simplicity over framework-style extensibility.
- Keep the API easy to use for application-level logging.
- Prefer a small surface area with practical defaults.

## Recommended usage patterns

### Application-lifetime logger

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

### Short-lived scoped logger

```csharp
using var logger = SPLogFactory.Create(options =>
{
    options.Name = "Core";
    options.EnableFile = true;
    options.FilePath = "logs";
});
```

### Global logger with categories

```csharp
using var appLog = SPLogFactory.Create(options =>
{
    options.Name = "App";
    options.EnableFile = true;
    options.FilePath = "logs";
});

var coreLog = appLog.CreateCategory("Core");
var networkLog = appLog.CreateCategory("Network");
var socketLog = networkLog.CreateCategory("Socket");
```

## Lifecycle rules

- `Create()` starts logging immediately.
- Always dispose a logger when it will no longer be used.
- Global loggers should be disposed at app shutdown.
- Scoped loggers should usually use `using var`.
- Category loggers are lightweight handles that share the root logger's queue and writer.
- In a category setup, the root logger is the one that should normally be disposed at shutdown.
- Since 1.2.3, a sink failure completes the queue and releases blocked producers with an error; failed batches are not retried.
- Since 1.2.4, composite cleanup attempts every sink once, preserving single failures or aggregating multiple failures. Creation rollback also attempts every acquired sink.
- Dispose can throw. Stop producers before shutdown, handle errors at the owner boundary, and report failures through an independent fallback.

## Configuration rules

- `SPLogOptions` holds configuration only.
- `SPLogger` is the runtime logger instance.
- Updating an existing `SPLogOptions` object does not hot-reconfigure an already-created logger.
- To apply new settings: dispose the old logger, update options, create a new logger.

## File path rules

- Relative file paths resolve from `AppContext.BaseDirectory`.
- If `FilePath` is a folder path such as `logs`, SPLog creates `<Name>.log`.
- Absolute paths are used as-is.
- Since 1.2.5, shared path identity is ordinal-ignore-case on Windows and ordinal on other platforms; per-volume case detection and symlink identity are not provided.
- Retention and restart discovery share an exact filename parser. Only the current rolling mode's valid dates/hours and canonical positive D3 sequence suffixes are owned. Similar prefixes and malformed suffixes are preserved.
- Cleanup always reserves one slot for its open file. Different logical logs must not overlap generated filename families (for example, None-mode App versus App_001); use separate directories.

## Logging behavior

- Exception logging uses dedicated overloads such as `logger.Error(ex, "...")`.
- Supported rolling modes: `None`, `Daily`, `Hourly`.
- Supported file conflict modes: `Append`, `CreateNew`.
- `CreateNew` starts numbering only when a file for the same period already exists.
- Size rolling and `CreateNew` share the same numeric sequence.

## Current defaults

- `Name = "SPLog"`
- `MinimumLevel = Information`
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

## Removed or skipped options

- `MaxMessageLength` removed
- `IncludeScopes` removed
- `SingleFile` intentionally not added

## Documentation formats

- HTML guides
- Markdown guides
- RTF removed because of Korean encoding issues

## Current validation assets

- `SPLog.Tests` for deterministic checks
- `SPLog.StressRunner` for smoke, stress, and long-run validation
- `codex/SPLog-StressRunner.sample.json` for the default 3-day-style stress run template with one shared root logger (`App`) used directly, three categories under it, plus two additional independent loggers
