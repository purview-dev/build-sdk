# Project Type Detection

This page documents the detection mechanics behind the policy described in
[Engineering Principles](Engineering-Principles.md).

During `Sdk.props` evaluation the SDK classifies every `.csproj` by reading the project filename, the
`Sdk` attribute, and on-disk markers. The resulting flags drive the defaults described throughout this
wiki.

## Detection flags

| Flag | Condition |
| -- | -- |
| `IsCSharpProject` | Project file extension is `.csproj`. |
| `IsTestProject` | Project name matches `^.*\.([\w|_]\w*)Tests?$` — a target name, a dot, then any word before `Test`/`Tests`. The captured word becomes `TestingType`. |
| `IsSharedTestingProject` | Project name is one of `SharedTestingFramework`, `SharedTestingInfrastructure`, `SharedTestingInfra`, `SharedTestingUtilities`, `SharedTestingUtils`, `SharedTestingLibrary`, `SharedTestingLib`, `SharedTestingHelpers`. |
| `IsSharedProject` | Project name is one of `Shared`, `SharedFramework`, `SharedInfrastructure`, `SharedInfra`, `SharedUtilities`, `SharedUtils`, `SharedLibrary`, `SharedLib`, `SharedHelpers`. |
| `IsContainerProject` | A `Dockerfile`, `dockerfile`, or `Dockerfile.dev` exists in the project directory. |
| `IsSdkProject` | An `Sdk` value is parsed from the `<Project>`/`<Import>` element. |
| `IsWebSdkProject` | `SdkProjectName` is `Microsoft.NET.Sdk.Web`. |
| `IsWorkerSdkProject` | `SdkProjectName` is `Microsoft.NET.Sdk.Worker`. |
| `IsAspireHostProject` | `SdkProjectName` starts with `Aspire.Sdk.Host` or `Aspire.AppHost.Sdk`. |
| `IsCLIProject` | Project name ends with `CLI`, `Console`, `CommandLine`, `QuickStart`, or `QuickStarts`. |
| `IsBlazorWebAssemblyProject` | `SdkProjectName` is `Microsoft.NET.Sdk.BlazorWebAssembly`. |
| `IsRazorClassLibraryProject` | `SdkProjectName` is `Microsoft.NET.Sdk.Razor`. |
| `IsBffProject` | A web SDK project whose name ends with `Bff` (case-sensitive), or that sets `IsBffProject=true`. `IsBffProject=false` opts out. |
| `IsBlazorHostProject` | A web SDK project that references an `IsBlazorWebAssemblyProject` project - at evaluation for the `<Name>`/`<Name>.Client` pairing, during the build (`ResolvePurviewBlazorHostProject`) otherwise. Never implies `IsBffProject`. |
| `IsRoslynComponent` | `<IsRoslynComponent>true</IsRoslynComponent>` is declared in the project file. |
| `IsPackable` | `<IsPackable>true</IsPackable>` is declared in the project file. |
| `IsWebProject` | Marker used in SDK web-project behaviour. |

`TestingType` is the detected test category suffix from the project name (for example `Unit`,
`Integration`, `E2E`); `TargetProjectName` is the inferred non-test project name that a test project
targets. The detected `TestingType` also becomes the baseline test category, while additional categories
remain available to the test suite.

Detection is a **pattern, not a lookup against the set below**. A project named `MyProject.GoldenTests`
is detected, and gets `TestingType=Golden` and `Category=Golden`, even though `Golden` appears nowhere in
the SDK. The pattern requires the dot, so a project named plainly `UnitTests` is **not** detected as a
test project at all — it gets no runner, no coverage and no category.

The `TestType` item group below governs [InternalsVisibleTo](InternalsVisibleTo.md) generation instead: a
non-test project grants access to `{AssemblyName}.{TestType}Tests` for each name on it. A suffix outside
the set still builds, still runs and still gets its category — it just cannot reach the target project's
`internal` members.

The SDK ships the following full `TestType` set, in addition to the standard `Unit` and
`Integration` values:
`Acceptance`, `Accessibility`, `Approval`, `Architecture`, `Benchmark`, `BlackBox`, `Capacity`,
`Chaos`, `Compatibility`, `Compliance`, `Component`, `Concurrency`, `Configuration`, `Contract`,
`Deployment`, `E2E`, `EndToEnd`, `Endurance`, `Environment`, `Failover`, `Functional`, `Fuzz`,
`Globalization`, `GrayBox`, `GreyBox`, `Integration`, `Interactive`, `Interoperability`, `Load`,
`Localization`, `Migration`, `Mutation`, `Penetration`, `Performance`, `Property`, `Recovery`,
`Regression`, `Resilience`, `Sanity`, `Scenario`, `Security`, `Smoke`, `Snapshot`, `Soak`, `Spike`,
`Stress`, `System`, `Threat`, `Unit`, `Upgrade`, `Usability`, `Visual`, and `WhiteBox`.

## What each type gets

### Test projects (`IsTestProject=true`)

- `OutputType=Exe` when the testing framework is not `None`.
- A **test-context rule set** (`PurviewTestContextNoWarn`): the production API-surface rules
  (`CA1002`, `CA1012`, `CA1034`, `CA1047`, `CA1050`, `CA1051`, `CA1062`, `CA1064`, `CA1515`, `CA1707`)
  are exempt, while the strict style contract (`IDE0040`, field naming, formatting, `IDE1006` naming
  rules) still applies. `DisablePurviewTestContextRuleSet=true` enforces the production rules instead.
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
- `OutputType=Library` with `IsTestProject`/`IsTestingPlatformApplication` cleared, reasserted after the
  package props (which otherwise flip the project into an executable test host). The fixtures are consumed
  by the test assemblies, so staying a library avoids `CA1515` (which only targets executables). Set
  `PurviewSharedTestingOutputType=Exe` before the SDK import to keep the package-driven test-host shape.

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

### Blazor WebAssembly clients (`IsBlazorWebAssemblyProject=true`)

- `IsPackable=false` unless the project sets it, no container defaults even with a `Dockerfile`, and no
  automatic `../Shared*` reference. `DisablePurviewBlazorWebAssemblyDefaults=true` (before the SDK
  import) restores the generic defaults.
- `PRBLZ0001` for each `ProjectReference` that does not set `PurviewBrowserSafe`.
- Globalisation, trimming and the Blazor runtime settings are left to the Blazor SDK.

### Blazor hosts (`IsBlazorHostProject=true`)

- A host named `<Name>` gets an automatic reference to its `<Name>.Client` client unless it declares one.
  `PurviewAutoBlazorClientReference=false` opts out.

### BFF hosts (`IsBffProject=true`)

- No packages added. No OpenAPI interceptor namespace unless the BFF references
  `Microsoft.AspNetCore.OpenApi` or sets `PurviewBffExposesApi=true`.
- `PDS0008`/`PDS0009` on the authentication cookie (see [Analyzers](Analyzers.md)).

### Browser-safe libraries (`PurviewBrowserSafe=true`)

- `IsAotCompatible=true`, `<SupportedPlatform Include="browser" />`, and the trim, single-file, AOT and
  `CA1416` ids in `WarningsAsErrors` - each only where the project set nothing.
  `PurviewBrowserSafeWarningsAsErrors=false` keeps them as warnings.
- The same `PRBLZ0001` reference check a client runs.

The README's [Blazor WebAssembly and BFF](../../README.md#blazor-webassembly-and-bff) section has the
failure each behaviour prevents.

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
