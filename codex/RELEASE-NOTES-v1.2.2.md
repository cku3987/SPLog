# SPLog v1.2.2

## Highlights

- Prevents lost entries when independent `Append` loggers write to the same main file within one process
- Keeps the writer open until the last logger using that file is disposed
- Preserves `CreateNew` file creation and rolling behavior

## What Changed

Main file logging now shares the existing path-based file target used by error logging. Compatible `Append` loggers serialize their writes and share rolling state. A logger with conflicting file target settings receives an explicit error during creation. Partially created sinks are released if logger creation fails.

The change coordinates loggers in one process. It does not coordinate separate processes or establish the cause of NUL bytes observed in an EOL log sample.

## Validation

- Before the fix, the concurrent main-file regression kept 1,000 of 4,000 entries; after the fix it kept all 4,000.
- All 21 correctness tests passed.
- Both `net8.0` and `netstandard2.0` builds passed.
- The .NET Framework 4.7.2 verification passed. In a separate .NET Framework stress check, four independent loggers kept all 8,000 entries with no NUL bytes.

## Version

- NuGet package: `1.2.2`
- Git tag: `v1.2.2`
