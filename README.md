# SPLog

SPLog is a lightweight logging DLL focused on performance, simplicity, and practical file logging for .NET applications.

GitHub repository: <https://github.com/cku3987/SPLog>

Targets:

- `net8.0`
- `netstandard2.0`  
  Usable from `.NET Framework 4.7.2` projects and newer compatible runtimes.

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
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
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

## Global Logger With Categories

If you want one global logger but still want subsystem names inside the log, create categories from the root logger:

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

coreLog.Information("core started");
networkLog.Warning("network delay detected");
socketLog.Error("socket closed");
```

This keeps one shared queue and one shared writer, while log lines are identified as `App.Core`, `App.Network`, and `App.Network.Socket`.

## External Configuration

```csharp
using var logger = SPLogFactory.CreateFromJsonFile("config/splog.core.json");
```

Example JSON:

```json
{
  "Name": "Core",
  "MinimumLevel": "Information",
  "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff",
  "EnableConsole": false,
  "EnableFile": true,
  "FilePath": "logs",
  "FileConflictMode": "Append",
  "FileRollingMode": "Hourly",
  "MaxFileSizeBytes": 10485760,
  "MaxRollingFiles": 100,
  "QueueCapacity": 8192,
  "BatchSize": 10,
  "FlushIntervalMs": 100,
  "FileBufferSize": 65536
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
  Stress, smoke, and long-run validation for one or many loggers at the same time
- `SPLog.Net472.Verify`
  Real `.NET Framework 4.7.2` reference/build/run verification

Recommended commands:

```powershell
dotnet build SPLog.sln -c Release -m:1
dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build
dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release -- --config codex\SPLog-StressRunner.sample.json
dotnet run --project SPLog.Net472.Verify\SPLog.Net472.Verify.csproj -c Release
```

The default stress sample includes:

- multiple logger scenarios running at the same time
- one root logger used directly and shared by multiple category scenarios
- additional independent loggers using separate file paths
- a 5-digit stress-run timestamp format for easier timing inspection
- a configuration that is safe to auto-load when launching from Visual Studio

## Documentation

- English guide: `docs/SPLog-Guide-EN.html`
- Korean guide: `docs/SPLog-Guide-KO.html`
- Validation guide: `codex/SPLog-Validation-Guide-EN.md`

## Project Link

- GitHub: <https://github.com/cku3987/SPLog>
