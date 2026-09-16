# Testing Wiring

The SDK wires the testing stack for you based on three properties that must be set **before** the
SDK import:

| Property | Default | Description |
| -- | -- | -- |
| `TestingFramework` | `TUnit` | Testing framework. Supported values: `TUnit`, `Xunit`, `None`. |
| `SubstituteFramework` | `TUnitMocks` | Mocking provider. Supported values: `TUnitMocks`, `NSubstitute`, `None`. |
| `TestDataFramework` | `Bogus` | Test data provider. Supported values: `Bogus`, `None`. |

All three are validated at build/restore/pack time, and invalid values fail the build with a clear
error.

## TUnit (default)

- `OutputType=Exe`, `TestingPlatformDotnetTestSupport=true`, `UseMicrosoftTestingPlatformRunner=true`,
  `EnableMicrosoftTestingPlatform=true`.
- The `TUnit` package is referenced (with the `Microsoft.Testing.Platform` runner configured via
  `global.json`).
- A `[Category: <TestingType>]` assembly attribute tags every test with its detected test category
  (for example `Unit`, `Integration`), which makes `--treenode-filter` filtering work.
- `TUnit.Mocks` is referenced when `SubstituteFramework=TUnitMocks` (the default).

## Xunit (opt-in)

Set `TestingFramework=Xunit`:

- `xunit.v3` and `xunit.runner.visualstudio` (private assets) are referenced.
- A `[Trait("Category", "<TestingType>")]` assembly attribute tags every test.
- `OutputType=Exe` when the testing framework is not `None`.

## None

Set `TestingFramework=None` to disable the test runner wiring entirely. The test project is still
detected and gets coverage/IVT behaviour, but no framework packages or runner configuration are
added.

## Shared test configuration

All test and shared-testing projects get:

- `CollectCoverage=true` with coverage exclusions for `[NSubstitute*]`, `[TUnit.*]`, `[xunit.*]`,
  `[Microsoft.Testing.*]`, `[Microsoft.NET.Test*]`, and `[Bogus*]`.
- `ExcludeByAttribute` for `ExcludeFromCodeCoverageAttribute`.
- `IsPackable=false`, `IsPublishable=false`, `MaxCpuCount=0`, `DisableGenerateAssemblyInfoClass=true`.
- Disabled native instrumentation.
- An `[assembly: ExcludeFromCodeCoverage]` attribute.

## Substitution frameworks

- **TUnitMocks** (default) — references `TUnit.Mocks`.
- **NSubstitute** — references `NSubstitute` plus `NSubstitute.Analyzers.CSharp` (analyzers,
  private assets) and a global `using NSubstitute`.
- **None** — no mocking package.

## Test data frameworks

- **Bogus** (default) — references `Bogus` with a global `using Bogus`.
- **None** — no data package.

## Shared testing projects

Projects named `SharedTestingFramework`, `SharedTestingInfrastructure`, `SharedTestingInfra`,
`SharedTestingUtilities`, `SharedTestingUtils`, `SharedTestingLibrary`, `SharedTestingLib`, or
`SharedTestingHelpers` are treated as shared testing helpers:

- They get the test package references (`TUnit.Core` rather than the full `TUnit`) but **not** the
  test runner or coverage settings.
- A `[Skip]` attribute (TUnit) keeps the shared assembly from being executed directly.
- Test projects automatically reference the sibling shared testing project via
  `../SharedTesting*/SharedTesting*.csproj`.

## Automatic project references

Test projects automatically reference their target project. The SDK probes
`../<TargetProjectName>/`, `../../<TargetProjectName>/`, `../src/<TargetProjectName>/`, and
`../../src/<TargetProjectName>/` (whichever exists), so both the `src/`+`tests/` and flat layouts are
covered.

## Running tests

Tests use **Microsoft.Testing.Platform** (`global.json` sets `"runner": "Microsoft.Testing.Platform"`)
and are filtered with TUnit tree-node filters:

```sh
dotnet test --treenode-filter "/*/*/*/*[Category=Unit]/"
```

See [Project Naming Conventions](Project-Naming-Conventions.md) for the recognised test suffixes and
[InternalsVisibleTo](InternalsVisibleTo.md) for how test assemblies access internals.