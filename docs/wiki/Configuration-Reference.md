# Configuration Reference

Setting a property in `Directory.Build.props` **before** the `<Import>` always works. A property whose
description says *set it before the `Sdk.props` import* only works there; the rest can also be set in a
project file. Every property here is described in the SDK's [property schema](configuration/Property-Schema.md), so
the same text appears in csproj IntelliSense.

## Required settings and versions

Four values have to be set correctly for the SDK to behave as documented. `NamespacePrefix`, and
`RootPackageJson` when you name the version file explicitly, are read during `Sdk.props` evaluation, so
they must be set **before** the `<Import>`.

| Setting | Where | Notes |
| -- | -- | -- |
| `NamespacePrefix` | `Directory.Build.props` (pre-import) | Required. Without it the build fails (`ValidateRootNamespacePrefixTarget`) unless `DisableNamespacePrefixCheck=true`. |
| SDK version | `global.json` → `msbuild-sdks.Purview.BuildSdk` | Pinned to a released version. Bootstrapped when missing; `PurviewBuildSdkVersionForGlobalJson` decides the value written. |
| Product version | repo-root `package.json` → `version` | Applied to `Version` and `PackageVersion` by default. A git repository with no root `package.json` fails the build: add one, or set `UsePackageJsonVersion=false`. `UsePackageJsonVersion=Strict` also fails when no repository root is found, instead of falling back to `0.0.1`. Set `RootPackageJson` (pre-import) to name the file explicitly. |
| Package versions | `Directory.Packages.props` | Central Package Management is on; a `PackageVersion` is a minimum, not an exact pin. |

## Version detection

| Property | Default | Description |
| -- | -- | -- |
| `UsePackageJsonVersion` | `true` | `true` enables version detection, `false` disables it, and `Strict` requires version detection to succeed (build fails if no version source can be resolved). |
| `RootPackageJson` | *(auto-discovered)* | Explicit path to a `package.json`. A relative path resolves from the project directory, so in `Directory.Build.props` prefer `$(MSBuildThisFileDirectory)package.json`. |
| `EnableVersionDetectionCache` | `true` | Enables local caching of auto-discovered package.json version results. |
| `VersionDetectionLogEnabled` | `false` | Emits a high-importance message showing the detected package version. |
| `VersionDetectionCacheFile` | *(one file per project directory under the temporary directory)* | File the package.json version detection cache is written to. Set it before the `Sdk.props` import. |

See [Version Detection](configuration/Version-Detection.md) for the full resolution rules.

## General

| Property | Default | Description |
| -- | -- | -- |
| `NamespacePrefix` | *(required)* | Root namespace prefix, e.g. `Acme`. Results in `Acme.MyProject`. |
| `DisableNamespacePrefixCheck` | `false` | Set to `true` to suppress the build error for missing `NamespacePrefix`. |
| `DisablePurviewStylePolicyValidation` | `false` | Set to `true` to stop `ValidatePurviewStylePolicy` failing the build (`PRSGD0006`-`PRSGD0009`) when the repository `.editorconfig` overrides `dotnet_style_require_accessibility_modifiers`, hides `IDE0040`/`IDE1006`, disables the Style category in bulk, weakens the `_camelCase` private instance field rule, or adds the accessibility rules to `NoWarn`. Entries the SDK injects itself (the test-context rule set, `CA1515` for Aspire hosts and CLI apps) are ignored. See [Style policy](analyzers/Analyzers.md#style-policy). |
| `PurviewTestContextNoWarn` | `CA1002;CA1012;CA1034;CA1047;CA1050;CA1051;CA1062;CA1064;CA1515;CA1707` | Production API-surface rules exempted in test and shared-testing projects (they keep the strict style contract). Override before the SDK import to narrow or extend the list. |
| `DisablePurviewTestContextRuleSet` | `false` | Set to `true` to make test and shared-testing projects enforce the production API-surface rules as well. |
| `PurviewSharedTestingOutputType` | `Library` | Output type forced on `IsSharedTestingProject` projects; `Library` also clears `IsTestProject`/`IsTestingPlatformApplication`. Set to `Exe` (in the project or `Directory.Build.props`) to keep the test packages' executable/test-host shape. |
| `TargetFramework` | `net10.0;net11.0` | Override the default TFMs per-project or globally. Defaults to `netstandard2.0` for projects declaring `IsRoslynComponent=true`. To multi-target from a central, overridable definition rather than a hard-coded list, see [Target framework sets](#target-framework-sets). |
| `DisableUnconditionalProjectDeclarationCheck` | `false` | Set to `true` to suppress `PurviewConditionedProjectDeclaration`. The SDK reads `IsRoslynComponent`, `IsRoslynComponentOnly`, `IsPackable`, `PurviewTargetFrameworkSet` and `RootNamespace` from the project XML before the project body is evaluated, so it cannot evaluate a `Condition` on them and will not see the declaration — the SDK default applies instead. Declare those properties unconditionally, or set them in a file imported before this SDK. |
| `IsRoslynComponent` | `false` | When explicitly `true`, applies source-generator defaults: a single `netstandard2.0` target, `LangVersion=latest`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, extended analyzer rules, SourceLink with `EmbedUntrackedSources=true`, compiler-generated output under the intermediate directory, no dependency file, symbol packaging (`IncludeSymbols=false` by default), telemetry exclusion, and package build output. Packable Roslyn components automatically pack the built analyzer assembly and its PDB into `analyzers/dotnet/cs/` (`PurviewPackAnalyzerPdb=true`; set `false` only when symbols are delivered another way — NuGet's `.snupkg` cannot host `analyzers/dotnet/cs` symbols). Pack-time validation (`ValidateRoslynComponentCompilerSettings`) fails the pack if the compiler defaults are missing unless `DisableRoslynCompilerDefaultsValidation=true`. Roslyn development dependencies (`Microsoft.CodeAnalysis.*`, `Microsoft.CodeAnalysis.Analyzers`) default to `PrivateAssets="all"`. |
| `PackProjectReferencedSourceGenerators` | `true` | Automatically packs analyzer `ProjectReference` outputs and their runtime dependencies under `analyzers/dotnet/cs/`. Set to `false` to opt out; set `Pack="false"` on an individual reference to exclude only that generator. |
| `SourceLinkPackageName` | `Microsoft.SourceLink.GitHub` | SourceLink provider package, added (with `PrivateAssets="all"`) to every non-test project. Match the repository host: `Microsoft.SourceLink.AzureRepos.Git` for Azure DevOps Services, `Microsoft.SourceLink.AzureDevOpsServer.Git` for on-premises Azure DevOps Server / TFS, `Microsoft.SourceLink.GitLab`, `Microsoft.SourceLink.Bitbucket.Git`. Set it in `Directory.Build.props` so every project agrees. See [SourceLink](packaging/SourceLink.md). |
| `DisableSourceLink` | `false` | Set to `true` to stop the SDK from adding the SourceLink package at all. An empty `SourceLinkPackageName` does not opt out; the SDK fills it with the GitHub default. |
| `EnableAssemblyNameGeneration` | `true` | When `true` (default), `AssemblyName` and `PackageId` derive from the fully evaluated `RootNamespace`. When explicitly `false`, standard .NET behaviour applies (`$(MSBuildProjectName)`). Explicit `<AssemblyName>`/`<PackageId>` in a `.csproj` always take precedence. |
| `DisableProjectFileNamingConventionCheck` | `false` | Set to `true` to disable the validation that requires `MyProject\MyProject.csproj` naming alignment. |
| `DisableGenerateAssemblyInfoClass` | `false` (`true` for test and shared-testing projects) | Set to `true` to disable the generated `AssemblyInfo` helper source. |
| `DisableGenerateAssemblyIdentifierClass` | `false` (`true` for test and shared-testing projects) | Set to `true` to disable the generated assembly identifier marker class. See [Assembly identifier class](concepts/Assembly-Name-Generation.md#assembly-identifier-class). |
| `AssemblyIdentifierClassName` | `<project name minus NamespacePrefix, dots removed>Assembly` | Overrides the name of the generated assembly identifier marker class. |
| `AutoIncludeUsings` | `true` | Controls SDK-added global usings for `NamespacePrefix` and `RootNamespace`. |
| `MemorySafetyRules` | `0` | C# 15 memory-safety tier on `net11.0`+ targets: `0` off (default), `1` preview pointer syntax (sets `LangVersion=preview` when the project has not chosen one), `2` updated rules (`updated-memory-safety-rules`). The updated rules are an experimental compiler feature, so tier `2` is opt-in. An unrecognised value fails with `PRCFG0001`. `AllowUnsafeBlocks` is never changed. |
| `PurviewRuntimeAsync` | *(unset, which is treated as false)* | Set to `true` to add `<Features>runtime-async=on</Features>` on `net11.0`+ targets. `UseRuntimeAsync=true` also opts in; an explicit `false` under either name keeps it off. |
| `IsClientSideProject` | *(detected)* | `true` when the project is downloaded to a browser: a WebAssembly client, a `PurviewBrowserSafe` library, a Razor class library, or a `*.Client`/`*.ClientShared`/`*.Browser`/`*.Wasm`/`*.WebAssembly`/`*.Components` (or `ClientShared`/`Components`) name. Set explicitly to override the detection either way. |
| `PurviewAutoServerSharedReference` | `true` | Adds `../ServerShared*/ServerShared*.csproj` to every non-client-side project. `false` disables it. |
| `DisablePurviewServerSharedReferenceCheck` | *(unset, which is treated as false)* | Turns off `PRSHR0001` and `PRSHR0002`. |
| `PurviewServerSharedReferenceCheckAsError` | `false` | Reports `PRSHR0001`/`PRSHR0002` as errors. |
| `AutoGeneratedAssemblyInfoFile` | `GeneratedAssemblyInfo.cs` | File name of the generated AssemblyInfo helper source, written under the intermediate `Properties/` folder. |
| `AutoGeneratedAssemblyIdentifierFile` | `GeneratedAssemblyIdentifier.cs` | File name of the generated assembly identifier source, written under the intermediate `Properties/` folder. |
| `CurrentYear` | *(the current year at build time)* | Year used in generated assembly metadata. |
| `NamespaceRemoveSuffix` | *(built-in list)* | Item type listing the suffixes stripped from `RootNamespace`. Remove an entry **after** the `Sdk.props` import to keep that suffix in the namespace, e.g. `<NamespaceRemoveSuffix Remove="Abstractions" />`. **Roslyn component names are not in the list** — `SourceGenerator`, `SourceGenerators`, `SourceGeneration`, `Generators`, `Analyzers`, `CodeFixers` and `CodeFixes` are part of a component's identity, and a generator, its analyzers and its code fixes are separate assemblies that each need their own namespace. Add one with `<NamespaceRemoveSuffix Include="Analyzers" />` if a repository really wants it collapsed. See [Namespace stripping](concepts/Assembly-Name-Generation.md#namespace-stripping). |

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
| `Current` | `net10.0;net11.0` | The default. The current LTS plus the latest release. |
| `Latest` | `net11.0` | Newest release only. |
| `Supported` | `net10.0;net11.0` | A package that should work on anything still in support. |
| `Broad` | `net8.0;net9.0;net10.0` | Every shipped release except the newest major — widest reach without committing to it. |
| `All` | `net8.0;net9.0;net10.0;net11.0` | `Broad` plus the newest major. |

`net8.0` and `net9.0` are deliberately outside `Supported`: both are at or near end of support, so a new
package should not imply a commitment to them. Choose `Broad` to keep them.

`Broad` and `All` differ by one real product decision — whether the package commits to the newest
release — so they are separate names rather than one set that silently widens every consumer of it.

A set is **always** in play: `Current` applies when nothing is selected, and resolves to the current LTS
plus the latest release. The value is named and overridable in one place, so moving a repository (or every
repository) between runtimes is a property change rather than an edit in each project.

Overrides are layered, narrowest first:

1. an explicit `TargetFramework`/`TargetFrameworks` always wins, wherever it is set: the project (with
   or without a `Condition`), a file it imports, or `Directory.Build.props` before or after the import.
   Only `Directory.Build.targets` is too late, because the .NET SDK has already decided whether the
   project multi-targets;
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
| `PurviewTargetFrameworkSet` | `Current` | `Current`, `Latest`, `Supported`, `Broad` or `All`. An unrecognised name fails the build with `PurviewInvalidTargetFrameworkSet` rather than silently falling back to the default set. A single-entry set resolves to `TargetFramework`, so it does not pay for an outer multi-targeting build. |
| `PurviewTargetFrameworksCurrent` | `net10.0;net11.0` | Redefines the `Current` set. |
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

The SDK never sets package or site metadata such as `PackageProjectUrl`. A repository that wants one
shared value can define its own properties before the import and derive the metadata from them. This
repository does that in `src/Directory.Build.props` — a convention, not SDK properties; the SDK neither
defines nor reads them:

```xml
<PropertyGroup>
  <PurviewHomepage>https://purview.dev/</PurviewHomepage>
  <PurviewProjectUrl>$(PurviewHomepage)projects/build-sdk/</PurviewProjectUrl>
  <PackageProjectUrl>$(PurviewProjectUrl)</PackageProjectUrl>
</PropertyGroup>
```

## Packable project defaults

For projects where `IsPackable=true`, the SDK provides these defaults **only when the consuming
project has not supplied a value**:

| Property | Default | Description |
| -- | -- | -- |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. |
| `IncludeSymbols` | `true` | Produces a symbol package (`false` for an analyzer-only Roslyn component, `IsRoslynComponentOnly=true` — its PDB ships inside the `.nupkg` under `analyzers/dotnet/cs/` via `PurviewPackAnalyzerPdb=true`). |
| `SymbolPackageFormat` | `snupkg` | Symbol package format. Always the modern `.snupkg`; the legacy `.symbols.nupkg` is never produced by default. |
| `PublishRepositoryUrl` | `true` | Publishes the repository URL. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources for SourceLink. |
| `DebugType` | `portable` | Ensures portable PDBs for symbol-package delivery. |
| `IncludeSource` | `false` | Packs source files into the symbol package. Off for every project, packable or not; SourceLink is how sources reach the debugger. |

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
| `BootstrapMultiThreadedBuildRspToRepoRoot` | `true` | Creates a managed `Directory.Build.rsp` at the repository root that enables MSBuild's multithreaded build (`-mt` plus `-nr:false`) on .NET 11. Set to `false` to stop bootstrapping it. |
| `RepositoryDirectoryBuildRspFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `Directory.Build.rsp`. |
| `PurviewMultiThreadedBuild` | `true` | When `false`, a managed `Directory.Build.rsp` is rewritten to `-mt:false`, so opting out cannot leave the mode enabled. Left unset, a Blazor WebAssembly client writes `-mt:false` for the whole repository, because it cannot build under `-mt` (`PRBLZ0002`); a value you set always wins. |
| `PurviewBuildSdkVersionForGlobalJson` | *(restored package version, else the version it was packed as)* | Version written to the `msbuild-sdks.Purview.BuildSdk` entry in a bootstrapped `global.json`. |
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing` never touches an existing file, `Always` overwrites it, `WarnOnDrift` reports that an existing file differs from the SDK-provided one, and `Never` skips bootstrapping entirely. |
| `PurviewRepoBootstrapCopyRetries` | `3` | Copy attempts before a bootstrap write failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between bootstrap write attempts. |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a bootstrap write that still fails after every retry is reported as a warning and the build continues. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes MSB3026 copy retry notices to messages. Set to `false` to see every retry attempt. |
| `PurviewEditorConfigAutoUpdate` | `true` | Refreshes a managed `.editorconfig` (one opening with the SDK's version marker) when the SDK version changes. `false` keeps it exactly as it is. |
| `PurviewEditorConfigAdoptionWarning` | `true` | Warns, once per repository per SDK version, when the repository `.editorconfig` is not one the SDK manages and so will never be updated. |
| `PurviewEditorConfigAdoptionWarningInCI` | `false` | Reports the adoption warning on CI builds too. Off so that advice cannot fail a `TreatWarningsAsErrors` build. |
| `PurviewEditorConfigAdoptionStampPath` | *(.purview/editorconfig-adoption.cache beside the repository .editorconfig)* | Where the once-per-SDK-version record of the adoption warning is kept. |
| `PurviewRepositoryRootStrategy` | `Markers` | How the bootstrap finds the repository root: `Markers` walks up to the nearest root marker without starting a process; `Git` asks git for the worktree root. |
| `PurviewRepositoryRootMarkers` | `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json` | Ordered markers identifying a repository root. The first is definitive and searched to the top; the rest resolve nearest-first. |
| `EditorConfigFilePath` | *(the .editorconfig inside the installed SDK package)* | The SDK-provided `.editorconfig` injected into `@(EditorConfigFiles)` for build-time style enforcement. Set it before the `Sdk.props` import. |
| `RepoRoot` | *(discovered from CI workspace variables, then a .git marker or package.json above the project)* | Repository root used for version detection and bootstrapping. Set it before the `Sdk.props` import. |

Bootstrap writes are staged into a temporary file and renamed into place, so editors and tools never
observe a partially written `.editorconfig`, `global.json` or `Directory.Build.rsp`. Because every
project in a solution runs the same bootstrapping targets, a lost race between parallel projects is a
no-op: an existing file is treated as success rather than a copy failure.

The `Directory.Build.rsp` is SDK-managed: its first line carries `# Purview.BuildSdk - managed file`,
and only a file carrying that marker is refreshed. It contains `-mt` and `-nr:false`. `-mt` is a build
host switch, so a response file is the only way a project can turn it on; `-nr:false` avoids reusing a
node left by a non-`-mt` build, which can cache a `RoslynCodeTaskFactory` task with no assembly path
and fail the first `-mt` build (`dotnet/msbuild#15213`). `-mt` still uses the MSBuild server (short-lived)
for Server GC.

A repository with a Blazor WebAssembly client gets `-mt:false` instead, naming the client, because a
`net11.0` client cannot build under `-mt`. A client built under `-mt` anyway stops with `PRBLZ0002`
before the WebAssembly SDK fails. See
[Repositories with a Blazor WebAssembly client](Repository-Bootstrap.md#repositories-with-a-blazor-webassembly-client).

## .NET 11 features

Both compiler opt-ins are off by default and apply only to a `net11.0`+ target; the `net10.0` leg of a
multi-targeted project is unchanged.

| Property | Default | Notes |
| -- | -- | -- |
| `PurviewRuntimeAsync` | *(unset, which is treated as false)* | When set to `true`, adds `<Features>runtime-async=on</Features>`. `UseRuntimeAsync=true` also opts in; an explicit `false` under either name keeps it off. |
| `MemorySafetyRules` | `0` | `0` off (default); `1` sets the preview pointer-syntax tier (`LangVersion=preview` unless the project named its own); `2` enables `updated-memory-safety-rules`, which is an experimental compiler feature and therefore opt-in. `PRCFG0001` on an unknown value. |
| `AllowUnsafeBlocks` | *(compiler default: false)* | Never set by the SDK. |

Server GC, `TieredPGO`, ReadyToRun, Native AOT, `UseArtifactsOutput`, `EnablePackageValidation`,
`NuGetAuditMode=all` and `CheckForOverflowUnderflow` were evaluated and deliberately left opt-in:
each changes behaviour or cost for every project rather than improving one workload. See
[.NET 11 features](https://github.com/purview-dev/build-sdk#net-11-features) in the README.

## ServerShared

A `ServerShared*` project is referenced automatically by every project that cannot run in a browser.
It is skipped for a WebAssembly client, a `PurviewBrowserSafe` library, a Razor class library, a
`*.Client`/`*.ClientShared`/`*.Browser`/`*.Wasm`/`*.WebAssembly`/`*.Components` (or `ClientShared`/`Components`) project, and for `Shared`/`SharedTesting`
projects (which are themselves referenced widely). `IsClientSideProject` can override the classification
either way.

`PRSHR0001` reports a client-side project that references a `ServerShared` project; `PRSHR0002`
reports a `Shared` project that does. Both are warnings by default, become errors with
`PurviewServerSharedReferenceCheckAsError=true`, and are suppressed by
`DisablePurviewServerSharedReferenceCheck=true`. `PurviewBrowserSafe="true"` on the `ProjectReference`
records that the reference was judged safe.

## Sdk folder packaging

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAutoSdkPack` | `true` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. Disable this for MSBuild SDK projects. |

The SDK writes nothing into an agent directory. It used to mirror a package's `.agents/**` into the
consuming repository; that is gone, because the skills CLI covers it. See
[Agent Skills](tooling/Agent-Skills.md).

## Telemetry

| Property | Default | Description |
| -- | -- | -- |
| `ExcludePurviewTelemetry` | `false` | Set to `true` to exclude `Purview.Telemetry.SourceGenerator` from all projects. |
| `ExcludeMSTelemetryExtension` | `false` | Set to `true` to exclude `Microsoft.Extensions.Telemetry.Abstractions`. Only relevant when `ExcludePurviewTelemetry` is also `false` — when `ExcludePurviewTelemetry=true` the whole telemetry group is skipped anyway. |

## Blazor WebAssembly, browser-safe libraries and BFF hosts

| Property | Default | Description |
| -- | -- | -- |
| `IsBffProject` | *(detected: true for a web SDK project named `*Bff`)* | Classifies a BFF host. `false` opts out, `true` opts in. |
| `IsBlazorHostProject` | *(computed)* | `true` for a web SDK project referencing a WebAssembly client. `false` opts out. |
| `PurviewAutoBlazorClientReference` | `true` | A host `<Name>` references its `<Name>.Client` automatically. |
| `DisablePurviewBlazorWebAssemblyDefaults` | `false` | Keeps the generic defaults on a WebAssembly client. Set before the SDK import. |
| `PurviewBrowserSafe` | *(unset, which is treated as false)* | `true` on a class library applies `IsAotCompatible`, `SupportedPlatform browser` and the browser-safe `WarningsAsErrors`. |
| `PurviewBrowserSafeWarningsAsErrors` | *(true on a PurviewBrowserSafe library, otherwise unset)* | `false` keeps trim/AOT diagnostics as warnings. |
| `PurviewBrowserSafeWarningCodes` | *(on a PurviewBrowserSafe library, CA1416, IL2001-IL2128, IL3000-IL3005 and IL3050-IL3059)* | The ids added to `WarningsAsErrors`. |
| `DisablePurviewBrowserSafeReferenceCheck` | *(unset, which is treated as false)* | Turns off `PRBLZ0001`. |
| `PurviewBrowserSafeReferenceCheckAsError` | *(unset, which is treated as false)* | Reports `PRBLZ0001` as an error. |
| `PurviewBffExposesApi` | *(true when the BFF references Microsoft.AspNetCore.OpenApi, otherwise unset)* | Whether a BFF keeps the OpenAPI interceptor namespace. |

## Testing

| Property | Default | Description |
| -- | -- | -- |
| `TestingFramework` | `TUnit` | Testing framework. Supported values: `TUnit`, `Xunit`, `None`. |
| `SubstituteFramework` | `TUnitMocks` | Mocking provider. Supported values: `TUnitMocks`, `NSubstitute`, `None`. |
| `TestDataFramework` | `Bogus` | Test data provider. Supported values: `Bogus`, `None`. |
| `DisableAutoInternalsVisibleTo` | `false` | Set to `true` to disable automatic `InternalsVisibleTo` generation for test types and shared testing projects. |

See [Engineering Principles](concepts/Engineering-Principles.md) for the policy-level testing guidance and
[Testing Wiring](testing/Testing-Wiring.md) for the implementation details.

## Project detection

The SDK classifies every project before its body is evaluated, and every result is also compiler-visible.
Override one by setting it before the `Sdk.props` import. See [Project Type Detection](concepts/Project-Type-Detection.md).

| Property | Default | Description |
| -- | -- | -- |
| `IsCSharpProject` | *(detected from the project file extension)* | `true` for a `.csproj`; every C# default in the SDK is gated on it. |
| `IsSdkProject` | *(detected from the project)* | `true` when an `Sdk` value is found on the project or its import declaration. |
| `SdkProjectName` | *(detected from the project)* | The SDK named by the project or its import, for example `Microsoft.NET.Sdk.Web`. |
| `IsWebProject` | *(detected from the project)* | Marks a project with web behaviour (a web SDK project or an API host). |
| `IsWebSdkProject` | *(detected from the project)* | `true` when the project SDK is `Microsoft.NET.Sdk.Web`. |
| `IsWorkerSdkProject` | *(detected from the project)* | `true` when the project SDK is `Microsoft.NET.Sdk.Worker`. |
| `IsAspireHostProject` | *(detected from the project)* | `true` when the project SDK starts with `Aspire.Sdk.Host` or `Aspire.AppHost.Sdk`. |
| `IsBlazorWebAssemblyProject` | *(detected from the project)* | `true` when the project SDK is `Microsoft.NET.Sdk.BlazorWebAssembly`. |
| `IsRazorClassLibraryProject` | *(detected from the project)* | `true` when the project SDK is `Microsoft.NET.Sdk.Razor`. |
| `IsBlazorHostProject` | *(detected from the project)* | `true` for a web SDK project that references a Blazor WebAssembly client. Says nothing about whether the host is a BFF. |
| `IsBffProject` | *(detected from the project)* | `true` for a web SDK project whose name ends with `Bff`; set `true` or `false` (in the project too) to override. Gates `PDS0008`/`PDS0009`. |
| `IsCLIProject` | *(detected from the project)* | `true` when the project name ends with `CLI`, `Console`, `CommandLine`, `QuickStart` or `QuickStarts`. |
| `IsContainerProject` | *(detected from the project)* | `true` when a `Dockerfile` beside the project selects the container defaults. |
| `IsSharedProject` | *(detected from the project name)* | `true` for a `Shared` helper project (`Shared`, `SharedFramework`, ...), which other projects reference automatically. |
| `IsServerSharedProject` | *(detected from the project name)* | `true` for a `ServerShared` helper project, which only projects that cannot run in a browser reference automatically. |
| `IsSharedTestingProject` | *(detected from the project name)* | `true` for a shared testing helper project (`SharedTestingFramework`, ...). |
| `TestingType` | *(detected from the project name)* | Test category taken from a test project's name suffix (`Unit`, `Integration`, `E2E`, ...). |
| `TargetProjectName` | *(detected from the project name)* | For a test project, the project under test, inferred from the test project's name. |

## Roslyn components and analyzers

| Property | Default | Description |
| -- | -- | -- |
| `IsRoslynComponentOnly` | `false` (`true` for Roslyn components) | Produces an analyzer-only package: no `lib/` output, no symbol package, and the analyzer and its PDB packed under `analyzers/dotnet/cs/`. Set `false` for a dual-role component. Set it before the `Sdk.props` import. |
| `DisableRoslynCompilerDefaultsValidation` | `false` | Stops `ValidateRoslynComponentCompilerSettings` failing the pack of a Roslyn component whose compiler defaults were changed. |
| `PurviewPackAnalyzerPdb` | *(true for an analyzer-only Roslyn component, otherwise unset)* | Packs a Roslyn component's PDB beside its analyzer under `analyzers/dotnet/cs/`, because a `.snupkg` cannot carry analyzer symbols. |
| `PurviewSourceGeneratorFrameworkAnalyzerValidation` | *(unset, which is treated as true)* | Set to `false` to turn off `PRSGD0005`, which checks that every file in a Roslyn component's analyzer closure is self-contained. |
| `PurviewAnalyzerClosurePermittedReferences` | `Microsoft.CodeAnalysis;System.Composition;System.Collections.Immutable;System.Memory;System.Threading.Tasks.Extensions;netstandard;mscorlib;System.` | Simple-name prefixes of host-provided assemblies an analyzer-closure file may reference without `PRSGD0005`. |
| `PurviewIncludeComponentReferenceAnalyzerClosure` | `true` | Includes a referenced Roslyn component's analyzer artifact in the referencing component's analyzer closure. |
| `PurviewImportProjectReferenceBuildTransitive` | `true` | Registers the `CompilerVisibleProperty` items a referenced project ships in `Sdk/buildTransitive`, so a package's own properties reach analyzers while developing it through `ProjectReference`. |

## Agent skills

See [Agent Skills](tooling/Agent-Skills.md) for the targets these configure.

| Property | Default | Description |
| -- | -- | -- |
| `PurviewSkillsLockFile` | `<repo root>/skills-lock.json` | Overrides where the skills CLI's lockfile is read from. |
| `PurviewSkillsSourceFolder` | `<repo root>/skills` | Repository folder holding authored skills. |
| `PurviewSkillsAgents` | *(empty, so the skills CLI auto-detects agents)* | Comma-separated agents the skill targets install to, and that recommended commands name. When set, `CheckPurviewSkills`/`FixPurviewSkills` allow only those agents' skill directories. |
| `PurviewSkillsDryRun` | *(unset, which is treated as false)* | When `true`, `InstallPurviewSkills` and `UpdatePurviewSkills` print the CLI commands without running them. |
| `PurviewSkillsRunner` | `bun` | Package runner used to launch the skills CLI (`bun`, `npx`, `bunx`, `pnpm`, `yarn`, `deno`, `skills`, or any other). |
| `PurviewSkillsRunnerCandidates` | `bun;npx;bunx;pnpm;yarn;deno` | Runners probed on `PATH` after `PurviewSkillsRunner`. |
| `PurviewSkillsPackage` | `skills@1` | Package spec the executed launcher pins to. |
| `PurviewSkillsInstallRoot` | *(resolved)* | Overrides the repository root every skill target runs from. |
| `PurviewRepositoryRootMarkers` | `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json` | Ordered markers identifying a repository root. The first is definitive and searched to the top; the rest resolve nearest-first. |
| `PurviewSkillsCommand` | *(detected)* | Overrides the launcher entirely and skips runner detection. |
| `PurviewSkillsSource` | *(none)* | The source `InstallPurviewSkills` adds (`owner/repo`, optionally `#ref`); without it, install restores `skills-lock.json`. |
| `PurviewSkillsRepoRoot` | *(RepoRoot, else the nearest folder with an AGENTS.md)* | Evaluation-time guess at the repository root for the skill targets; a value you set outranks the marker walk. Set it before the `Sdk.props` import. |

## Property schema validation

For a package that ships its own `.props`/`.targets` and an MSBuild Editor sidecar. See
[MSBuild property schema and IntelliSense](configuration/Property-Schema.md).

| Property | Default | Description |
| -- | -- | -- |
| `EnablePurviewPropertySchemaValidation` | `false` | Runs ValidatePurviewPropertySchema before Pack, failing it when a .buildschema.json sidecar and the package's .props/.targets disagree (PRPSC0001-PRPSC0004). For a package that ships MSBuild properties. Off, so no ordinary build or pack changes. |
| `PurviewPropertySchemaDirectory` | *(the project's Sdk/ folder, which PurviewAutoSdkPack ships)* | Folder whose .props/.targets files and .buildschema.json sidecars ValidatePurviewPropertySchema checks. |
| `PurviewPropertySchemaIgnoredProperties` | *(empty)* | Compiler-visible properties ValidatePurviewPropertySchema does not require a sidecar entry for, because the package does not own them (Version, RootNamespace, ...). |

## Compiler-visible SDK properties

The SDK exports its properties via `CompilerVisibleProperty`, so analyzers and source generators can
read them through `build_property.<PropertyName>`:

```csharp
// In a DiagnosticAnalyzer or ISourceGenerator.
context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.IsBffProject", out var isBff);
```

Read the exported property rather than re-deriving SDK behaviour: the export is the SDK's own answer
and stays correct when the rules change. **To expose a new property**, add a `CompilerVisibleProperty`
item (with a `<Description>`) alongside the others in `Sdk.props` — an analyzer cannot see an MSBuild
property that is not in this list.

| Property | Description |
| -- | -- |
| `UsePackageJsonVersion` | Whether version detection from `package.json` is active. |
| `RootPackageJson` | Resolved path to the `package.json` used for version detection. |
| `RepoRoot` | Repo root directory found via `.git` auto-discovery. |
| `Version` | Package/assembly version, sourced from `package.json` when detection is enabled. |
| `PackageVersion` | NuGet package version, sourced from `package.json` when detection is enabled. |
| `ProjectDir` | Absolute project directory path (with trailing separator) used by analyzers for project-relative file calculations. |
| `RootNamespace` | Effective project root namespace used by analyzers that validate or customize namespace conventions. |
| `NamespacePrefix` | Required namespace prefix used to derive `RootNamespace`. |
| `DisableNamespacePrefixCheck` | Disables the build error for missing `NamespacePrefix`. |
| `TestingFramework` | Selected testing framework (`TUnit`, `Xunit`, or `None`). |
| `SubstituteFramework` | Selected mocking provider (`TUnitMocks`, `NSubstitute`, or `None`). |
| `TestDataFramework` | Selected test data provider (`Bogus` or `None`). |
| `SourceLinkPackageName` | SourceLink package ID added by the SDK. |
| `DisableSourceLink` | Disables automatic SourceLink integration. |
| `ExcludePurviewTelemetry` | Opt-out for `Purview.Telemetry.SourceGenerator`. |
| `ExcludeMSTelemetryExtension` | Opt-out for `Microsoft.Extensions.Telemetry.Abstractions`. |
| `PurviewSkillsSourceFolder` | Repository folder holding authored agent skills (default `<repo root>/skills`). |
| `PurviewSkillsAgents` | Comma-separated agents the skill targets install to (default empty: the CLI auto-detects). |
| `PurviewSkillsDryRun` | When `true`, `InstallPurviewSkills` and `UpdatePurviewSkills` print commands without running them. |
| `PurviewSkillsCommand` | Overrides the launcher entirely and skips runner detection. |
| `PurviewSkillsSource` | The source `InstallPurviewSkills` adds (`owner/repo`, optionally `#ref`); without it, install restores `skills-lock.json`. |
| `PurviewSkillsLockFile` | Where the skills CLI's `skills-lock.json` is read from (default `<repo root>/skills-lock.json`). |
| `PurviewSkillsInstallRoot` | Overrides the repository root every skill target runs from. |
| `PurviewRepositoryRootMarkers` | Ordered markers identifying a repository root (default `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json`). |
| `PurviewSkillsRunner` | Package runner used to launch the skills CLI (default `bun`; also `npx`, `bunx`, `pnpm`, `yarn`, `deno`, `skills`). |
| `PurviewSkillsRunnerCandidates` | Runners probed on `PATH` after `PurviewSkillsRunner` (default `bun;npx;bunx;pnpm;yarn;deno`). |
| `PurviewSkillsPackage` | Package spec the executed launcher pins to (default `skills@1`). |
| `PurviewRepoBootstrapMode` | Controls repo file bootstrapping: `IfMissing` (default), `Always`, `WarnOnDrift` or `Never`. |
| `PurviewRepoBootstrapCopyRetries` | Number of write attempts before a bootstrap failure is reported (default `3`). |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | Base delay between bootstrap write attempts (default `500`). |
| `PurviewRepoBootstrapCopyFailureAsError` | When `true` (default), a bootstrap write that still fails after every retry is an error. |
| `PurviewSuppressCopyRetryWarnings` | When `true` (default), built-in copy retry notices (`MSB3026`) are demoted to messages. |
| `PurviewAutoSdkPack` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. |
| `DisableGenerateAssemblyInfoClass` | Disables generated `AssemblyInfo` helper source. |
| `DisableGenerateAssemblyIdentifierClass` | Disables the generated assembly identifier marker class. |
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
| `IsAspireHostProject` | True when SDK starts with `Aspire.Sdk.Host` or `Aspire.AppHost.Sdk`. |
| `IsBlazorWebAssemblyProject` | True when `SdkProjectName` is `Microsoft.NET.Sdk.BlazorWebAssembly`. |
| `IsRazorClassLibraryProject` | True when `SdkProjectName` is `Microsoft.NET.Sdk.Razor`. |
| `IsBlazorHostProject` | True for a web SDK project that references a Blazor WebAssembly client. |
| `IsBffProject` | True for a web SDK project named `*Bff`, or that sets `IsBffProject=true`. |
| `PurviewBrowserSafe` | When `true` on a class library, applies the browser-safe defaults. |
| `PurviewBrowserSafeWarningsAsErrors` | When `false`, browser-safe trim and AOT diagnostics fall back to the project's own warning policy (`TreatWarningsAsErrors`, `true` by default). |
| `DisablePurviewBrowserSafeReferenceCheck` | Turns off `PRBLZ0001`. |
| `PurviewBrowserSafeReferenceCheckAsError` | Reports `PRBLZ0001` as an error. |
| `DisablePurviewBlazorWebAssemblyDefaults` | Keeps the generic defaults on a WebAssembly client. |
| `PurviewAutoBlazorClientReference` | When `false`, a host does not reference its `<Name>.Client` automatically. |
| `PurviewBffExposesApi` | Whether a BFF keeps the OpenAPI interceptor namespace. |
| `PurviewAutoServerSharedReference` | When false, a server-side project does not automatically reference a sibling ServerShared project. Client-side projects never get one. Defaults to true. |
| `DisablePurviewServerSharedReferenceCheck` | When true, a client-side or Shared project does not report a ServerShared project reference (PRSHR0001/PRSHR0002). |
| `PurviewServerSharedReferenceCheckAsError` | When true, PRSHR0001/PRSHR0002 are errors instead of warnings. Defaults to false. |
| `MemorySafetyRules` | The C# 15 memory-safety tier: 0 off (default), 1 preview syntax, 2 updated rules. The updated rules are an experimental compiler feature, so they are opt-in. AllowUnsafeBlocks is left unset. |
| `PurviewRuntimeAsync` | When true, a net11.0+ project compiles with the runtime-async compiler feature. Defaults to false; UseRuntimeAsync=true also opts in. |
| `PurviewMultiThreadedBuild` | When true (default), boots a Directory.Build.rsp that enables MSBuild's multithreaded build (-mt). Left unset, a Blazor WebAssembly client writes -mt:false instead. |
| `EditorConfigFilePath` | Path to the SDK-provided `.editorconfig` that is injected into `@(EditorConfigFiles)`. |
| `RepositoryEditorConfigFilePath` | Destination path for bootstrapping a physical repo-level `.editorconfig` (defaults to git repo root; falls back to `Directory.Build.props` directory). |
| `BootstrapEditorConfigToRepoRoot` | When `true` (default), copies the SDK `.editorconfig` to `RepositoryEditorConfigFilePath` if missing. |
| `RepositoryGlobalJsonFilePath` | Destination path for bootstrapping a physical repo-level `global.json` (defaults to git repo root; falls back to `Directory.Build.props` directory). |
| `BootstrapGlobalJsonToRepoRoot` | When `true` (default), creates `global.json` at `RepositoryGlobalJsonFilePath` if missing. |
| `RepositoryDirectoryBuildRspFilePath` | Destination path for bootstrapping a repository-level Directory.Build.rsp when missing. |
| `BootstrapMultiThreadedBuildRspToRepoRoot` | When true, bootstraps a managed Directory.Build.rsp that enables MSBuild's multithreaded build (-mt). |
| `DisableAutoCopySdkFiles` | When `true`, disables SDK auto-copy/bootstrap for repo files (`.editorconfig`, `global.json`, `Directory.Build.rsp`). |
| `PurviewBuildSdkVersionForGlobalJson` | Version used for `msbuild-sdks.Purview.BuildSdk` when bootstrapping `global.json` (auto-detected from SDK package path, falling back to the version stamped in at pack time). |
| `CurrentYear` | Current year used in generated assembly metadata. |
| `AutoGeneratedAssemblyInfoFile` | File name of the generated AssemblyInfo source file (written under the intermediate `Properties/` folder). |
| `AutoGeneratedAssemblyIdentifierFile` | File name of the generated assembly identifier source file (written under the intermediate `Properties/` folder). |
| `AssemblyIdentifierClassName` | Name of the generated assembly identifier marker class. |
| `IsCLIProject` | True when the project name ends with `CLI`, `Console`, `CommandLine`, `QuickStart` or `QuickStarts`. |
| `IsSharedProject` | True when the project is a shared project. |
| `IsServerSharedProject` | True for ServerShared helper projects (ServerSharedFramework, etc.), which are referenced automatically only by projects that cannot run in a browser. |
| `IsClientSideProject` | True when the project's assemblies are downloaded to a browser: a WebAssembly client, a PurviewBrowserSafe library, a Razor class library, or a project named with a client-side suffix (*.Client, *.ClientShared, *.Browser, *.Wasm, *.WebAssembly, *.Components, or exactly ClientShared or Components). Set explicitly to override. |
| `LangVersion` | C# language version used to build the Roslyn component. Defaults to latest. |
| `Nullable` | Nullable reference-types context used to build the Roslyn component. Defaults to enable. |
| `TreatWarningsAsErrors` | Treats all compiler, analyzer and NuGet warnings as errors. Defaults to true for every project; set false to opt out. |
| `EnforceExtendedAnalyzerRules` | Enforces Roslyn analyzer best-practice rules. Defaults to true for Roslyn components. |
| `Deterministic` | Produce deterministic builds. Defaults to true. |
| `ContinuousIntegrationBuild` | True when building in a recognized CI environment, producing deterministic SourceLink output. |
| `EmbedUntrackedSources` | Embeds untracked source files for SourceLink. Defaults to true for Roslyn components. |
| `PurviewSourceGeneratorFrameworkAnalyzerValidation` | False to opt out of PRSGD0005 self-contained analyzer-closure validation. |

`LangVersion`, `Nullable`, `TreatWarningsAsErrors`, `EnforceExtendedAnalyzerRules`, `Deterministic`,
`ContinuousIntegrationBuild` and `EmbedUntrackedSources` are exported for Roslyn components only, so downstream
tooling can confirm the shipped analyzer packages were built with the standard settings.

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
