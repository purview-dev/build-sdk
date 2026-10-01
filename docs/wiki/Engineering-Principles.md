# Engineering Principles

`Purview.BuildSdk` treats naming, placement, and test structure as configuration. The SDK can only infer
the right namespaces, identities, references, and test wiring when projects follow a small, predictable set
of conventions.

This page is the policy-level guide for those conventions. Use it when creating a new repository, adding a
new project, or rationalising an older repository toward the SDK defaults. The companion pages explain the
mechanics in detail:

- [Project Naming Conventions](Project-Naming-Conventions.md)
- [Project Type Detection](Project-Type-Detection.md)
- [Assembly Name Generation](Assembly-Name-Generation.md)
- [Testing Wiring](Testing-Wiring.md)

## Core principles

- Naming is part of configuration.
- Folder placement is part of configuration.
- `NamespacePrefix` is the root identity source for the repository.
- `RootNamespace` is the canonical code identity by default.
- `AssemblyName` and `PackageId` should normally align with the resolved project identity.
- Test suites should optimise readability at scale, not just local convenience.
- Automatic SDK behavior should be preferred over manual per-project overrides.

These conventions exist to make repository structure legible to both humans and tools. A repository with
hundreds or thousands of tests becomes easier to navigate when project names, namespaces, categories, and
test class names all tell the same story.

## Canonical project layout

The SDK is intentionally tolerant of both `src/` + `tests/` and flat layouts, but new repositories should
prefer an explicit source/test split.

Recommended layout:

```text
MyRepo/
|- Directory.Build.props
|- Directory.Build.targets
|- Directory.Packages.props
|- global.json
|- package.json
|- src/
|  |- MyProduct.slnx
|  |- src/
|  |  |- Domain/
|  |  |  '- Domain.csproj
|  |  |- Identity.API/
|  |  |  '- Identity.API.csproj
|  |  '- Web.App/
|  |     '- Web.App.csproj
|  '- tests/
|     |- Domain.UnitTests/
|     |  '- Domain.UnitTests.csproj
|     |- Identity.API.IntegrationTests/
|     |  '- Identity.API.IntegrationTests.csproj
|     '- SharedTestingInfra/
|        '- SharedTestingInfra.csproj
'- README.md
```

Canonical patterns:

- solution entry point: `src/{SolutionName}.slnx`
- source projects: `src/src/{ProjectName}/{ProjectName}.csproj`
- test projects: `src/tests/{ProjectName}.{TestType}Tests/{ProjectName}.{TestType}Tests.csproj`

The `.csproj` filename should match the containing directory name unless the repository explicitly opts out
with `DisableProjectFileNamingConventionCheck=true`.

## Project naming rules

Use short project names. The SDK applies the prefixing and identity generation for you.

Good examples:

- `Hosting.csproj`
- `Identity.API.csproj`
- `Domain.UnitTests.csproj`
- `SharedTestingInfra.csproj`

Avoid redundant prefixing:

- `Aspire.Hosting.csproj`
- `Acme.Sales.RegionalPipeline.Identity.API.csproj`

The SDK derives behavior from project names, so near-miss names are a liability. If you want the SDK's
automatic shared/shared-testing behavior, use its exact recognised names.

Recognised shared project names:

- `Shared`
- `SharedFramework`
- `SharedInfrastructure`
- `SharedInfra`
- `SharedUtilities`
- `SharedUtils`
- `SharedLibrary`
- `SharedLib`
- `SharedHelpers`

Recognised shared testing project names:

- `SharedTestingFramework`
- `SharedTestingInfrastructure`
- `SharedTestingInfra`
- `SharedTestingUtilities`
- `SharedTestingUtils`
- `SharedTestingLibrary`
- `SharedTestingLib`
- `SharedTestingHelpers`

## Namespace, assembly, and package identity

`NamespacePrefix` is the source of truth for repository identity.

The SDK derives a logical project identity from:

- `NamespacePrefix`
- the project name
- a small set of suffix-stripping rules

Key rules:

1. `RootNamespace` defaults to the logical project identity.
2. Duplicate tail segments are collapsed, so an already-prefixed project does not get double-prefixed.
3. Test suffixes such as `.UnitTests` and `.IntegrationTests` are removed from `RootNamespace`.
4. Known non-identity suffixes such as `Core`, `EF`, `Shared`, `ClientShared`, and `ServiceDefaults` may be
   stripped from `RootNamespace`.
5. `AssemblyName` and `PackageId` normally default to the resolved project identity.
6. When suffix stripping would otherwise collapse two distinct artifacts into the same identity,
   `AssemblyName` and `PackageId` keep the fuller logical identity so the artifacts remain distinct.

Examples:

| `NamespacePrefix` | Project name | `RootNamespace` | `AssemblyName` / `PackageId` |
| -- | -- | -- | -- |
| `Aspire` | `Hosting` | `Aspire.Hosting` | `Aspire.Hosting` |
| `Aspire` | `Hosting.UnitTests` | `Aspire.Hosting` | `Aspire.Hosting.UnitTests` |
| `Acme.Sales.RegionalPipeline` | `Domain` | `Acme.Sales.RegionalPipeline.Domain` | `Acme.Sales.RegionalPipeline.Domain` |
| `Acme.Sales.RegionalPipeline` | `Identity.API` | `Acme.Sales.RegionalPipeline.Identity.API` | `Acme.Sales.RegionalPipeline.Identity.API` |
| `Acme.Sales.RegionalPipeline` | `Identity.Core` | `Acme.Sales.RegionalPipeline.Identity` | `Acme.Sales.RegionalPipeline.Identity.Core` |
| `Acme.Sales.RegionalPipeline` | `Shared` | `Acme.Sales.RegionalPipeline` | `Acme.Sales.RegionalPipeline.Shared` |

This distinction matters when reasoning about packaging, `InternalsVisibleTo`, and test assembly names.

## Automatic project references

The SDK infers project references from naming and placement.

For test projects:

- `TargetProjectName` is inferred from the project name.
- the target project is auto-discovered from conventional relative paths
- sibling shared-testing projects are also discovered automatically

For non-test projects:

- sibling shared projects are discovered automatically

This means naming discipline is not cosmetic. It directly affects whether the SDK can connect the right
projects without manual `ProjectReference` maintenance.

## Test project types and categories

The detected test type becomes the baseline category for the project.

Examples:

- `*.UnitTests` -> `Category=Unit`
- `*.IntegrationTests` -> `Category=Integration`
- `*.ContractTests` -> `Category=Contract`

The detected test category is the default category, not the only one. Additional categories are allowed and
encouraged when they improve filtering and discoverability in large suites.

Recommended common test project types:

- `UnitTests`
- `IntegrationTests`
- `E2ETests`
- `FunctionalTests`
- `ContractTests`

The SDK also recognises broader suffixes when a repository genuinely needs them, including:

- `AcceptanceTests`
- `PerformanceTests`
- `LoadTests`
- `SmokeTests`
- `StressTests`
- `RegressionTests`
- `SecurityTests`
- `ScenarioTests`
- `SystemTests`
- `ArchitectureTests`
- `AccessibilityTests`
- `InteractiveTests`
- `EnvironmentTests`
- `WhiteBoxTests`
- `BlackBoxTests`
- `ChaosTests`
- `ThreatTests`

Prefer the smaller common set by default. Reach for the broader set only when the test type itself carries
important operational meaning.

## Default and specialised test wiring

Standard test projects automatically receive the default test stack from the SDK:

- `TUnit`
- `TUnit.Mocks`
- `Bogus`
- Microsoft.Testing.Platform integration

Shared testing projects receive test-support wiring rather than a runnable test host.

Specialised dependencies stay explicit and intentional:

- `TUnit.Aspire` for Aspire lifecycle or AppHost-backed integration tests
- `Testcontainers` for container-backed integration tests

Do not manually duplicate the default stack in every test project unless the repository has explicitly opted
out of the SDK defaults.

## Test readability rules

The goal of the naming conventions is readability when a repository contains thousands of tests.

### Subject-based tests

When a test class owns a specific subject, prefer `{SubjectName}Tests`.

Examples:

- `CustomerIdTests`
- `ResultsEndpointFilterTests`
- `AssemblyNameCalculatorTests`

When a test method targets a specific member or subject behavior, use:

`{SubjectOrMemberUnderTest}_{Scenario}_{Expectation}`

Examples:

- `Create_GivenInvalidEmail_ThrowsArgumentException`
- `Constructor_GivenNullLogger_ThrowsArgumentNullException`
- `DisplayName_WhenTrimmed_ReturnsNormalizedValue`
- `CompareTo_GivenHigherVersion_ReturnsPositiveValue`

This format is for subject-based tests: methods, constructors, properties, operators, conversions,
validation hooks, and similarly well-bounded behavior.

### Non-subject-based suites

Not every useful suite is centered on one subject. Broader suite names are allowed when they are more
readable and truthful.

Examples:

- `BuildIntegrationTests`
- `GeneratedPackageAssetsTests`
- `CrossPlatformSchemaCompatibilityTests`
- `AppHostLifecycleTests`

Use TUnit features such as display names, categories, and data-driven metadata to keep these broader suites
discoverable and understandable.

### Async, assertions, and cancellation

- Test methods should be `public async Task`.
- Assertions should use TUnit `Assert`.
- When the API under test accepts a `CancellationToken`, the test method should accept
  `CancellationToken cancellationToken` as its final parameter.
- Parameters before the token may come from TUnit data sources.
- The cancellation token should be forwarded to the API under test and to helper methods that also accept
  one.

## Rationalising existing repositories

Older repositories may not fully match these conventions yet. Apply them pragmatically:

- new projects should follow these rules by default
- existing projects should converge over time
- preserve readability and avoid churn-only renames
- when in doubt, prefer the structure that lets the SDK infer behavior without extra overrides

Use these engineering principles as the policy layer, then use the companion pages for the concrete
mechanics of detection, naming, identity generation, and test wiring.
