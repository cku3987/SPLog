# SPLog v1.2.0

## Highlights

- Added optional shared error file logging
- Allows each logger to keep its own main log while `Error` and `Critical` entries are also copied to one common error file
- Added JSON configuration support for the new `ErrorFile` option
- Updated English and Korean guides, README, and sample JSON

## What Changed

### Shared error file

`SPLogOptions` now supports:

```csharp
options.ErrorFile = new SPLogErrorFileOptions
{
    FilePath = "logs/error.log",
    MinimumLevel = LogLevel.Error,
    FileRollingMode = FileRollingMode.Daily
};
```

When configured:

- normal logs continue to write to the logger's main targets
- `Error` and `Critical` entries are also written to the configured error file
- multiple logger instances can share the same error file by using the same full `ErrorFile.FilePath`

### Error file options

Added `SPLogErrorFileOptions`:

- `FilePath`
- `MinimumLevel`
- `UseUtcTimestamp`
- `FileConflictMode`
- `FileRollingMode`
- `MaxFileSizeBytes`
- `MaxRollingFiles`
- `FileBufferSize`

## Validation

Verified during release preparation:

- `dotnet build SPLog.sln -c Release -m:1`
- `dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build`
- `dotnet run --project SPLog.Net472.Verify\SPLog.Net472.Verify.csproj -c Release --no-build`
- `dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release --no-build -- --duration 00:00:03 --status 00:00:01`

## Version

- NuGet package: `1.2.0`
- Git tag: `v1.2.0`
