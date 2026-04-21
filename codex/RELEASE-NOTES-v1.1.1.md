# SPLog v1.1.1

## Highlights

- Fixed queue backpressure handling that could terminate high-load runs
- Verified stress-run completion under bounded queue pressure
- Added a focused regression test for full-queue blocking behavior

## What Changed

### Async queue backpressure fix

`AsyncLogProcessor.Enqueue()` used `ChannelWriter.WriteAsync()` with immediate synchronous result access.

Under load, when the bounded queue was full, that path could throw:

- `System.InvalidOperationException: The asynchronous operation has not completed.`

The enqueue path now waits correctly when the queue is full instead of touching an incomplete `ValueTask`.

### Regression coverage

Added a dedicated internal test path to validate the intended behavior:

- when queue capacity is exhausted, producers block
- no messages are dropped
- no exception is thrown from the enqueue path

## Validation

Verified during release preparation:

- `dotnet restore SPLog.sln`
- `dotnet build SPLog.sln -c Release -m:1 --no-restore`
- `dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build`
- `dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release --no-build -- --duration 00:00:05 --status 00:00:02`
- `dotnet run --project SPLog.Net472.Verify\SPLog.Net472.Verify.csproj -c Release --no-build`
- package creation for `SPLog.1.1.1.nupkg`

Stress-run validation result:

- produced messages: `25970`
- dropped messages: `0`
- line-count validation: `passed`

## Version

- NuGet package: `1.1.1`
- Git tag: `v1.1.1`
