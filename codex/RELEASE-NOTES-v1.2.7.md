# SPLog v1.2.7

## License and Package Metadata

- Adopt the MIT License with copyright holder cku3987.
- Add the full LICENSE text to the repository and the NuGet package root.
- Declare PackageLicenseExpression=MIT and Copyright (c) 2026 cku3987 in package metadata.
- Add CI checks that reject packages with missing/incorrect license metadata or missing/changed embedded license text.
- Update README licensing information and package validation examples.

## Compatibility

- Keep the C# implementation, existing public logging APIs, and runtime behavior unchanged.
- Continue targeting net8.0 and netstandard2.0, including .NET Framework 4.7.2 consumers.
- No package dependency changes. Existing runtime regression coverage remains 68 tests on .NET 8 and 67 on the .NET Framework 4.7.2 target.
- Package and assembly versions advance to 1.2.7 / 1.2.7.0. Previously published package files are not replaced.
