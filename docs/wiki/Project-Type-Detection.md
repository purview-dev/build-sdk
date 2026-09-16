# Project Type Detection

During `Sdk.props` evaluation the SDK classifies every `.csproj` by reading the project filename, the
`Sdk` attribute, and on-disk markers. The resulting flags drive the defaults described throughout this
wiki.

## Detection flags

| Flag | Condition |
| -- | -- |
| `IsCSharpProject` | Project file extension is `.csproj`. |
| `IsTestProject` | Project name ends with `Test`/`Tests` and carries a recognised `TestingType` suffix. |
| `IsSharedTestingProject` | Project name is one of `SharedTestingFramework`, `SharedTestingInfrastructure`, `SharedTestingInfra`, `SharedTestingUtilities`, `SharedTestingUtils`, `SharedTestingLibrary`, `SharedTestingLib`, `SharedTestingHelpers`. |
| `IsSharedProject` | Project name is one of `Shared`, `SharedFramework`, `SharedInfrastructure`, `SharedInfra`, `SharedUtilities`, `SharedUtils`, `SharedLibrary`, `SharedLib`, `SharedHelpers`. |
| `IsContainerProject` | A `Dockerfile`, `dockerfile`, or `Dockerfile.dev` exists in the project directory. |
| `IsSdkProject` | An `Sdk` value is parsed from the `<Project>`/`<Import>` element. |
| `IsWebSdkProject` | `SdkProjectName` is `Microsoft.NET.Sdk.Web`. |
| `IsWorkerSdkProject` | `SdkProjectName` is `Microsoft.NET.Sdk.Worker`. |
| `IsAspireHostProject` | `SdkProjectName` starts with `Aspire.Sdk.Host` or `Aspire.AppHost.Sdk`. |
| `IsCLIProject` | Project name ends with `CLI`, `Console`, `CommandLine`, `QuickStart`, or `QuickStarts`. |
| `IsRoslynComponent` | `<IsRoslynComponent>true</IsRoslynComponent>` is declared in the project file. |
| `IsPackable` | `<IsPackable>true</IsPackable>` is declared in the project file. |
| `IsWebProject` | Marker used in SDK web-project behaviour. |

`TestingType` is the detected test category suffix from the project name (for example `Unit`,
`Integration`, `E2E`); `TargetProjectName` is the inferred non-test project name that a test project
targets.

## What each type gets

### Test projects (`IsTestProject=true`)

- `OutputType=Exe` when the testing framework is not `None`.
- `CollectCoverage=true` with coverage exclusions for framework and mocking packages.
- `IsPackable=false`, `IsPublishable=false`, `MaxCpuCount=0`.
- Disabled native instrumentation by default.
- An `[assembly: ExcludeFromCodeCoverage]` attribute.
- Automatic `ProjectReference` to the target project (resolved as `../<TargetProjectName>/`, sibling
  `src/` paths, and the shared testing project alongside).
- A `[Category: <TestingType>]` assembly attribute (TUnit) or `[Trait("Category", ...)]` (Xunit).

### Shared testing projects (`IsSharedTestingProject=true`)

- Test package references, but not the test runner or coverage settings.
- A `[Skip]` attribute (TUnit) so the shared assembly is never executed directly.
- `TUnit.Core` instead of the full `TUnit` package.

### Shared projects (`IsSharedProject=true`)

- Automatically referenced by sibling non-test projects via wildcard
  (`../Shared*/Shared*.csproj`); test projects automatically reference
  `../SharedTesting*/SharedTesting*.csproj`.

### Container projects

- `InvariantGlobalization=true`, `PublishAot=true`, `DockerDefaultTargetOS=Linux`,
  `DockerfileContext=..\..\`.
- Adds `Microsoft.VisualStudio.Azure.Containers.Tools.Targets`.

### Web SDK projects (`IsWebSdkProject=true`)

- Non-API web apps get `InterceptorsNamespaces` extended with
  `Microsoft.AspNetCore.OpenApi.Generated` for OpenAPI interceptors.

### CLI projects

- `OutputType=Exe`, `appsettings*.json` copied to the output directory.
- `CA1515` suppressed (nested settings classes on internal commands).

### Aspire host projects

- `OutputType=Exe` (when not a test/shared-testing project), `CA1515` suppressed.

### Roslyn components (`IsRoslynComponent=true`)

- Default `netstandard2.0` target, `LangVersion=latest`, `Nullable=enable`,
  `TreatWarningsAsErrors=true`, `EnforceExtendedAnalyzerRules=true`, `Deterministic=true`.
- Compiler-generated output under the intermediate directory, no dependency file.
- No symbol package by default; the PDB ships beside the analyzer in the package.
- SourceLink with `EmbedUntrackedSources=true`, telemetry excluded.
- See [Packaging](Packaging.md) for how the analyzer assets are packed.

### Packable projects (`IsPackable=true`)

- `GenerateDocumentationFile`, `IncludeSource`, `IncludeSymbols` (`.snupkg`), `PublishRepositoryUrl`,
  `EmbedUntrackedSources`, `DebugType=portable` defaults.
- See [Packaging](Packaging.md).

## Properties exposed to the compiler

The detection flags (plus many SDK properties) are exposed to Roslyn analyzers and source generators
via `CompilerVisibleProperty`, readable as `build_property.<PropertyName>`. The full list is in the
[Configuration Reference](Configuration-Reference.md).