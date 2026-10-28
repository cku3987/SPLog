# SPLog v1.2.5

## Fixes

- Prevent retention from deleting files belonging to similarly named logs, such as AppAudit when cleaning App.
- Share an exact filename parser between retention and restart sequence discovery for main files and shared error files.
- Recognize only the configured rolling mode's valid date/hour suffixes and canonical positive sequence numbers (_001 through _999, then _1000 and above). Preserve malformed names, backup files, and different rolling-mode families.
- Always retain the current writer's open file, even when its creation time is older than another candidate.
- Preserve case-distinct file and directory paths on Linux. Windows case aliases continue to share a writer, including sequence discovery and cleanup.
- Treat filename characters literally instead of interpolating them into directory search patterns.

## Regression Coverage

- Expand the suite from 31 to 51 tests.
- Cover all None/Daily/Hourly and Append/CreateNew combinations for main and error file retention.
- Cover file/directory casing, case-sensitive content isolation, Windows alias lifetime, literal filenames, sequence numbers above 999, and older active files.
- Add Linux source/package tests to CI and make them a prerequisite for publishing, alongside Windows .NET 8 and net472 tests.
- The original retention bug was reproduced by 12 new failing tests against the pre-fix source on Windows.

## Compatibility and Limits

- No public API or package dependency changes; targets remain net8.0 and netstandard2.0.
- Retention now leaves files from a different rolling mode untouched after settings change.
- Reserve each generated filename family for one logical log. For example, None-mode App and a distinct logger named App_001 collide by definition; use separate directories.
- Path comparison follows platform conventions, not per-volume case settings or symbolic-link identity.
- The documented unresponsive-sink shutdown limitation is unchanged. This release does not add a shutdown timeout or promise persistence after I/O failure.
