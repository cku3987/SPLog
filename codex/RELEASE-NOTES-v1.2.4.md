# SPLog v1.2.4

## Fixes

- Continue cleanup of all output sinks when an earlier sink fails to dispose.
- Preserve a single cleanup exception and its original stack; aggregate multiple cleanup failures in sink order.
- Keep disposal idempotent after failures and release later shared-file leases.
- Clean up all acquired sinks after logger creation fails, retaining both creation and cleanup errors.
- Retain the v1.2.3 fix for sink errors leaving producers blocked: stop the worker, complete the queue, and propagate failures instead of hanging.
- Align package, assembly, and source version declarations at 1.2.4.

## Validation and Documentation

- Run the same 31 regression tests on .NET 8 and the .NET Framework 4.7.2 target.
- Add multi-sink cleanup, shared-lease release, and worker-plus-multiple-cleanup regression coverage.
- Verify .NET Framework JSON save/load and file logging with fresh output per run.
- Add Windows CI for builds, both test targets, JSON verification, concurrent logging, and tests against the actual NuGet package.
- Add manually triggered, version-checked NuGet publishing using GitHub OIDC Trusted Publishing rather than a permanent API key.
- Explain failure propagation, shutdown exceptions, no-retry behavior, and independent error reporting in both Korean and English HTML/Markdown guides.

## Compatibility

- Public API and package dependencies are unchanged; targets remain net8.0 and netstandard2.0.
- .NET Framework applications should update through NuGet and regenerate binding redirects, rather than copying SPLog.dll alone.
- A sink failure is not automatically recovered or retried. Entries already queued are not guaranteed persisted after failure.
- Cleanup can now report additional exceptions that were previously hidden. Use AggregateException.Flatten() to inspect nested causes.
- A write that never completes can still block shutdown. This release handles writes that throw, not unresponsive storage timeouts.
