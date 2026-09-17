# Purview .NET Project SDK Wiki

This wiki is the project documentation hub for **Purview.BuildSdk** — a reusable MSBuild SDK
NuGet package that delivers standardised .NET project defaults, code-style enforcement, test-framework
wiring, and Central Package Management integration. Install it once per repo; every project beneath the
repo root inherits everything automatically.

> [!NOTE]
> The SDK imposes convention over configuration, enforcing certain styles and automations based on
> project file names and repository layout.

## Start here

- [Getting Started](Getting-Started.md)
- [Project Naming Conventions](Project-Naming-Conventions.md)
- [Project Type Detection](Project-Type-Detection.md)
- [Configuration Reference](Configuration-Reference.md)
- [Version Detection](Version-Detection.md)
- [Assembly Name Generation](Assembly-Name-Generation.md)
- [InternalsVisibleTo](InternalsVisibleTo.md)
- [Testing Wiring](Testing-Wiring.md)
- [Packaging](Packaging.md)
- [SDK-Shipped Analyzers](Analyzers.md)
- [Code Fixes](Code-Fixes.md)
- [Repository Bootstrap](Repository-Bootstrap.md)
- [Agent Folder](Agent-Folder.md)
- [Release Flow](Release-Flow.md)

## Feature highlights

- **Project type detection** — `IsCSharpProject`, `IsTestProject`, `IsSharedTestingProject`,
  `IsContainerProject`, `IsWebSdkProject`, `IsAspireHostProject`, `IsCLIProject`, … driven by the
  `.csproj` filename, the `Sdk` attribute, and on-disk markers.
- **C# defaults** — `net10.0` TFM (overridable, `netstandard2.0` for Roslyn components),
  `LangVersion=preview` (`latest` for Roslyn components), `Nullable=enable`, `ImplicitUsings=enable`,
  deterministic builds, `ManagePackageVersionsCentrally=true`.
- **Code style** — an `.editorconfig` baked into the package, applied via `EditorConfigFilePath`, and
  auto-bootstrapped to the repo root if missing; `EnforceCodeStyleInBuild=true`,
  `EnableNETAnalyzers=true`, `AnalysisLevel=latest`, `AnalysisMode=All`.
- **Assembly identity** — `RootNamespace` is the canonical public name; `AssemblyName` and `PackageId`
  default to the fully evaluated `RootNamespace` (or the full logical project name when suffix
  stripping removed a segment).
- **Testing framework** — `TestingFramework`: **TUnit** (default), `Xunit`, or `None`;
  `SubstituteFramework`: **TUnitMocks** (default), `NSubstitute`, or `None`;
  `TestDataFramework`: **Bogus** (default) or `None`.
- **Version detection** — the `version` field from the repo `package.json` is applied to `Version` and
  `PackageVersion` automatically, with local caching and a strict mode.
- **Repository bootstrap** — missing repo-root `.editorconfig` and `global.json` are auto-copied or
  created by default (`DisableAutoCopySdkFiles=true` to opt out).
- **Bundled analyzers and code fixes** — `PDS0001`–`PDS0005` plus IDE code fixes for naming and
  extensions-namespace conventions.
- **Agent folder** — the package ships `.agents/**` content that is copied into the consuming
  repository so coding agents can discover repository-aware guidance automatically.

## Requirements

- .NET SDK 10.0 or later (the default TFM for source projects is `net10.0`).
- A `NamespacePrefix` set before the SDK import.
- A `Directory.Packages.props` at the repo root when Central Package Management is used (the SDK sets
  `ManagePackageVersionsCentrally=true`).

## Repository layout

- `src/src/BuildSdk` — the packable MSBuild SDK package (`Purview.BuildSdk`), with the
  SDK logic under `Sdk/`.
- `src/src/Analyzers` — Roslyn analyzer/suppressor assembly (`Purview.BuildSdk.Analyzers`).
- `src/src/CodeFixers` — Roslyn code-fix assembly (`Purview.BuildSdk.CodeFixers`).
- `src/tests` — unit, integration, and SDK harness test projects.
- `docs/wiki` — this wiki.
- `templates/` — ready-to-copy starter files for consuming repositories.