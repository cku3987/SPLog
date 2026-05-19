# SPLog v1.2.1

## Highlights

- Changed file creation to happen on first actual write
- Prevents empty main log files when a logger is created but never writes
- Prevents empty shared error files when `ErrorFile` is configured but no matching error entry is written

## What Changed

### Lazy file creation

Main file logging and shared error file logging now open the file writer only when a log line must be written.

Before:

- creating a file logger could create an empty log file immediately
- configuring `ErrorFile` could create an empty error file immediately

Now:

- main log files are created when the first normal entry is written
- shared error files are created when the first entry matching `ErrorFile.MinimumLevel` is written
- `Information` or `Warning` entries do not create the shared error file when `ErrorFile.MinimumLevel = Error`

## Validation

Verified during release preparation:

- `dotnet build SPLog.sln -c Release -m:1`
- `dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release`
- `dotnet run --project SPLog.Net472.Verify\SPLog.Net472.Verify.csproj -c Release`
- `dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release -- --duration 00:00:02 --status 00:00:01 --producers 1 --payload 16 --burst 10 --pause-ms 1`

## Version

- NuGet package: `1.2.1`
- Planned Git tag: `v1.2.1`
