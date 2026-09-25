# Build SDK

[![NuGet version](https://img.shields.io/nuget/v/Purview.BuildSdk.svg)](https://www.nuget.org/packages/Purview.BuildSdk)
[![Release](https://github.com/purview-dev/build-sdk/actions/workflows/release.yml/badge.svg)](https://github.com/purview-dev/build-sdk/actions/workflows/release.yml)

A reusable MSBuild SDK NuGet package that delivers standardised .NET project defaults, code-style enforcement, test-framework wiring, and Central Package Management integration. Install it once per repo — every project beneath the repo root inherits everything automatically.

> [!NOTE]
> This SDK package imposes convention over configuration, enforcing certain styles and automations based on project file names, etc.

## What's included

| Feature | Detail |
| -- | -- |
| **Project type detection** | `IsCSharpProject`, `IsTestProject`, `IsSharedTestingProject`, `IsContainerProject`, `IsWebSdkProject`, `IsAspireHostProject`, … |
| **C# defaults** | `net10.0` TFM (overridable), `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=enable`, deterministic builds |
| **Code style** | `.editorconfig` baked into the package, applied via `EditorConfigFilePath`, and auto-bootstrapped to repo root if missing; `EnforceCodeStyleInBuild=true`, `EnableNETAnalyzers=true`, `AnalysisLevel=latest`, `AnalysisMode=All` |
| **NuGet packaging** | `AssemblyName`/`PackageId` default to the fully evaluated `RootNamespace`; packable projects get `GenerateDocumentationFile=true`, `PublishRepositoryUrl=true`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `EmbedUntrackedSources=true`, and portable PDBs delivered via `.snupkg` (not the `.nupkg`) |
| **Repo bootstrap** | Missing repo-root `.editorconfig` and `global.json` are auto-copied/created by default (disable via `DisableAutoCopySdkFiles=true`) |
| **CI detection** | `ContinuousIntegrationBuild` set automatically when `CI`, `GITHUB_ACTIONS`, or `TF_BUILD` env vars are present |
| **SourceLink** | `Microsoft.SourceLink.GitHub` added to all packable projects (configurable via `SourceLinkPackageName`) |
| **Purview Telemetry** | `Purview.Telemetry.SourceGenerator` + `Microsoft.Extensions.Telemetry.Abstractions` added by default (opt-out) |
| **Assembly info** | Auto-generated `static partial class AssemblyInfo` with `RootNamespace`, `Version`, `Company`, etc., plus an embedded `Microsoft.CodeAnalysis.EmbeddedAttribute` (can be excluded via `PURVIEW_SDK_EXCLUDE_EMBEDDED`). |
| **InternalsVisibleTo** | Generated for all `TestType` variants and shared testing projects, using the resolved `$(AssemblyName)` so explicit, generated, and default naming are all handled |
| **Namespace management** | `NamespacePrefix.ProjectName` pattern with suffix stripping (`.Core`, `.Shared`, `.EF`, …) |
| **Testing framework** | `TestingFramework`: **TUnit** (default), `Xunit`, or `None` |
| **Mocking provider** | `SubstituteFramework`: **TUnitMocks** (default), `NSubstitute`, or `None` |
| **Test data provider** | `TestDataFramework`: **Bogus** (default) or `None` |
| **Version detection** | Reads `version` from `package.json` and applies it to `Version` and `PackageVersion` automatically; falls back to `0.0.1` |
| **CPM** | `ManagePackageVersionsCentrally=true` — versions live in your `Directory.Packages.props` |

---

## Quick start

### 1. Add the SDK to `global.json`

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Purview.BuildSdk": "1.0.0"
  }
}
```

### 2. Create `Directory.Build.props` at repo root

```xml
<Project>
  <PropertyGroup>
    <!-- Required: sets the root namespace prefix for all projects -->
    <NamespacePrefix>YourCompany</NamespacePrefix>
  </PropertyGroup>

  <Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
</Project>
```

### 3. Create `Directory.Build.targets` at repo root

```xml
<Project>
  <Import Sdk="Purview.BuildSdk" Project="Sdk.targets" />
</Project>
```

### 4. Copy `Directory.Packages.props` to repo root

Copy `templates/Directory.Packages.props` from this package to your repo root. All package versions default to `*` (latest at restore). Pin any package by replacing `*` with a specific version.

> **Note:** `ManagePackageVersionsCentrally=true` is set by the SDK. You **must** have a `Directory.Packages.props` at your repo root for CPM to work, even if it only contains the packages the SDK adds automatically.

---

## Project naming guide

The SDK applies several conventions automatically based on the `.csproj` filename and `NamespacePrefix`.

### Defaults (no extra configuration)

`RootNamespace` is always derived from `$(NamespacePrefix).$(ProjectName)` and is the canonical default public name. By default (`EnableAssemblyNameGeneration=true`), `AssemblyName` and `PackageId` both follow the fully evaluated `RootNamespace`. Test projects retain their detected suffix so test assemblies stay distinct from the source assembly. Set `EnableAssemblyNameGeneration=false` (before the SDK import) to opt out and use standard .NET behaviour (the `.csproj` filename):

| `.csproj` filename | `AssemblyName` / `PackageId` | `RootNamespace` | Detected as |
| -- | -- | -- | -- |
| `Api.csproj` | `Acme.Api` | `Acme.Api` | Source project |
| `Api.UnitTests.csproj` | `Acme.Api.UnitTests` | `Acme.Api` | `IsTestProject=true`, `TestingType=Unit` |
| `Api.IntegrationTests.csproj` | `Acme.Api.IntegrationTests` | `Acme.Api` | `IsTestProject=true`, `TestingType=Integration` |
| `SharedTestingFramework.csproj` | `Acme.SharedTestingFramework` | `Acme` | `IsSharedTestingProject=true` |

> **Note:** `InternalsVisibleTo` follows `$(AssemblyName)` — so for `Api.csproj` the SDK generates `Acme.Api.UnitTests`, `Acme.Api.IntegrationTests`, etc.

Use short `.csproj` names — the SDK handles the prefixing:

```text
✅  Api.csproj                        → short name, SDK resolves the rest
❌  Acme.Api.csproj                   → redundant prefix, avoid
```

A build-time check (`PurviewProjectFileNameMismatch`) enforces that the `.csproj` filename matches its parent directory name, preventing inconsistent naming. Set `DisableProjectFileNamingConventionCheck=true` to opt out.

### Recommended structure: `src/` + `tests/`

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

### Flat structure: everything together

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

### Quick reference

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

---

## Template files

The `templates/` folder contains ready-to-copy starter files for new repos:

| File | Purpose |
| -- | -- |
| `Directory.Build.props` | Bootstrapper — copy to repo root and set `NamespacePrefix` |
| `Directory.Build.targets` | Bootstrapper — copy to repo root |
| `Directory.Packages.props` | All default package versions with `*` floating to latest |
| `global.json` | `msbuild-sdks` entry + `Microsoft.Testing.Platform` test runner |
| `.gitignore` | ASP.NET Core + VS + Rider + Node combined gitignore |
| `.gitattributes` | Line-ending normalisation for .cs, .json, .yml, etc. |
| `.config/dotnet-tools.json` | CSharpier tool manifest |

The package also ships bundled agent content under `.agents/**`. During build, the SDK mirrors it into the consuming repository's `.agents/` folder by default so compatible coding agents can discover repository-aware guidance automatically. The SDK also injects a `.gitignore` file into each second-level agent folder with the content `# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore`, so the copied folder is ignored by Git while keeping the folder structure discoverable.

The mirror is change-aware: file fingerprints and content hashes are recorded in
`.purview/agent-sync.cache` at the repository root, so unchanged content is skipped and repeat builds
touch no files. Every write is staged into a temporary file and renamed into place, and because all
projects in a solution share these destinations the retries are silent — copy retry notices (`MSB3026`)
are demoted to low-importance messages while a copy that still fails after every retry is reported as an
error. See [Agent Folder](docs/wiki/Agent-Folder.md) and
[Repository Bootstrap](docs/wiki/Repository-Bootstrap.md) for the retry and failure properties.

---

## Configuration reference

Set any of these properties **before** the `<Import>` in your `Directory.Build.props`:

### Version detection

| Property | Default | Description |
| -- | -- | -- |
| `UsePackageJsonVersion` | `true` | `true` enables version detection, `false` disables it, and `Strict` requires version detection to succeed (build fails if no version source can be resolved). |
| `RootPackageJson` | *(auto-discovered)* | Explicit path to a `package.json`. Relative paths are resolved from the project directory. |
| `EnableVersionDetectionCache` | `true` | Enables local caching of auto-discovered package.json version results. |
| `VersionDetectionLogEnabled` | `false` | Emits a high-importance message showing the detected package version. Set to `true` to enable logging.

When `UsePackageJsonVersion=true` (the default) or `UsePackageJsonVersion=Strict`, the SDK:

1. **Explicit path** — if `RootPackageJson` is set, reads that file directly.
2. **Auto-discovery** — otherwise, locates the repo root from CI workspace variables (`GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY`, `BUILD_REPOSITORY_LOCALPATH`, `CI_PROJECT_DIR`), then by walking up from the project directory looking for a `.git` marker or a `package.json`, and reads `package.json` from there.

The extracted `version` field is applied to both `Version` and `PackageVersion`. A build error is raised when a `package.json` was resolved but can't be read, or when it contains no `version` field. With `UsePackageJsonVersion=Strict`, the build also fails when no package.json source can be discovered at all (for example, no explicit `RootPackageJson` and no discoverable `.git` marker or CI workspace variable); in non-strict mode that case silently falls back to the `0.0.1` default.

Version detection logging is disabled by default. Set `VersionDetectionLogEnabled` to `true` to emit a high-importance message showing the detected package version.

> **Important — set before the import:** Both `UsePackageJsonVersion` and `RootPackageJson` must be set **before** the `<Import Sdk="Purview.BuildSdk" Project="Sdk.props" />` line in your `Directory.Build.props`. The version logic runs during that import and cannot see properties set afterwards (e.g. in individual `.csproj` files).
>
> ```xml
> <Project>
>   <PropertyGroup>
>     <NamespacePrefix>Acme</NamespacePrefix>
>     <!-- Set here, before the import -->
>     <RootPackageJson>$(MSBuildThisFileDirectory)package.json</RootPackageJson>
>   </PropertyGroup>
>
>   <Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
> </Project>
> ```

### General

| Property | Default | Description |
| -- | -- | -- |
| `NamespacePrefix` | *(required)* | Root namespace prefix, e.g. `Acme`. Results in `Acme.MyProject`. |
| `DisableNamespacePrefixCheck` | `false` | Set to `true` to suppress the build error for missing `NamespacePrefix`. |
| `TargetFramework` | `net10.0` | Override the default TFM per-project or globally. Defaults to `netstandard2.0` for projects declaring `IsRoslynComponent=true`. |
| `IsRoslynComponent` | `false` | When explicitly `true`, applies source-generator defaults: a single `netstandard2.0` target, `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, extended analyzer rules, SourceLink with `EmbedUntrackedSources=true`, compiler-generated output under the intermediate directory, no dependency file, telemetry exclusion, and package build output. Pack-time validation (`ValidateRoslynComponentCompilerSettings`) fails the pack if the compiler defaults are missing unless `DisableRoslynCompilerDefaultsValidation=true`. Roslyn development dependencies (`Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Analyzers`) default to `PrivateAssets="all"`. |
| `IsRoslynComponentOnly` | `true` for Roslyn components | Produces an analyzer-only package: `IncludeBuildOutput=false`, `IncludeSymbols=false`, and the analyzer assembly and portable PDB are packed under `analyzers/dotnet/cs/`. Set to `false` for a dual-role Roslyn component that uses normal library symbol packaging. |
| `PackProjectReferencedSourceGenerators` | `true` | Automatically packs analyzer `ProjectReference` outputs and their runtime dependencies under `analyzers/dotnet/cs/`. Set to `false` to opt out; set `Pack="false"` on an individual reference to exclude only that generator. |
| `SourceLinkPackageName` | `Microsoft.SourceLink.GitHub` | SourceLink provider. Set to `Microsoft.SourceLink.AzureDevOps.Git` for ADO repos. |
| `DisableSourceLink` | `false` | Set to `true` to stop the SDK from adding the configured SourceLink package automatically. |
| `EnableAssemblyNameGeneration` | `true` | When `true` (default), `AssemblyName` and `PackageId` derive from the fully evaluated `RootNamespace`. When explicitly `false`, standard .NET behaviour applies (`$(MSBuildProjectName)`). Explicit `<AssemblyName>`/`<PackageId>` in a `.csproj` always take precedence. |
| `DisableProjectFileNamingConventionCheck` | `false` | Set to `true` to disable the validation that requires `MyProject\MyProject.csproj` naming alignment. |
| `DisableGenerateAssemblyInfoClass` | `false` | Set to `true` to disable the generated `AssemblyInfo` helper source. |
| `AutoIncludeUsings` | `true` | Controls SDK-added global usings for `NamespacePrefix` and `RootNamespace`. |

### Packable project defaults

For projects where `IsPackable=true`, the SDK provides these defaults **only when the consuming project has not supplied a value** — explicit values are always preserved:

| Property | Default | Description |
| -- | -- | -- |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. |
| `IncludeSymbols` | `true` | Produces a symbol package (`false` for Roslyn-component-only packages — their PDB ships inside the `.nupkg` under `analyzers/dotnet/cs/` via `PurviewPackAnalyzerPdb=true`). |
| `SymbolPackageFormat` | `snupkg` | Symbol package format. Always the modern `.snupkg`; the legacy `.symbols.nupkg` is never produced by default. |
| `PublishRepositoryUrl` | `true` | Publishes the repository URL. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources for SourceLink. |
| `DebugType` | `portable` | Ensures portable PDBs for symbol-package delivery. |
| `IncludeSource` | `true` | Includes source files in the package. |

Portable PDBs are delivered through the `.snupkg`; the normal `.nupkg` does **not** receive PDB files unless the project explicitly opts in (for example by adding `.pdb` to `AllowedOutputExtensionsInPackageBuildOutputFolder`).

**Repository README auto-inclusion:** when the repo root is discoverable (`.git` marker or CI workspace variable), the repository-root `README.md` is packed automatically for packable projects and registered via `PackageReadmeFile` — but only when the file exists and `PackageReadmeFile` has not been configured explicitly. The SDK skips the auto-inclusion if a README-named file is already being packed, so no duplicate readme items are produced. No README is required; if the file is absent the pack succeeds without readme metadata.

The SDK never forces organization/package-specific metadata — `Authors`, `Company`, `PackageLicenseExpression`, `PackageLicenseFile`, `Description`, `PackageTags`, `PackageProjectUrl`, and repository URLs are left to the repository or individual package. `IsPackable` is not set blindly: it defaults to `false` and only becomes `true` when a project explicitly opts in.

Non-packable projects (including web applications) default `WarnOnPackingNonPackableProject=false`, so solution-wide pack operations skip them silently. Set `<WarnOnPackingNonPackableProject>true</WarnOnPackingNonPackableProject>` explicitly to re-enable the "cannot be packed" warning.

### Repo bootstrap

| Property | Default | Description |
| -- | -- | -- |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |
| `BootstrapEditorConfigToRepoRoot` | `true` | Copies the SDK `.editorconfig` to the repository root when missing. |
| `RepositoryEditorConfigFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `.editorconfig`. |
| `BootstrapGlobalJsonToRepoRoot` | `true` | Creates a `global.json` at the repository root when missing. |
| `RepositoryGlobalJsonFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `global.json`. |
| `PurviewBuildSdkVersionForGlobalJson` | *(auto-detected or `1.0.0` fallback)* | Version written to the `msbuild-sdks.Purview.BuildSdk` entry in a bootstrapped `global.json`. |
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing` never touches an existing file, `Always` overwrites it, `WarnOnDrift` reports a file that differs from the SDK-provided one, and `Never` skips bootstrapping. |
| `PurviewRepoBootstrapCopyRetries` | `3` | Write attempts before a bootstrap write failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between bootstrap write attempts. |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a failed bootstrap write is a warning instead of an error. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes built-in copy retry notices (`MSB3026`) to messages. Set to `false` to see every retry attempt. |

### Agent folder

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAutoSdkPack` | `true` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. Disable this for MSBuild SDK projects. |
| `EnableAgentFolderInPackage` | `true` | Mirrors the bundled `.agents/**` folder from the SDK NuGet package into the consuming repository's `.agents/` folder (or `$(AgentPackDestinationFolder)/`) before build. |
| `AgentPackDestinationFolder` | `.agents` | Repo-relative destination folder that receives the mirrored agent folder contents when `EnableAgentFolderInPackage` is `true`. |
| `PurviewAgentFolderSourcePath` | *(package-level `.agents`)* | Overrides the folder that provides the bundled `.agents` content. |
| `PurviewAgentFolderCopyRetries` | `3` | Copy attempts per file before the failure is reported. |
| `PurviewAgentFolderCopyRetryDelayMilliseconds` | `500` | Base delay between copy attempts. |
| `PurviewAgentFolderCopyFailureAsError` | `true` | When `false`, a copy that still fails after every retry is a warning instead of an error. |
| `PurviewAgentSyncManifestPath` | `<repo root>/.purview/agent-sync.cache` | Overrides the change-detection manifest used to skip unchanged agent content. |

To disable bundled agent folder copying in a consuming repo, set the opt-out property before importing the SDK:

```xml
<PropertyGroup>
  <EnableAgentFolderInPackage>false</EnableAgentFolderInPackage>
</PropertyGroup>
```

When a project is packable, the SDK treats any content under `Sdk/` as a pack target with these rules:

| Source path | Package path |
| -- | -- |
| `Sdk/.agents/**` | `.agents/**` |
| `Sdk/.github/**` | `.github/**` |
| `Sdk/build/**` | `build/**` |
| `Sdk/buildTransitive/**` | `buildTransitive/**` |
| `Sdk/buildMultiTargeting/**` | `buildMultiTargeting/**` |
| `Sdk/*.md`, `Sdk/*.png`, `Sdk/*.jpg`, etc. | package root |
| everything else under `Sdk/` | `Sdk/` |

The SDK automatically adds a `.gitignore` file into each second-level folder under `Sdk/.agents` with the content `# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore`. This ensures the copied folder structure remains discoverable in consuming repositories while the content itself is ignored by Git.

### Telemetry

| Property | Default | Description |
| -- | -- | -- |
| `ExcludePurviewTelemetry` | `false` | Set to `true` to exclude `Purview.Telemetry.SourceGenerator` from all projects. |
| `ExcludeMSTelemetryExtension` | `false` | Set to `true` to exclude `Microsoft.Extensions.Telemetry.Abstractions`. Only relevant when `ExcludePurviewTelemetry` is also `false` — when `ExcludePurviewTelemetry=true` the whole telemetry group is skipped anyway. |

### Testing

| Property | Default | Description |
| -- | -- | -- |
| `TestingFramework` | `TUnit` | Testing framework. Supported values: `TUnit`, `Xunit`, `None`. |
| `SubstituteFramework` | `TUnitMocks` | Mocking provider. Supported values: `TUnitMocks`, `NSubstitute`, `None`. |
| `TestDataFramework` | `Bogus` | Test data provider. Supported values: `Bogus`, `None`. |
| `DisableAutoInternalsVisibleTo` | `false` | Set to `true` to disable automatic `InternalsVisibleTo` generation for test types and shared testing projects. |

### Compiler-visible SDK properties

The SDK now exports its properties via `CompilerVisibleProperty`, so analyzers and source generators can read them through `build_property.<PropertyName>`.

| Property | Description |
| -- | -- |
| `UsePackageJsonVersion` | Whether version detection from `package.json` is active. |
| `RootPackageJson` | Resolved path to the `package.json` used for version detection. |
| `RepoRoot` | Repo root directory found via `.git` auto-discovery. |
| `Version` | Package/assembly version, sourced from `package.json` when detection is enabled. |
| `PackageVersion` | NuGet package version, sourced from `package.json` when detection is enabled. |
| `NamespacePrefix` | Required namespace prefix used to derive `RootNamespace`. |
| `DisableNamespacePrefixCheck` | Disables the build error for missing `NamespacePrefix`. |
| `TestingFramework` | Selected testing framework (`TUnit`, `Xunit`, or `None`). |
| `SubstituteFramework` | Selected mocking provider (`TUnitMocks`, `NSubstitute`, or `None`). |
| `TestDataFramework` | Selected test data provider (`Bogus` or `None`). |
| `SourceLinkPackageName` | SourceLink package ID added by the SDK. |
| `ExcludePurviewTelemetry` | Opt-out for `Purview.Telemetry.SourceGenerator`. |
| `ExcludeMSTelemetryExtension` | Opt-out for `Microsoft.Extensions.Telemetry.Abstractions`. |
| `DisableGenerateAssemblyInfoClass` | Disables generated `AssemblyInfo` helper source. |
| `EnableAssemblyNameGeneration` | When `true` (default), `AssemblyName` derives from `RootNamespace`. |
| `DisableAutoInternalsVisibleTo` | Disables automatic `InternalsVisibleTo` generation. |
| `AutoIncludeUsings` | Controls SDK-added global usings. |
| `IsCSharpProject` | True when the project is a `.csproj`. |
| `IsTestProject` | True when project name ends with a supported test suffix. |
| `IsSharedTestingProject` | True for known shared testing helper project names. |
| `TestingType` | Detected test category suffix from project name. |
| `TargetProjectName` | Inferred target project name for test projects. |
| `IsContainerProject` | True when Dockerfile markers indicate container defaults. |
| `IsSdkProject` | True when an SDK value is detected from project/import declaration. |
| `SdkProjectName` | Detected SDK name (e.g. `Microsoft.NET.Sdk.Web`). |
| `IsWebProject` | Marker used in SDK web-project behaviour. |
| `IsWebSdkProject` | True when `SdkProjectName` is `Microsoft.NET.Sdk.Web`. |
| `IsWorkerSdkProject` | True when `SdkProjectName` is `Microsoft.NET.Sdk.Worker`. |
| `IsAspireHostProject` | True when SDK starts with `Aspire.Sdk.Host`. |
| `EditorConfigFilePath` | Path to the SDK-provided `.editorconfig` that is injected into `@(EditorConfigFiles)`. |
| `RepositoryEditorConfigFilePath` | Destination path for bootstrapping a physical repo-level `.editorconfig` (defaults to git repo root; falls back to `Directory.Build.props` directory). |
| `BootstrapEditorConfigToRepoRoot` | When `true` (default), copies the SDK `.editorconfig` to `RepositoryEditorConfigFilePath` if missing. |
| `RepositoryGlobalJsonFilePath` | Destination path for bootstrapping a physical repo-level `global.json` (defaults to git repo root; falls back to `Directory.Build.props` directory). |
| `BootstrapGlobalJsonToRepoRoot` | When `true` (default), creates `global.json` at `RepositoryGlobalJsonFilePath` if missing. |
| `PurviewBuildSdkVersionForGlobalJson` | Version used for `msbuild-sdks.Purview.BuildSdk` when bootstrapping `global.json` (auto-detected from SDK package path, fallback `1.0.0`). |
| `DisableAutoCopySdkFiles` | When `true`, disables SDK auto-copy/bootstrap for repo files (`.editorconfig`, `global.json`). |
| `PurviewRepoBootstrapMode` | Controls repo file bootstrapping: `IfMissing` (default), `Always`, `WarnOnDrift` or `Never`. |
| `PurviewRepoBootstrapCopyRetries` | Number of write attempts before a bootstrap failure is reported (default `3`). |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | Base delay between bootstrap write attempts (default `500`). |
| `PurviewRepoBootstrapCopyFailureAsError` | When `true` (default), a bootstrap write that still fails after every retry is an error. |
| `PurviewAgentFolderSourcePath` | Folder that provides the bundled `.agents` content mirrored into the repository. |
| `PurviewAgentFolderCopyRetries` | Number of copy attempts per file before an agent folder sync failure is reported (default `3`). |
| `PurviewAgentFolderCopyRetryDelayMilliseconds` | Base delay between agent folder copy attempts (default `500`). |
| `PurviewAgentFolderCopyFailureAsError` | When `true` (default), an agent folder copy that still fails after every retry is an error. |
| `PurviewAgentSyncManifestPath` | Change-detection manifest used to skip unchanged agent content (default `<repo root>/.purview/agent-sync.cache`). |
| `PurviewSuppressCopyRetryWarnings` | When `true` (default), built-in copy retry notices (`MSB3026`) are demoted to messages. |
| `PurviewAutoSdkPack` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. |
| `CurrentYear` | Current year used in generated assembly metadata. |
| `AutoGeneratedAssemblyInfoFile` | Relative path to generated AssemblyInfo source file. |

#### Example: switch a repo to Xunit + NSubstitute and disable Bogus

```xml
<Project>
  <PropertyGroup>
    <NamespacePrefix>Acme</NamespacePrefix>
    <TestingFramework>Xunit</TestingFramework>
    <SubstituteFramework>NSubstitute</SubstituteFramework>
    <TestDataFramework>None</TestDataFramework>
  </PropertyGroup>

  <Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
</Project>
```

---

## Test project naming conventions

Test projects are automatically detected by their suffix. Supported patterns:

```text
MyProject.UnitTests       → IsTestProject=true, TestingType=Unit
MyProject.IntegrationTests→ IsTestProject=true, TestingType=Integration
MyProject.E2ETests        → IsTestProject=true, TestingType=E2E
```

Any suffix from the full list is recognised: `Unit`, `Integration`, `E2E`, `EndToEnd`, `Acceptance`, `Functional`, `Performance`, `Load`, `Smoke`, `Stress`, `Regression`, `Security`, `Chaos`, `Scenario`, `System`, `Threat`, `BlackBox`, `WhiteBox`, `Accessibility`, `Interactive`, `Environment`, `Architecture`, `Contract`.

### Shared testing projects

Projects named `SharedTestingFramework`, `SharedTestingInfrastructure`, `SharedTestingInfra`, `SharedTestingUtilities`, `SharedTestingUtils`, `SharedTestingLibrary`, `SharedTestingLib`, or `SharedTestingHelpers` are treated as shared testing helpers — they get test package references but not the test runner or coverage settings.

---

## InternalsVisibleTo

The SDK automatically generates `[assembly: InternalsVisibleTo("…")]` attributes for every non-test C# project. The friend assembly name is derived from the source project's resolved `$(AssemblyName)`, so all naming modes are handled correctly:

- **Explicit `<AssemblyName>`** — if a project sets `<AssemblyName>Custom.Assembly</AssemblyName>`, the generated attributes use `Custom.Assembly.UnitTests`, `Custom.Assembly.IntegrationTests`, etc.
- **Default** — `AssemblyName` is `RootNamespace`-derived, so fully-qualified names are used (e.g. `Acme.MyProject.UnitTests`).
- **`EnableAssemblyNameGeneration=false`** — standard .NET behaviour: `$(MSBuildProjectName)` (e.g. `MyProject.UnitTests`).

Two categories of friend assemblies are generated:

1. **TestType variants** — for each defined `TestType` (`Unit`, `Integration`, `Architecture`, `Contract`, `Functional`, …) the SDK emits the `$(AssemblyName).{TestType}Tests` alias plus the `$(TargetProjectName).{TestType}Tests` and `$(MSBuildProjectName).{TestType}Tests` equivalents, so the attribute resolves regardless of whether naming comes from an explicit `AssemblyName`, the SDK default, or the raw project name.
2. **SharedTesting projects** — one per known shared testing project name (`SharedTestingFramework`, `SharedTestingInfrastructure`, etc.). By default (`EnableAssemblyNameGeneration=true`) with a `NamespacePrefix` set, these are prefixed (e.g. `Acme.SharedTestingFramework`); with `EnableAssemblyNameGeneration=false` the raw name is used.

### Disabling automatic InternalsVisibleTo

To disable automatic InternalsVisibleTo generation, set `DisableAutoInternalsVisibleTo=true` in your project or `Directory.Build.props`:

```xml
<PropertyGroup>
  <DisableAutoInternalsVisibleTo>true</DisableAutoInternalsVisibleTo>
</PropertyGroup>
```

---

## EmbeddedAttribute generation

When `GenerateAssemblyInfoClassTarget` writes the SDK-generated `AssemblyInfo` source, it also emits:

```csharp
namespace Microsoft.CodeAnalysis
{
    sealed partial class EmbeddedAttribute : System.Attribute { }
}
```

This block is guarded by:

```csharp
#if !PURVIEW_SDK_EXCLUDE_EMBEDDED
```

`AssemblyInfo` is emitted with `[Microsoft.CodeAnalysis.Embedded]`, so the project must have a matching `Microsoft.CodeAnalysis.EmbeddedAttribute` type available at compile time. The SDK emits that attribute to satisfy the reference and to keep generated metadata/source-generator-facing symbols marked as embedded.

Define `PURVIEW_SDK_EXCLUDE_EMBEDDED` only when your build already provides `Microsoft.CodeAnalysis.EmbeddedAttribute` from another source; otherwise compilation will fail because the attribute used by generated `AssemblyInfo` cannot be resolved.

---

## Namespace stripping

Certain suffixes are automatically stripped from `RootNamespace` to avoid awkward namespace names like `Acme.MyProject.Core.Something`:

Stripped suffixes: `Core`, `EF`, `Shared`, `ClientShared`, `ServiceDefaults`, and all shared testing project names.

### Extensions namespace rule (`PDS0002`)

When a file is placed under a project-root `Extensions/` folder, the analyzer intentionally treats
that folder as a namespace reset point.

- Scope: only files where the first project-relative segment is exactly `Extensions`
- Expected namespace: derived from subfolders under `Extensions/` (file name is ignored)
- `RootNamespace` is deliberately ignored for these files

Examples:

| Project-relative file path | Expected namespace |
| -- | -- |
| `Extensions/System/StringExtensions.cs` | `System` |
| `Extensions/Microsoft/Extensions/Configuration/ConfigurationExtensions.cs` | `Microsoft.Extensions.Configuration` |
| `Extensions/TopLevel.cs` | *(global namespace)* |

To avoid conflicting guidance, `IDE0130` is suppressed for files in this root `Extensions/` scope.
The shipped `.editorconfig` also suppresses the namespace-conflict diagnostics this convention can
trigger (`CA1724`, `CS0436`, `CS1591`, `IDE0005`), so extension files never need `#pragma` suppressions.
Outside this scope, normal `IDE0130` behaviour remains unchanged.

A code refactoring (`Split extensions class into one class per receiver type`) is offered when a
static extensions class targets multiple receiver types. It splits the class into one
`<ReceiverType>Extensions` class per receiver — for a generic `this TBuilder where TBuilder : IHostApplicationBuilder`
receiver that becomes `HostApplicationBuilderExtensions` — and places each new file under
`Extensions/<receiver namespace>/` so the namespace convention above stays satisfied. When a
`<ReceiverType>Extensions` class already exists in the receiver's namespace, the receiver's methods are
merged into it instead of creating a duplicate file.

A second refactoring (`Move extensions class to conventional location`) is offered when a static
extensions class targets a single receiver type but is not already at its conventional location (or is
at that location but not conventionally named). It re-paths the file to
`Extensions/<receiver namespace>/<Type>Extensions.cs` and renames the class to the receiver-derived
name (e.g. an `IServiceCollection` extension becomes `ServiceCollectionExtensions` and moves to
`Microsoft.Extensions.DependencyInjection`). When a `<Type>Extensions` class already exists in the
receiver's namespace it is merged into that class instead of creating a duplicate — every member from
both classes (constants, private helpers, XML docs) is preserved, only members with a matching
signature are skipped. The namespace is fixed, a `using` for the previous namespace is added to the
moved file, and a `using` for the new namespace plus the renamed type name are applied to every other
document that references the type, so the move compiles everywhere.

---

## SDK-shipped analyzers

The package ships `Purview.BuildSdk.Analyzers.dll` (plus a separate code-fix assembly for the IDE)
and adds the analyzer to every C# project as an `<Analyzer>` item, so the rules surface in both
command-line builds and Visual Studio.

| Rule | Category | Severity | Description |
| -- | -- | -- | -- |
| `PDS0001` | (suppressor) | — | Suppresses `CS1591` for `EditorBrowsable(Never)` members |
| `PDS0002` | Naming | Warning | Files under a project-root `Extensions/` folder reset their namespace |
| `PDS0003` | Style | Warning | Prefer an explicit type with target-typed `new()` over `var` |
| `PDS0004` | Naming | Warning | Use correct acronym capitalization (`Api` → `API`) |

`PDS0004` follows .NET naming guidance for well-known framework spellings (`Sql`, `Guid`, `Uuid`, `Url`,
`Dns`, `Http`, `Xml`, `DbContext`, ...) while still enforcing uppercase for acronyms such as `Api` → `API`,
`Ai` → `AI`, `Cpu` → `CPU`, `Gpu` → `GPU`, `Cli` → `CLI`, `Gui` → `GUI`, `Ram` → `RAM`, and
`Ssh` → `SSH`. By default the acronym-like segments `Http`, `Xml`, `Json`, `Id`, `Sdk`, `Sql`, `Uuid`,
`Url`, `Dns`, `Tcp`, `Udp`, `Csv`, `Pdf`, `Html`, `Css`, `Ftp`, `Smtp`, `Imap`, and `Db` are exempt —
`Db` is deliberately exempt so the prevalent EF Core/ADO.NET spellings (`DbContext`, `DbConnection`,
`DbSet`, `CreateDbContext`) are never flagged; a repo that prefers `DB` can re-enable it via
`acronym_map = Db:DB`. All options are customisable per repo, project, or folder via `.editorconfig` and
**merge with the shipped defaults — config entries override them** (so you can opt into `Sql` → `SQL`
or opt out of `Cli` → `CLI` without re-declaring every default):

- `dotnet_analyzer_configuration.pds0004.allowed_words` — semicolon-separated segments that are never flagged.
- `dotnet_analyzer_configuration.pds0004.acronym_map` — semicolon-separated `Key:Value` corrections.
- `dotnet_analyzer_configuration.pds0004.allowed_identifiers` — semicolon-separated whole identifiers that
  are never flagged. Matched in the order listed (first match wins) by exact name or word-boundary prefix,
  so a brand name like `CosmosDb` also covers `CosmosDbServer`/`CosmosDbContext`.

Members whose names are mandated by a contract — interface implementations (implicit or explicit) and
base-class overrides — are never renamed, since doing so would break the contract.

```ini
[*.cs]
# Optional overrides; everything not mentioned keeps its shipped default.
dotnet_analyzer_configuration.pds0004.allowed_words = Cli
dotnet_analyzer_configuration.pds0004.acronym_map = Db:DB
dotnet_analyzer_configuration.pds0004.allowed_identifiers = ICosmosDBService;CosmosDb;GitHub;YouTube
```

The code fix for `PDS0004` renames the identifier and all of its references across the solution.

---

## Assembly name generation

By default (`EnableAssemblyNameGeneration=true`), the SDK treats `RootNamespace` as the canonical public name: `AssemblyName` and `PackageId` both default to the fully evaluated `RootNamespace` — or, when suffix-stripping removed a segment of the logical project name, to the full logical project name so assemblies stay distinct. The defaults are applied during `Sdk.props` evaluation — before the Microsoft SDK computes `TargetName` and before the project body — so compilation, output paths, project references, restore, and packing all agree on the same identities. Set `EnableAssemblyNameGeneration=false` **before the SDK import** to opt out and fall back to standard .NET behaviour (`$(MSBuildProjectName)`).

With the default enabled:

| Project name | `NamespacePrefix` | `RootNamespace` | Resolved `AssemblyName` / `PackageId` |
| -- | -- | -- | -- |
| `Api` | `Acme` | `Acme.Api` | `Acme.Api` |
| `Acme.Api` | `Acme` | `Acme.Api` | `Acme.Api` (no double-prefix) |
| `Core.Infrastructure` | `Acme` | `Acme.Infrastructure` | `Acme.Core.Infrastructure` (`.Core` stripped from the namespace only) |
| `Shared` | `Acme` | `Acme` | `Acme.Shared` (full logical name, so it stays distinct) |
| `ServiceDefaults` | `Acme` | `Acme` | `Acme.ServiceDefaults` (full logical name, so it stays distinct) |
| `Acme` | `Acme` | `Acme` | `Acme` |

Explicitly setting `<RootNamespace>` before the SDK import always wins: `AssemblyName`/`PackageId` follow that override instead of re-appending a stripped suffix.

Test projects keep their detected suffix: `Api.UnitTests` → `AssemblyName`/`PackageId` = `Acme.Api.UnitTests`, while `RootNamespace` remains `Acme.Api`.

Explicit `<AssemblyName>` or `<PackageId>` in a `.csproj` (or `Directory.Build.props`) always takes precedence. Because the defaults run before the project body, project-authored values set in the body are evaluated later and win.

> **Note:** set `EnableAssemblyNameGeneration=false` **before** the SDK import (for example in `Directory.Build.props`) — it is consumed during `Sdk.props` evaluation.

---

## Central Package Management

The SDK sets `ManagePackageVersionsCentrally=true`. The `templates/Directory.Packages.props` file contains `PackageVersion` entries for all packages the SDK auto-adds — all set to `Version="*"` (floating to latest).

To pin a package:

```xml
<PackageVersion Include="TUnit" Version="1.45.29" />
```

To add project-specific packages, just append `PackageVersion` entries to your `Directory.Packages.props`.

---

## Documentation

- [Homepage](https://purview.dev/projects/build-sdk/)
- [Documentation](https://purview.dev/docs/build-sdk/)

## Building the SDK

```sh
dotnet build src/BuildSdk.slnx -c Release
dotnet test src/BuildSdk.slnx -c Release
dotnet pack src/src/BuildSdk/BuildSdk.csproj -o ./artifacts
```

## License

MIT
