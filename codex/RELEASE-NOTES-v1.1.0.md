# SPLog v1.1.0

## Highlights

- Added `netstandard2.0` support for wider runtime compatibility
- Verified real `.NET Framework 4.7.2` reference, build, run, and file logging
- Kept `net8.0` as the primary modern target
- Updated validation tooling and README to reflect compatibility support

## What's New

### Wider target support

SPLog now ships with two target frameworks:

- `net8.0`
- `netstandard2.0`

This allows SPLog to be consumed by `.NET Framework 4.7.2` projects while keeping the modern `net8.0` target for newer applications.

### Real .NET Framework verification

A dedicated verification project was added:

- `SPLog.Net472.Verify`

This project references SPLog from a real `net472` console application and validates:

- root logger creation
- category logger usage
- exception logging
- file output generation
- queue sequence headers

### Compatibility adjustments

To support the broader target range, compatibility work was added around:

- `System.Threading.Channels`
- `System.Text.Json`
- guard helpers that were too new for older targets
- runtime-specific interpolation handler support

The interpolation handler optimization remains available on modern targets, while the general logging API stays usable on older frameworks.

## Validation

Verified during release preparation:

- `dotnet build SPLog.sln -c Release -m:1 --no-restore`
- `dotnet run --project SPLog.Tests\SPLog.Tests.csproj -c Release --no-build`
- `dotnet run --project SPLog.Net472.Verify\SPLog.Net472.Verify.csproj -c Release --no-build`
- package creation for `SPLog.1.1.0.nupkg`

The resulting NuGet package contains:

- `lib/net8.0/SPLog.dll`
- `lib/netstandard2.0/SPLog.dll`

## Version

- NuGet package: `1.1.0`
- Git tag: `v1.1.0`

