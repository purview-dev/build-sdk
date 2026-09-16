# Project Naming Conventions

The SDK applies several conventions automatically based on the `.csproj` filename and
`NamespacePrefix`.

## Defaults (no extra configuration)

`RootNamespace` is always derived from `$(NamespacePrefix).$(ProjectName)` and is the canonical
default public name. By default (`EnableAssemblyNameGeneration=true`), `AssemblyName` and `PackageId`
both follow the fully evaluated `RootNamespace`. Test projects retain their detected suffix so test
assemblies stay distinct from the source assembly. Set `EnableAssemblyNameGeneration=false` (before the
SDK import) to opt out and use standard .NET behaviour (the `.csproj` filename):

| `.csproj` filename | `AssemblyName` / `PackageId` | `RootNamespace` | Detected as |
| -- | -- | -- | -- |
| `Api.csproj` | `Acme.Api` | `Acme.Api` | Source project |
| `Api.UnitTests.csproj` | `Acme.Api.UnitTests` | `Acme.Api` | `IsTestProject=true`, `TestingType=Unit` |
| `Api.IntegrationTests.csproj` | `Acme.Api.IntegrationTests` | `Acme.Api` | `IsTestProject=true`, `TestingType=Integration` |
| `SharedTestingFramework.csproj` | `Acme.SharedTestingFramework` | `Acme` | `IsSharedTestingProject=true` |

> **Note:** `InternalsVisibleTo` follows `$(AssemblyName)` — so for `Api.csproj` the SDK generates
> `Acme.Api.UnitTests`, `Acme.Api.IntegrationTests`, etc.

Use short `.csproj` names — the SDK handles the prefixing:

```text
✅  Api.csproj                        → short name, SDK resolves the rest
❌  Acme.Api.csproj                   → redundant prefix, avoid
```

A build-time check (`PurviewProjectFileNameMismatch`) enforces that the `.csproj` filename matches its
parent directory name, preventing inconsistent naming. Set `DisableProjectFileNamingConventionCheck=true`
to opt out.

## Recommended structure: `src/` + `tests/`

For larger repos, separate source and test projects into `src/` and `tests/` folders:

```text
MyRepo/
├── Directory.Build.props          ← NamespacePrefix=Acme
├── Directory.Build.targets
├── Directory.Packages.props
├── global.json
├── src/
│   ├── Api/
│   │   └── Api.csproj
│   ├── Core/
│   │   └── Core.csproj
│   └── SourceGenerator/
│       └── SourceGenerator.csproj
├── tests/
│   ├── Api.UnitTests/
│   │   └── Api.UnitTests.csproj    → IsTestProject=true, TestingType=Unit
│   ├── Api.IntegrationTests/
│   │   └── Api.IntegrationTests.csproj
│   └── SharedTestingFramework/
│       └── SharedTestingFramework.csproj  → IsSharedTestingProject=true
└── package.json
```

## Flat structure: everything together

For smaller repos, source and test projects can live side-by-side:

```text
MyRepo/
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── global.json
├── Api/
│   └── Api.csproj
├── Api.UnitTests/
│   └── Api.UnitTests.csproj
├── Core/
│   └── Core.csproj
├── Core.IntegrationTests/
│   └── Core.IntegrationTests.csproj
└── package.json
```

Both layouts work identically — the SDK detects test projects by name suffix, not folder location.

## Quick reference

```sh
# Create a source project
mkdir src/Api && cd src/Api
dotnet new classlib -n Api

# Create its unit tests
mkdir ../../tests/Api.UnitTests && cd ../../tests/Api.UnitTests
dotnet new classlib -n Api.UnitTests   # SDK wires TUnit automatically

# Or flat:
mkdir Api.UnitTests && cd Api.UnitTests
dotnet new classlib -n Api.UnitTests
```

## Test project naming conventions

Test projects are automatically detected by their suffix. Supported patterns:

```text
MyProject.UnitTests       → IsTestProject=true, TestingType=Unit
MyProject.IntegrationTests→ IsTestProject=true, TestingType=Integration
MyProject.E2ETests        → IsTestProject=true, TestingType=E2E
```

Any suffix from the full list is recognised: `Unit`, `Integration`, `E2E`, `EndToEnd`, `Acceptance`,
`Functional`, `Performance`, `Load`, `Smoke`, `Stress`, `Regression`, `Security`, `Chaos`, `Scenario`,
`System`, `Threat`, `BlackBox`, `WhiteBox`, `Accessibility`, `Interactive`, `Environment`,
`Architecture`, `Contract`.

## Shared testing projects

Projects named `SharedTestingFramework`, `SharedTestingInfrastructure`, `SharedTestingInfra`,
`SharedTestingUtilities`, `SharedTestingUtils`, `SharedTestingLibrary`, `SharedTestingLib`, or
`SharedTestingHelpers` are treated as shared testing helpers — they get test package references but
not the test runner or coverage settings.

See [Project Type Detection](Project-Type-Detection.md) for how these names are classified, and
[Assembly Name Generation](Assembly-Name-Generation.md) for how the identities are derived.