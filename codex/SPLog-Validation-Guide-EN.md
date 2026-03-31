# SPLog Validation Guide

## What was added

- `SPLog.Tests`: deterministic verification runner for correctness checks
- `SPLog.StressRunner`: long-running stress and line-count validation runner

## Recommended command order

### 1. Build

Use the solution build in single-node mode on this machine:

```powershell
dotnet build SPLog.sln -c Release -m:1
```

### 2. Deterministic validation

```powershell
dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build
```

Current coverage:

- Dispose flushes queued entries
- `BatchSize` does not require a full batch
- `MinimumLevel` filtering
- Exception logging output
- `Append` file reuse
- `CreateNew` file naming
- `CreateNew` plus size-rolling numbering
- `UpdateFromJsonFile` behavior

### 3. Short smoke run

```powershell
dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release --no-build -- --duration 00:00:05 --status 00:00:02 --producers 2 --payload 32 --burst 20
```

This confirms:

- logging stays alive under concurrent writers
- no dropped messages under the selected settings
- final file line count matches produced message count
- summary and metrics files are written

## 3-day long-run recommendation

Write a sample config first if needed:

```powershell
dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release -- --write-sample codex\SPLog-StressRunner.sample.generated.json
```

Then run the long test:

```powershell
dotnet run --project SPLog.StressRunner\SPLog.StressRunner.csproj -c Release -- --config codex\SPLog-StressRunner.sample.json
```

The sample config already uses:

- `Duration = 3.00:00:00`
- `StatusInterval = 00:00:30`
- file logging enabled
- hourly rolling
- line-count validation enabled
- dropped-message failure enabled

## Output files

Each stress run creates a timestamped run folder under:

```text
SPLog.StressRunner\bin\Release\net8.0\artifacts\stress\
```

Inside each run folder:

- `logs\` contains the actual log files
- `metrics.csv` contains periodic runtime snapshots
- `summary.json` contains the final validation summary

## What to watch in a real 3-day run

- `DroppedMessages` should stay `0`
- `LineCountValidationPassed` should be `true`
- `DroppedMessageValidationPassed` should be `true`
- `PeakWorkingSetBytes` should not keep climbing without reason
- log file count and total bytes should match expectations for the chosen rolling settings
