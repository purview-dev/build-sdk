# Purview .NET Project SDK Wiki

This wiki is the project documentation hub for **Purview.BuildSdk** — a reusable MSBuild SDK
NuGet package that delivers standardised .NET project defaults, code-style enforcement, test-framework
wiring, and Central Package Management integration. Install it once per repo; every project beneath the
repo root inherits everything automatically.

> [!NOTE]
> The SDK imposes convention over configuration, enforcing certain styles and automations based on
> project file names and repository layout.

## How this wiki is organised

The wiki is one folder per topic area, in eight sections, in reading order. The sidebar carries the
same sections in the same order, and `_Sidebar.md` is what the published documentation site reads for
page order. A page's folder is part of its published URL (`packaging/SourceLink.md` is
`/docs/build-sdk/packaging/sourcelink/`), so a page moves folder only when it changes topic.

| Section | Where it lives |
| -- | -- |
| Get started | the wiki root, and `concepts/Engineering-Principles.md` |
| Conventions and detection | `concepts/` |
| Configuration | `Configuration-Reference.md` and `configuration/` |
| Testing | `testing/` |
| Analyzers and code fixes | `analyzers/` |
| Packaging | `packaging/` |
| Agent skills and tooling | `tooling/` |
| Contributing and releases | `development/` |

Three pages stay at the wiki root, because the site depends on their URLs: `Getting-Started.md` is
the landing page, and `Configuration-Reference.md` and `Repository-Bootstrap.md` are the pages the
site's project catalogue deep-links. `DocumentationWikiTests` holds the folders, this page and the
sidebar to all of that.

## Get started

| Page | What it answers |
| -- | -- |
| [Getting Started](Getting-Started.md) | The minimal setup to adopt the SDK: `global.json`, `Directory.Build.props`, `package.json`, and a first build. |
| [Repository Bootstrap](Repository-Bootstrap.md) | Which repository-root files the SDK writes (`.editorconfig`, `global.json`), when, and how to opt out. |
| [Engineering Principles](concepts/Engineering-Principles.md) | The conventions the SDK relies on: layout, naming, namespace identity and test structure, and why. |

## Conventions and detection

| Page | What it answers |
| -- | -- |
| [Project Naming Conventions](concepts/Project-Naming-Conventions.md) | The exact naming and layout rules the SDK implements. |
| [Project Type Detection](concepts/Project-Type-Detection.md) | How the SDK decides what a project is (`IsTestProject`, `IsWebSdkProject`, …) and what follows from it. |
| [Assembly Name Generation](concepts/Assembly-Name-Generation.md) | How `RootNamespace`, `AssemblyName` and `PackageId` are derived. |

## Configuration

| Page | What it answers |
| -- | -- |
| [Configuration Reference](Configuration-Reference.md) | Every SDK property: its default, and whether it must be set before the `Sdk.props` import. |
| [Property Schema and IntelliSense](configuration/Property-Schema.md) | The `Sdk.props.buildschema.json` sidecar, csproj IntelliSense, and the checks that hold it to the code. |
| [Version Detection](configuration/Version-Detection.md) | How `Version` and `PackageVersion` come from `package.json`, and the strict mode. |
| [InternalsVisibleTo](configuration/InternalsVisibleTo.md) | The generated `InternalsVisibleTo` attributes for test projects. |

## Testing

| Page | What it answers |
| -- | -- |
| [Testing Wiring](testing/Testing-Wiring.md) | How test projects get their framework, mocking and test-data packages, and how to change them. |

## Analyzers and code fixes

| Page | What it answers |
| -- | -- |
| [SDK-Shipped Analyzers](analyzers/Analyzers.md) | The bundled analyzers and suppressors (`PDS*`), and what each one reports. |
| [Code Fixes](analyzers/Code-Fixes.md) | The IDE code fixes, and why they load only inside Visual Studio. |

## Packaging

| Page | What it answers |
| -- | -- |
| [Packaging](packaging/Packaging.md) | What a packable project gets: symbols, analyzer assets, the `Sdk/` folder and the README. |
| [SourceLink](packaging/SourceLink.md) | Choosing the SourceLink provider for the repository host, and opting out. |

## Agent skills and tooling

| Page | What it answers |
| -- | -- |
| [Agent Skills](tooling/Agent-Skills.md) | Installing, restoring, updating and tidying agent skills through the standard skills CLI (`PRSKL*`). |
| [Justfile](tooling/Justfile.md) | The shared `just` modules: the barrel, the recipes, the exported environment and `just bump`. |

## Contributing and releases

| Page | What it answers |
| -- | -- |
| [Release Flow](development/Release-Flow.md) | How a version is cut and published, and the pipeline recipes. |
| [Performance](development/Performance.md) | What the SDK costs at evaluation time, measured, and what has been done about it. |

## Feature highlights

- **Project type detection** — `IsCSharpProject`, `IsTestProject`, `IsSharedTestingProject`,
  `IsContainerProject`, `IsWebSdkProject`, `IsAspireHostProject`, `IsCLIProject`, … driven by the
  `.csproj` filename, the `Sdk` attribute, and on-disk markers.
- **Engineering conventions** — repository layout, project naming, namespace identity, test readability,
  and the default boundaries between source, test, and shared test-support projects.
- **C# defaults** — `net10.0;net11.0` TFMs (overridable, `netstandard2.0` for Roslyn components),
  `LangVersion=latest` (C# 15, including unions), `Nullable=enable`, `ImplicitUsings=enable`,
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
- **Repository bootstrap** — missing repo-root `.editorconfig` and `global.json` are written atomically by
  default (`DisableAutoCopySdkFiles=true` to opt out). Existing files are never overwritten unless
  `PurviewRepoBootstrapMode` says otherwise, and a race between parallel projects is a no-op rather than a
  build failure.
- **Bundled analyzers and code fixes** — three analyzers (`PDS0002`–`PDS0004`) and four suppressors
  (`PDS0001`, `PDS0005`, `PDS0006`, `PDS0007`), plus IDE code fixes for naming and extensions-namespace
  conventions.
- **Agent skills** — a thin wrapper over the standard skills CLI (`bun x skills`): install a repository's
  root `skills/` (`-t:InstallPurviewSkills -p:PurviewSkillsSource=owner/repo`), restore `skills-lock.json`,
  update every recorded source, and keep the agent directories tidy. Each is an explicit step that runs
  once per solution from the repository root — never during an ordinary build. The SDK itself writes nothing into an agent directory: the build-time
  `.agents/**` mirror was removed outright, because the CLI owns those paths and two writers on one set
  of paths meant the mirror silently overwriting a pinned install.

## Requirements

- .NET SDK 11.0 or later (the default TFMs for source projects are `net10.0` and `net11.0`).
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
