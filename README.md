# SPLog

SPLog is a lightweight logging DLL focused on performance, simplicity, and practical file logging for .NET applications.

GitHub repository: <https://github.com/cku3987/SPLog>

## Why SPLog

- Simple API
- Fast enough for high-volume application logging
- File logging and console logging out of the box
- External JSON configuration support
- Exception logging support
- Daily and hourly rolling
- `Append` and `CreateNew` file conflict handling

## Quick Start

```csharp
using SPLog;

using var logger = SPLogFactory.Create(options =>
{
    options.Name = "Core";
    options.EnableConsole = true;
    options.EnableFile = true;
    options.FilePath = "logs";
});

logger.Information("application started");
logger.Warning("response delay detected");

try
{
    RunProcess();
}
catch (Exception ex)
{
    logger.Error(ex, "process failed");
}
```

## External Configuration

```csharp
using var logger = SPLogFactory.CreateFromJsonFile("config/splog.core.json");
```

Example JSON:

```json
{
  "Name": "Core",
  "MinimumLevel": "Information",
  "EnableConsole": false,
  "EnableFile": true,
  "FilePath": "logs",
  "FileConflictMode": "Append",
  "FileRollingMode": "Hourly",
  "MaxFileSizeBytes": 52428800,
  "MaxRollingFiles": 500,
  "QueueCapacity": 8192,
  "BatchSize": 10,
  "FlushIntervalMs": 100,
  "FileBufferSize": 65536,
  "BlockWhenQueueFull": true
}
```

## File Naming Behavior

- `FilePath = "logs"` creates `<Name>.log` automatically
- Relative paths are resolved from `AppContext.BaseDirectory`
- Absolute paths are used as-is

Examples:

- `Daily + Append` -> `Core_20260331.log`
- `Daily + CreateNew` -> `Core_20260331.log`, then `Core_20260331_001.log`
- `Hourly + CreateNew` -> `Core_20260331_13.log`, then `Core_20260331_13_001.log`

## Validation Projects

This repository also includes validation helpers:

- `SPLog.Tests`
  Deterministic correctness checks
- `SPLog.StressRunner`
  Stress, smoke, and long-run validation

Recommended commands:

```powershell
dotnet build SPLog.sln -c Release -m:1
dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build
dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release -- --config codex\SPLog-StressRunner.sample.json
```

## Documentation

- English guide: `docs/SPLog-Guide-EN.html`
- Korean guide: `docs/SPLog-Guide-KO.html`
- Validation guide: `codex/SPLog-Validation-Guide-EN.md`

## Project Link

- GitHub: <https://github.com/cku3987/SPLog>
