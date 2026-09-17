# Configuration Reference

Set any of these properties **before** the `<Import>` in your `Directory.Build.props` (most are
consumed during `Sdk.props` evaluation). Explicit values set before the import are always preserved.

## Version detection

| Property | Default | Description |
| -- | -- | -- |
| `UsePackageJsonVersion` | `true` | `true` enables version detection, `false` disables it, and `Strict` requires version detection to succeed (build fails if no version source can be resolved). |
| `RootPackageJson` | *(auto-discovered)* | Explicit path to a `package.json`. Relative paths are resolved from the project directory. |
| `EnableVersionDetectionCache` | `true` | Enables local caching of auto-discovered package.json version results. |
| `VersionDetectionLogEnabled` | `false` | Emits a high-importance message showing the detected package version. |

See [Version Detection](Version-Detection.md) for the full resolution rules.

## General

| Property | Default | Description |
| -- | -- | -- |
| `NamespacePrefix` | *(required)* | Root namespace prefix, e.g. `Acme`. Results in `Acme.MyProject`. |
| `DisableNamespacePrefixCheck` | `false` | Set to `true` to suppress the build error for missing `NamespacePrefix`. |
| `TargetFramework` | `net10.0` | Override the default TFM per-project or globally. Defaults to `netstandard2.0` for projects declaring `IsRoslynComponent=true`. |
| `IsRoslynComponent` | `false` | When explicitly `true`, applies source-generator defaults: a single `netstandard2.0` target, `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, extended analyzer rules, SourceLink with `EmbedUntrackedSources=true`, compiler-generated output under the intermediate directory, no dependency file, symbol packaging (`IncludeSymbols=false` by default), telemetry exclusion, and package build output. Packable Roslyn components automatically pack the built analyzer assembly and its PDB into `analyzers/dotnet/cs/` (`PurviewPackAnalyzerPdb=true`; set `false` only when symbols are delivered another way — NuGet's `.snupkg` cannot host `analyzers/dotnet/cs` symbols). Pack-time validation (`ValidateRoslynComponentCompilerSettings`) fails the pack if the compiler defaults are missing unless `DisableRoslynCompilerDefaultsValidation=true`. Roslyn development dependencies (`Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Analyzers`) default to `PrivateAssets="all"`. |
| `PackProjectReferencedSourceGenerators` | `true` | Automatically packs analyzer `ProjectReference` outputs and their runtime dependencies under `analyzers/dotnet/cs/`. Set to `false` to opt out; set `Pack="false"` on an individual reference to exclude only that generator. |
| `SourceLinkPackageName` | `Microsoft.SourceLink.GitHub` | SourceLink provider. Set to `Microsoft.SourceLink.AzureDevOps.Git` for ADO repos. |
| `DisableSourceLink` | `false` | Set to `true` to stop the SDK from adding the configured SourceLink package automatically. |
| `EnableAssemblyNameGeneration` | `true` | When `true` (default), `AssemblyName` and `PackageId` derive from the fully evaluated `RootNamespace`. When explicitly `false`, standard .NET behaviour applies (`$(MSBuildProjectName)`). Explicit `<AssemblyName>`/`<PackageId>` in a `.csproj` always take precedence. |
| `DisableProjectFileNamingConventionCheck` | `false` | Set to `true` to disable the validation that requires `MyProject\MyProject.csproj` naming alignment. |
| `DisableGenerateAssemblyInfoClass` | `false` | Set to `true` to disable the generated `AssemblyInfo` helper source. |
| `AutoIncludeUsings` | `true` | Controls SDK-added global usings for `NamespacePrefix` and `RootNamespace`. |

## Packable project defaults

For projects where `IsPackable=true`, the SDK provides these defaults **only when the consuming
project has not supplied a value**:

| Property | Default | Description |
| -- | -- | -- |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. |
| `IncludeSymbols` | `true` | Produces a symbol package (`false` for Roslyn components — their PDB ships inside the `.nupkg` under `analyzers/dotnet/cs/` via `PurviewPackAnalyzerPdb=true`). |
| `SymbolPackageFormat` | `snupkg` | Symbol package format. Always the modern `.snupkg`; the legacy `.symbols.nupkg` is never produced by default. |
| `PublishRepositoryUrl` | `true` | Publishes the repository URL. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources for SourceLink. |
| `DebugType` | `portable` | Ensures portable PDBs for symbol-package delivery. |
| `IncludeSource` | `true` | Includes source files in the package. |

Portable PDBs are delivered through the `.snupkg`; the normal `.nupkg` does **not** receive PDB files
unless the project explicitly opts in (for example by adding `.pdb` to
`AllowedOutputExtensionsInPackageBuildOutputFolder`).

The SDK never forces organization/package-specific metadata — `Authors`, `Company`,
`PackageLicenseExpression`, `PackageLicenseFile`, `Description`, `PackageTags`, `PackageProjectUrl`,
and repository URLs are left to the repository or individual package. `IsPackable` is not set blindly:
it defaults to `false` and only becomes `true` when a project explicitly opts in.

Non-packable projects default `WarnOnPackingNonPackableProject=false`, so solution-wide pack
operations skip them silently.

## Repo bootstrap

| Property | Default | Description |
| -- | -- | -- |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |
| `BootstrapEditorConfigToRepoRoot` | `true` | Copies the SDK `.editorconfig` to the repository root when missing. |
| `RepositoryEditorConfigFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `.editorconfig`. |
| `BootstrapGlobalJsonToRepoRoot` | `true` | Creates a `global.json` at the repository root when missing. |
| `RepositoryGlobalJsonFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `global.json`. |
| `PurviewBuildSdkVersionForGlobalJson` | *(auto-detected or `1.0.0` fallback)* | Version written to the `msbuild-sdks.Purview.BuildSdk` entry in a bootstrapped `global.json`. |

## Agent folder

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAutoSdkPack` | `true` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. Disable this for MSBuild SDK projects. |
| `EnableAgentFolderInPackage` | `true` | Copies the bundled `.agents/**` folder from the SDK NuGet package into the consuming repository's `.agents/` folder (or `$(AgentPackDestinationFolder)/`) before build. |
| `AgentPackDestinationFolder` | `.agents` | Repo-relative destination folder that receives the copied agent folder contents when `EnableAgentFolderInPackage` is `true`. |

To disable bundled agent folder copying in a consuming repo, set the opt-out property before
importing the SDK:

```xml
<PropertyGroup>
  <EnableAgentFolderInPackage>false</EnableAgentFolderInPackage>
</PropertyGroup>
```

## Telemetry

| Property | Default | Description |
| -- | -- | -- |
| `ExcludePurviewTelemetry` | `false` | Set to `true` to exclude `Purview.Telemetry.SourceGenerator` from all projects. |
| `ExcludeMSTelemetryExtension` | `false` | Set to `true` to exclude `Microsoft.Extensions.Telemetry.Abstractions`. Only relevant when `ExcludePurviewTelemetry` is also `false` — when `ExcludePurviewTelemetry=true` the whole telemetry group is skipped anyway. |

## Testing

| Property | Default | Description |
| -- | -- | -- |
| `TestingFramework` | `TUnit` | Testing framework. Supported values: `TUnit`, `Xunit`, `None`. |
| `SubstituteFramework` | `TUnitMocks` | Mocking provider. Supported values: `TUnitMocks`, `NSubstitute`, `None`. |
| `TestDataFramework` | `Bogus` | Test data provider. Supported values: `Bogus`, `None`. |
| `DisableAutoInternalsVisibleTo` | `false` | Set to `true` to disable automatic `InternalsVisibleTo` generation for test types and shared testing projects. |

See [Testing Wiring](Testing-Wiring.md) for details.

## Compiler-visible SDK properties

The SDK exports its properties via `CompilerVisibleProperty`, so analyzers and source generators can
read them through `build_property.<PropertyName>`:

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
| `DisableSourceLink` | Disables automatic SourceLink integration. |
| `ExcludePurviewTelemetry` | Opt-out for `Purview.Telemetry.SourceGenerator`. |
| `ExcludeMSTelemetryExtension` | Opt-out for `Microsoft.Extensions.Telemetry.Abstractions`. |
| `EnableAgentFolderInPackage` | When `true`, copies the bundled `.agents` folder into the consuming repository. |
| `AgentPackDestinationFolder` | Repo-relative destination folder that receives copied `.agents` content. |
| `PurviewAutoSdkPack` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package. |
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
| `IsCLIProject` | True when the project is a CLI project. |
| `IsSharedProject` | True when the project is a shared project. |
| `EditorConfigFilePath` | Path to the SDK-provided `.editorconfig` injected into `@(EditorConfigFiles)`. |
| `RepositoryEditorConfigFilePath` | Destination path for bootstrapping a physical repo-level `.editorconfig`. |
| `BootstrapEditorConfigToRepoRoot` | When `true` (default), copies the SDK `.editorconfig` to `RepositoryEditorConfigFilePath` if missing. |
| `RepositoryGlobalJsonFilePath` | Destination path for bootstrapping a physical repo-level `global.json`. |
| `BootstrapGlobalJsonToRepoRoot` | When `true` (default), creates `global.json` at `RepositoryGlobalJsonFilePath` if missing. |
| `PurviewBuildSdkVersionForGlobalJson` | Version used for `msbuild-sdks.Purview.BuildSdk` when bootstrapping `global.json`. |
| `DisableAutoCopySdkFiles` | When `true`, disables SDK auto-copy/bootstrap for repo files (`.editorconfig`, `global.json`). |
| `CurrentYear` | Current year used in generated assembly metadata. |
| `AutoGeneratedAssemblyInfoFile` | Relative path to generated AssemblyInfo source file. |

Roslyn components additionally expose `LangVersion`, `Nullable`, `TreatWarningsAsErrors`,
`EnforceExtendedAnalyzerRules`, `Deterministic`, `ContinuousIntegrationBuild`, and
`EmbedUntrackedSources` as compiler-visible properties so downstream tooling can confirm the shipped
analyzer packages were built with the standard settings.

## Example: switch a repo to Xunit + NSubstitute and disable Bogus

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