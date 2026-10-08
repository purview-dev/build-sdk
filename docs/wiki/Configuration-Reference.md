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
| `DisablePurviewStylePolicyValidation` | `false` | Set to `true` to stop `ValidatePurviewStylePolicy` failing the build (`PRSGD0006`-`PRSGD0009`) when the repository `.editorconfig` overrides `dotnet_style_require_accessibility_modifiers`, hides `IDE0040`/`IDE1006`, disables the Style category in bulk, weakens the `_camelCase` private instance field rule, or adds the accessibility rules to `NoWarn`. Entries the SDK injects itself (the test-context rule set, `CA1515` for Aspire hosts and CLI apps) are ignored. See [Style policy](Analyzers.md#style-policy). |
| `PurviewTestContextNoWarn` | `CA1002;CA1012;CA1034;CA1047;CA1050;CA1051;CA1062;CA1064;CA1515;CA1707` | Production API-surface rules exempted in test and shared-testing projects (they keep the strict style contract). Override before the SDK import to narrow or extend the list. |
| `DisablePurviewTestContextRuleSet` | `false` | Set to `true` to make test and shared-testing projects enforce the production API-surface rules as well. |
| `PurviewSharedTestingOutputType` | `Library` | Output type forced on `IsSharedTestingProject` projects; `Library` also clears `IsTestProject`/`IsTestingPlatformApplication`. Set to `Exe` before the SDK import to keep the test packages' executable/test-host shape. |
| `TargetFramework` | `net10.0` | Override the default TFM per-project or globally. Defaults to `netstandard2.0` for projects declaring `IsRoslynComponent=true`. To multi-target from a central, overridable definition rather than a hard-coded list, see [Target framework sets](#target-framework-sets). |
| `DisableUnconditionalProjectDeclarationCheck` | `false` | Set to `true` to suppress `PurviewConditionedProjectDeclaration`. The SDK reads `TargetFramework`, `TargetFrameworks`, `IsRoslynComponent`, `IsRoslynComponentOnly`, `IsPackable` and `PurviewTargetFrameworkSet` from the project XML before the project body is evaluated, so it cannot evaluate a `Condition` on them and will not see the declaration — the SDK default applies instead. Declare those properties unconditionally, or set them in a file imported before this SDK. |
| `IsRoslynComponent` | `false` | When explicitly `true`, applies source-generator defaults: a single `netstandard2.0` target, `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, extended analyzer rules, SourceLink with `EmbedUntrackedSources=true`, compiler-generated output under the intermediate directory, no dependency file, symbol packaging (`IncludeSymbols=false` by default), telemetry exclusion, and package build output. Packable Roslyn components automatically pack the built analyzer assembly and its PDB into `analyzers/dotnet/cs/` (`PurviewPackAnalyzerPdb=true`; set `false` only when symbols are delivered another way — NuGet's `.snupkg` cannot host `analyzers/dotnet/cs` symbols). Pack-time validation (`ValidateRoslynComponentCompilerSettings`) fails the pack if the compiler defaults are missing unless `DisableRoslynCompilerDefaultsValidation=true`. Roslyn development dependencies (`Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Analyzers`) default to `PrivateAssets="all"`. |
| `PackProjectReferencedSourceGenerators` | `true` | Automatically packs analyzer `ProjectReference` outputs and their runtime dependencies under `analyzers/dotnet/cs/`. Set to `false` to opt out; set `Pack="false"` on an individual reference to exclude only that generator. |
| `SourceLinkPackageName` | `Microsoft.SourceLink.GitHub` | SourceLink provider. Set to `Microsoft.SourceLink.AzureDevOps.Git` for ADO repos. |
| `DisableSourceLink` | `false` | Set to `true` to stop the SDK from adding the configured SourceLink package automatically. |
| `EnableAssemblyNameGeneration` | `true` | When `true` (default), `AssemblyName` and `PackageId` derive from the fully evaluated `RootNamespace`. When explicitly `false`, standard .NET behaviour applies (`$(MSBuildProjectName)`). Explicit `<AssemblyName>`/`<PackageId>` in a `.csproj` always take precedence. |
| `DisableProjectFileNamingConventionCheck` | `false` | Set to `true` to disable the validation that requires `MyProject\MyProject.csproj` naming alignment. |
| `DisableGenerateAssemblyInfoClass` | `false` | Set to `true` to disable the generated `AssemblyInfo` helper source. |
| `AutoIncludeUsings` | `true` | Controls SDK-added global usings for `NamespacePrefix` and `RootNamespace`. |
| `NamespaceRemoveSuffix` | *(built-in list)* | Item type listing the suffixes stripped from `RootNamespace`. Remove an entry **after** the `Sdk.props` import to keep that suffix in the namespace, e.g. `<NamespaceRemoveSuffix Remove="Abstractions" />`. **Roslyn component names are not in the list** — `SourceGenerator`, `SourceGenerators`, `SourceGeneration`, `Generators`, `Analyzers`, `CodeFixers` and `CodeFixes` are part of a component's identity, and a generator, its analyzers and its code fixes are separate assemblies that each need their own namespace. Add one with `<NamespaceRemoveSuffix Include="Analyzers" />` if a repository really wants it collapsed. See [Namespace stripping](Assembly-Name-Generation.md#namespace-stripping). |

## Target framework sets

A multi-targeting repository otherwise hard-codes its TFM list in every project, so a lifecycle change —
a release leaving support, a new one arriving — is an edit in every repository that has to be found and
kept consistent. Name a set instead and the decision moves into the SDK, so bumping `Purview.BuildSdk`
moves every consumer at once:

```xml
<PropertyGroup>
  <PurviewTargetFrameworkSet>Supported</PurviewTargetFrameworkSet>
</PropertyGroup>
```

| Set | Resolves to | Use for |
| -- | -- | -- |
| `Current` | `net10.0` | The default. A single target, stated explicitly. |
| `Latest` | `net11.0` | Newest release only. |
| `Supported` | `net10.0;net11.0` | A package that should work on anything still in support. |
| `Broad` | `net8.0;net9.0;net10.0` | Every shipped release — widest reach without committing to the newest major while it is pre-GA. |
| `All` | `net8.0;net9.0;net10.0;net11.0` | `Broad` plus the newest major. |

`net8.0` and `net9.0` are deliberately outside `Supported`: both are at or near end of support, so a new
package should not imply a commitment to them. Choose `Broad` to keep them.

`Broad` and `All` differ by one real product decision — whether the package commits to the newest
release — so they are separate names rather than one set that silently widens every consumer of it.

A set is **always** in play: `Current` applies when nothing is selected, which resolves to the same
single TFM the SDK used to hard-code. The out-of-box result is therefore unchanged — but the value is
named and overridable in one place, so moving a repository (or every repository) between runtimes is a
property change rather than an edit in each project.

Overrides are layered, narrowest first:

1. an explicit `TargetFramework`/`TargetFrameworks` in the project always wins. Like the set selection
   itself, this is read from the project XML, so it must be declared **unconditionally** to be seen;
2. a `PurviewTargetFrameworkSet` in the project wins over one the repository selected;
3. a repository can redefine any set with `PurviewTargetFrameworks<Set>` — `PurviewTargetFrameworksSupported`,
   `PurviewTargetFrameworksBroad`, and so on — set before the SDK import. This is also how a set is pinned
   while a consumer is not ready to follow the SDK's definition;
4. otherwise the table above applies.

A project declaring `TargetFrameworks` keeps `TargetFramework` empty, as a multi-targeting project must:
MSBuild honours the singular property ahead of the list, so a default left in place there would quietly
collapse the project to a single target and break every multi-targeting consumer of it.

| Property | Default | Description |
| -- | -- | -- |
| `PurviewTargetFrameworkSet` | `Current` | `Current`, `Latest`, `Supported`, `Broad` or `All`. An unrecognised name fails the build with `PurviewInvalidTargetFrameworkSet` rather than silently falling back to the single-TFM default. A single-entry set resolves to `TargetFramework`, so it does not pay for an outer multi-targeting build. |
| `PurviewTargetFrameworksCurrent` | `net10.0` | Redefines the `Current` set. |
| `PurviewTargetFrameworksLatest` | `net11.0` | Redefines the `Latest` set. |
| `PurviewTargetFrameworksSupported` | `net10.0;net11.0` | Redefines the `Supported` set. |
| `PurviewTargetFrameworksBroad` | `net8.0;net9.0;net10.0` | Redefines the `Broad` set. |
| `PurviewTargetFrameworksAll` | `net8.0;net9.0;net10.0;net11.0` | Redefines the `All` set. |

Roslyn components are not covered: a component targets `netstandard2.0` so every compiler host can load
it. Selecting a set on one is ignored and reported as `PurviewTargetFrameworkSetIgnored`.

Set the property in `Directory.Build.props` for a repository-wide choice, or in a `.csproj` for a single
project. Because the SDK is imported before the project body is evaluated, a `.csproj` selection is read
from the project XML directly — so declare it **unconditionally**. A `Condition` on it cannot be evaluated
that early and is reported as `PurviewConditionedProjectDeclaration`.

## Repository metadata

The SDK also supports repo/site metadata properties for generated package metadata and docs links. These
values are consumed before the import and are useful when a repository wants a single shared project URL
or docs base URL instead of repeating it per project:

| Property | Default | Description |
| -- | -- | -- |
| `PurviewHomepage` | *(repo-specific)* | Base site URL for the repository or product docs. In this repo it is set to `https://purview.dev/`. |
| `PurviewProjectUrl` | *(repo-specific)* | Public project page URL; usually derived from `PurviewHomepage` plus a project path. |
| `PurviewDocsUrl` | *(repo-specific)* | Public docs URL; often derived from `PurviewHomepage` plus a docs path. |

These values are not mandatory, but they provide a consistent place to keep package metadata and docs
links aligned with the repo's site structure.

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
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing` never touches an existing file, `Always` overwrites it, `WarnOnDrift` reports that an existing file differs from the SDK-provided one, and `Never` skips bootstrapping entirely. |
| `PurviewRepoBootstrapCopyRetries` | `3` | Copy attempts before a bootstrap write failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between bootstrap write attempts. |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a bootstrap write that still fails after every retry is reported as a warning and the build continues. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes MSB3026 copy retry notices to messages. Set to `false` to see every retry attempt. |

Bootstrap writes are staged into a temporary file and renamed into place, so editors and tools never
observe a partially written `.editorconfig` or `global.json`. Because every project in a solution runs
the same bootstrapping targets, a lost race between parallel projects is a no-op: an existing file is
treated as success rather than a copy failure.

## Agent folder

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAutoSdkPack` | `true` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. Disable this for MSBuild SDK projects. |
| `EnableAgentFolderInPackage` | `true` | Mirrors the bundled `.agents/**` folder from the SDK NuGet package into the consuming repository's `.agents/` folder (or `$(AgentPackDestinationFolder)/`) before build. |
| `AgentPackDestinationFolder` | `.agents` | Repo-relative destination folder that receives the mirrored agent folder contents when `EnableAgentFolderInPackage` is `true`. |
| `PurviewAgentFolderSourcePath` | *(package-level `.agents`)* | Overrides the folder that provides the bundled `.agents` content. |
| `PurviewAgentFolderCopyRetries` | `3` | Copy attempts per file before the failure is reported. |
| `PurviewAgentFolderCopyRetryDelayMilliseconds` | `500` | Base delay between copy attempts. |
| `PurviewAgentFolderCopyFailureAsError` | `true` | When `false`, a copy that still fails after every retry is reported as a warning and the build continues. |
| `PurviewAgentSyncManifestPath` | `<repo root>/.purview/agent-sync.cache` | Overrides the change-detection manifest used to skip unchanged agent content. |

The mirror is change-aware: content that already matches the manifest is skipped entirely, which keeps
repeat builds free of file writes and file locks. Retry notices are demoted to low-importance messages
(`MSB3026`); a copy that still fails after every retry is reported as an error that names the source,
destination and OS error.

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

See [Engineering Principles](Engineering-Principles.md) for the policy-level testing guidance and
[Testing Wiring](Testing-Wiring.md) for the implementation details.

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
| `IsSharedTestingProject` | True for known shared testing helper project names; the project is forced to `OutputType=Library` (see `PurviewSharedTestingOutputType`). |
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
