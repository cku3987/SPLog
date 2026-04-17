# SPLog v1.0.2

## Highlights

- Added category loggers that share one root pipeline
- Added configurable timestamp formatting with `TimestampFormat`
- Expanded long-run validation with multi-scenario stress testing
- Improved log ordering diagnostics for concurrent workloads
- Updated guides, samples, and validation coverage

## What's New

### Category loggers

You can now keep one root logger and still separate subsystems clearly:

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

This keeps one shared queue and one shared writer while log names appear as:

- `App`
- `App.Core`
- `App.Network`
- `App.Network.Socket`

### Timestamp formatting

SPLog now supports a fully configurable timestamp format:

```csharp
options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fffff";
```

This replaces the need for a fixed timestamp precision option and gives full control over layout and precision.

### Better concurrent log analysis

- Added optional queue sequence headers such as `[Q:123]`
- Improved file output so visible timestamps do not move backward in log order
- StressRunner now uses a 5-digit timestamp format by default for easier timing inspection

## Validation Improvements

Added and expanded validation projects:

- `SPLog.Tests`
  Deterministic correctness validation
- `SPLog.StressRunner`
  Stress, smoke, and long-run validation for multiple logger scenarios

The default StressRunner sample now covers:

- one root logger used directly
- multiple category loggers sharing the same root pipeline
- additional independent loggers writing to separate paths
- auto-loading of the default sample config in Visual Studio

## Documentation

Updated:

- README
- English and Korean guides
- sample JSON configuration
- validation guide

## Version

- NuGet package: `1.0.2`
- Git tag: `v1.0.2`

