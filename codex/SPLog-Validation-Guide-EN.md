# SPLog Validation Guide

## What was added

- `SPLog.Tests`: deterministic verification runner for correctness checks
- `SPLog.StressRunner`: long-running stress and line-count validation runner
- The default stress sample enables queue-order numbers (`[Q:n]`) so concurrent log ordering is easier to inspect.
- The default stress sample uses `yyyy-MM-dd HH:mm:ss.fffff` so timestamp differences are easier to inspect during concurrency tests.

## Recommended command order

### 1. Build

Use the solution build in single-node mode on this machine:

```powershell
dotnet build SPLog.sln -c Release -m:1
```

### 2. Deterministic validation

```powershell
dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release -f net8.0 --no-build
dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release -f net472 --no-build
```

Current coverage:

- The same 31 tests run on .NET 8 and the .NET Framework 4.7.2 target (Windows).
- Sink failure releases blocked producers and rejects later writes.
- Worker, drain, and multiple sink cleanup failures retain their causes.
- Composite cleanup releases later shared file leases even if an earlier sink fails.
- Repeated cleanup is a no-op, including after failures.
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
- multiple logger scenarios can run together when configured

## Package Validation and Publishing

The test and net472 verification projects normally reference the local SPLog project.
Set `SPLogPackageVersion` to verify the actual package instead, without editing source references:

```powershell
dotnet pack SPLog/SPLog.csproj -c Release --no-build -o artifacts/packages
$source = Join-Path $PWD 'artifacts/packages'
dotnet run --project SPLog.Tests -c Release -f net8.0 -p:SPLogPackageVersion=1.2.4 "-p:RestoreAdditionalProjectSources=$source"
dotnet run --project SPLog.Tests -c Release -f net472 -p:SPLogPackageVersion=1.2.4 "-p:RestoreAdditionalProjectSources=$source"
dotnet run --project SPLog.Net472.Verify -c Release -p:SPLogPackageVersion=1.2.4 "-p:RestoreAdditionalProjectSources=$source"
```

Use an isolated `RestorePackagesPath` when repacking an unpublished version locally; NuGet caches packages by version. Rebuild normally without these properties afterwards to restore project references.

GitHub `validate.yml` checks source and package builds on Windows for pushes and PRs.
`publish.yml` is dispatched manually from `main` with an existing version tag. It rejects version mismatches and tags outside main, reruns validation, then publishes the tested package through NuGet Trusted Publishing. No permanent API key is stored.

One-time setup on NuGet.org: trust owner `cku3987`, repository `SPLog`, workflow filename `publish.yml`, environment `nuget`, and only new versions of package `SPLog`. Set the GitHub repository variable `NUGET_USER` to the owning NuGet profile name (not email). Restrict the GitHub `nuget` environment to `main`.

Release sequence: update versions and release notes, commit and push, wait for validation, create the matching tag, dispatch `publish.yml` from main, verify the published package, then publish the GitHub release. Publishing is a deliberate action, not automatic on every push.

Reference: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing

## Stress Runner Configuration

If you launch `SPLog.StressRunner` from Visual Studio without `--config`, it now tries to auto-load:

```text
codex\SPLog-StressRunner.sample.json
```

If that file is not found, it falls back to built-in defaults.

The default sample is intentionally safe for normal validation:

- one root logger is used directly and also shared by multiple category scenarios
- additional independent loggers write to their own file paths
- multiple loggers still run at the same time
- Visual Studio `F5` uses the same safe sample unless you override it with `--config`

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
- multiple logger scenarios
- one shared root logger plus categories
- additional independent loggers with separate file paths
- hourly rolling
- line-count validation enabled
- dropped-message failure enabled

The sample scenario layout is:

- `AppRoot`
  Uses the root logger `App` directly
- `AppCore`
  Uses root logger `App` and category `Core`
- `AppNetwork`
  Uses root logger `App` and category `Network`
- `AppStorage`
  Uses root logger `App` and category `Storage`
- `Audit`
  Uses its own `audit` path
- `Metrics`
  Uses its own `metrics` path

## Output files

Each stress run creates a timestamped run folder under:

```text
SPLog.StressRunner\bin\Release\net8.0\artifacts\stress\
```

Inside each run folder:

- `logs\` contains the actual log files
- `metrics.csv` contains periodic runtime snapshots
- `summary.json` contains the final validation summary

In multi-scenario runs, `summary.json` also contains per-scenario results.

## What to watch in a real 3-day run

- `DroppedMessages` should stay `0`
- `LineCountValidationPassed` should be `true`
- `DroppedMessageValidationPassed` should be `true`
- `PeakWorkingSetBytes` should not keep climbing without reason
- log file count and total bytes should match expectations for the chosen rolling settings
