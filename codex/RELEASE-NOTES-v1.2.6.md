# SPLog v1.2.6

## Fixes

- Fix concurrent CreateNew writers selecting the same file and overwriting log entries. Filename creation is now atomic, including first write, size rolling, and period changes. Occupied numbers are skipped; unrelated I/O failures and sequence exhaustion are reported.
- Protect every file held open by SPLog in the same process from retention cleanup, including on Linux. The retention limit may temporarily be exceeded until writers close and a later cleanup runs.
- Support interpolation alignment with optional format strings in the .NET 8 Log handler, including left/right padding, nulls, strings, and spans. Disabled levels still skip evaluating interpolation expressions.
- Convert error timestamps to local time when requested, even if the main logger records UTC. Both targets independently honor their UTC setting.

## Validation

- Expand coverage from 51 to 68 tests on .NET 8 and 67 on the .NET Framework 4.7.2 target.
- Add 32 simultaneous independent loggers, four separate writer processes, occupied rolling targets, active-file retention, atomic collision retries, and error/overflow checks.
- Check aligned output under en-US/fr-FR cultures and all four main/error UTC combinations. Windows and Linux CI use a non-UTC local time zone.
- Eight new runtime checks failed against the pre-fix implementation on Windows. The missing alignment overload was also reproduced as compiler error CS1739 before the fix.

## Compatibility and Limits

- No package dependency or target-framework changes: net8.0 and netstandard2.0 remain supported.
- Existing string logging APIs and intentional in-process ErrorFile sharing are unchanged. CreateNew shared error leases still use one logical writer.
- Atomic filename creation does not coordinate cross-process retention or Append writing. Use separate directories for independently managed processes; generated filename families must not overlap.
- Files remain lazily created on the first matching entry.
- The existing unresponsive-sink shutdown limitation is unchanged. No shutdown timeout or crash-proof persistence guarantee is added.
