# Changelog

All notable changes to `Purview.BuildSdk` and `Purview.Skills.Testing`.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions come from the
`version` field in `package.json`, which is the single source of truth: a merge to `main` publishes
that version to nuget.org.

This file was reconstructed from the conventional-commit history at 1.0.7, so entries before that
release summarise what the commits recorded rather than what was written at the time.

## [Unreleased]

### Added

- **csproj IntelliSense for every SDK property.** The package now ships
  `Sdk/Sdk.props.buildschema.json`, an [MSBuild Editor](https://marketplace.visualstudio.com/items?itemName=mhutch.msbuildeditor)
  sidecar describing each of the SDK's own properties: description, type (enumerations included) and
  default, and whether it must be set before the `Sdk.props` import. Install the extension to get
  completion in any project that imports the SDK; without it the file is inert. Properties MSBuild and
  the .NET SDK already describe are not redescribed.
- **The schema is held to the code.** `PropertySchemaTests` fails when a property is defined with no
  schema entry (or the reverse), a `CompilerVisibleProperty` or analyzer `build_property.*` read is
  undescribed or unexported, a schema default differs from the evaluated default, a pre-import claim
  does not match what `Sdk.props` reads, a README or wiki table omits a property or states another
  default, the schema fails the MSBuild Editor meta-schema (vendored, validated offline), or the
  packed `.nupkg` lacks the sidecar. `just properties-check` runs them. It replaces the hand-maintained
  list in `CompilerVisiblePropertyTests`.
- **`ValidatePurviewPropertySchema`**, an opt-in target for packages that ship their own
  `.props`/`.targets` and sidecars: `EnablePurviewPropertySchemaValidation=true` runs it before `Pack`
  and fails on `PRPSC0001` (an undescribed `CompilerVisibleProperty`), `PRPSC0002` (a described property
  nothing defines), `PRPSC0003` (an unreadable sidecar) and `PRPSC0004` (a sidecar MSBuild Editor will
  never load because it is not named `<file>.buildschema.json`). `PurviewPropertySchemaDirectory` and
  `PurviewPropertySchemaIgnoredProperties` configure it. Off by default: no consumer build changes.
- The `purview-buildsdk-msbuild-properties` skill: adding, renaming, deprecating and removing a property,
  when it must be set before the import, when to make it compiler-visible, and where a sidecar must sit
  for `Sdk/`, `build/` and `buildTransitive/` deliveries — and declaring a new sidecar in
  `.config/purview-build.json` `PackValidation.RequiredContent`, which Purview.Build's pack validation
  otherwise rejects as undeclared content.
- **The `purview-buildsdk-justfile` skill: a shared, modular `Justfile`.** The root `Justfile` imports
  one barrel, `.build/just/index.just`, over modules the skill ships in `assets/just/` and a repository
  copies verbatim: `dotnet.just` (`ensure-tools`, `restore`, `build`, `clean`, `test`, `pack`),
  `version.just` (`version`, and `just bump major|minor|patch`, which resets the parts after the one
  bumped), `utilities.just` (`lint-check`, `lint-fix`, `vs`, `scrub`), `purview-build.just`
  (`pipeline-pr`, `pipeline-build`, `pipeline-tests`), `skills.just` (every `skills-*` target), and
  `env.just`, which exports telemetry opt-outs and `DOTNET_CLI_ENABLEAOT` to every recipe without overriding
  a value already set. It deliberately does not force the MSBuild server on: a shared server keeps the
  NuGet settings it first read, which breaks a build under another `nuget.config` (`MSB4242`). Two modules are
  optional, switched on by copying the file: `purview-build-nuget.just` (`pipeline-pack-validate`,
  `pipeline-local-release`, `pipeline-release`) and `docs.just` (`docs`, `docs-install`, `docs-check` for
  a pm-docs-site project site). A module's variables are defaults the root `Justfile` overrides.
- **The `purview-buildsdk-docs-sync` skill: keep skills, agent instructions, READMEs and the wiki true.**
  It treats the code as the only source of truth — every claim is checked against the `.props`/
  `.targets`, the `Justfile`, the analyzer descriptors and the tests, never against another document —
  and runs a change as a sweep across all four surfaces. It works out which README a package actually
  ships (a project's own `README.md`, else the repository's; `Sdk/*.md` packs to the package root), and
  keeps `docs/wiki/` hierarchical: root pages only where the documentation site depends on their URL,
  everything else in one topic folder, `_Sidebar.md` and `Home.md` in step. Its references cover how to
  check each kind of claim and a wiki-structure test to add.
### Fixed

- **A relative `RootPackageJson` resolves from the project directory.** It resolved against the build's
  working directory, so one build read a different `package.json` - or none - depending on where
  `dotnet` was started. An absolute path is unchanged; in `Directory.Build.props` prefer
  `$(MSBuildThisFileDirectory)package.json`, because a relative value resolves per project.
- **A file linked beneath `Sdk/` packs where a physical file at that path would.** `IncludeLinkedSdkFiles`
  stripped `Sdk/` from every link, so a linked `Sdk/Props/X.props` packed as `Props/X.props`. It now
  applies the `PurviewAutoSdkPack` table: `build/`, `buildTransitive/`, `buildMultiTargeting/`, `.github/`
  and top-level documents and images to the root, everything else under `Sdk/`. `SdkFolderPackingTests`
  packs both kinds side by side.
- **The property schema no longer says to set `SourceLinkPackageName`, `DisableSourceLink`,
  `AutoIncludeUsings`, `ExcludePurviewTelemetry`, `ExcludeMSTelemetryExtension`, `PurviewAutoSdkPack`,
  `IsSharedProject` or `TargetProjectName` before the import.** `Sdk.props` reads each only on items, which
  MSBuild evaluates after the project body, so a project file works. `PropertySchemaTests` now counts only
  property-pass reads as pre-import.
- **An explicit `TargetFramework` or `TargetFrameworks` always wins over the curated target framework
  sets.** The SDK only recognised an unconditional declaration in the project body. A value set in
  `Directory.Build.props` (before or after the import), under a `Condition`, or in a file the project
  imports left the set's `TargetFrameworks` list in place underneath it: NuGet restored the set's
  frameworks while the build compiled the explicit one, and the build failed with `NETSDK1005`. The
  inverse - a single-entry set such as `Latest` plus an explicit `TargetFrameworks` - silently built one
  framework. A new `Targets/TargetFrameworkOwnership.targets`, imported by the .NET SDK through
  `BeforeMicrosoftNETSdkTargets`, now withdraws the SDK's value once the project has set its own. Inner
  builds are untouched, so the TFM list stays identical across Visual Studio's evaluations. A value set
  in `Directory.Build.targets` is still too late to take part. Projects that did not set a TFM, or set
  one unconditionally in the project body, are unaffected.
- `PurviewConditionedProjectDeclaration` no longer fires for a conditioned `TargetFramework` or
  `TargetFrameworks`, because that declaration is now honoured. It still fires for `IsRoslynComponent`,
  `IsRoslynComponentOnly`, `IsPackable`, `PurviewTargetFrameworkSet` and `RootNamespace`.

### Changed

- **The skill targets are a thin wrapper over the standard skills CLI, with nothing bespoke.**
  `InstallPurviewSkills` runs `skills add "<source>" --skill "*"` for `PurviewSkillsSource` (append
  `#<ref>` to pin), or `skills experimental_install` to restore `skills-lock.json` without one.
  `UpdatePurviewSkills` re-adds every lockfile source at its latest. `CheckPurviewSkills` and
  `FixPurviewSkills` are unchanged. All run from the repository root, once per solution.
- **The C# 15 updated memory-safety rules are off by default.** A `net11.0`+ target no longer compiles
  with `<Features>updated-memory-safety-rules</Features>` unless asked. The updated model is an
  experimental compiler feature, so it is an opt-in: set `MemorySafetyRules=2` to enable it. Tier `1`
  (preview pointer syntax, which also selects `LangVersion=preview`) and tier `0` (off) are unchanged,
  and an unrecognised tier still fails with `PRCFG0001`. `AllowUnsafeBlocks` remains at its compiler
  default (`false`). Both .NET 11 compiler opt-ins are now opt-in, alongside Runtime Async.
- **`purview-buildsdk-skills-justfile` is folded into `purview-buildsdk-justfile`.** Its recipes are now
  the `skills.just` module and its guidance `references/skills-recipes.md`, so the two cannot disagree.
  The recipes name the solution unconditionally. A repository that installed the old skill keeps it until
  it is removed: run `npx skills add purview-dev/build-sdk` for the new one and
  `npx skills remove purview-buildsdk-skills-justfile` for the old. In an existing `Justfile`,
  `ensure-pipeline-tool` and `restore-tools` become `ensure-tools`, the `current_version` recipe becomes
  `version`, and `Justfile.skills.just` is replaced by the barrel.
- This repository's own `Justfile` uses the modules, keeping only `version-sync`, `properties-check`,
  `test-linux` and `skills-validate`. `just bump` runs `version-sync` afterwards.

### Removed

- **The package-to-skill mapping**, which read a `.agents/skills/` folder inside each restored package
  plus its nuspec commit. No package built by this SDK carried one, so it never found anything. With it
  went `ListPurviewSkills`, `RecommendPurviewSkills` and `VerifyPurviewSkills`, the
  `ResolvePurviewSkillSources` and `EvaluatePurviewSkillState` tasks, `PurviewSkillsContinueOnError`,
  and `PRSKL0001`, `PRSKL0002`, `PRSKL0010`-`PRSKL0014` and `PRSKL0017`. Install a repository's skills
  with `-t:InstallPurviewSkills -p:PurviewSkillsSource=owner/repo#<ref>`, or `bun x skills add` directly.
  The `skills-list`, `skills-recommend` and `skills-verify` `just` recipes are gone with them.

### Documentation

- **The wiki is organised into topic folders.** `docs/wiki/` now has `concepts/`, `configuration/`,
  `testing/`, `analyzers/`, `packaging/`, `tooling/` and `development/`, with
  `Getting-Started.md`, `Configuration-Reference.md` and `Repository-Bootstrap.md` kept at the root
  because purview.dev links to them. `Home.md` is a sectioned index, `_Sidebar.md` carries the same
  sections, and `Performance.md`, which was missing from the sidebar, is listed. A new
  `tooling/Justfile.md` describes the shared `just` modules. `DocumentationWikiTests` holds the
  structure, the indexes and every link to it, and `PropertySchemaTests` now reads property tables
  from every folder.
- **The README, wiki, skills and agent instructions were audited against the code**, and these were
  wrong:
  - **Pre-import claims.** `SourceLinkPackageName`, `DisableSourceLink`, `AutoIncludeUsings` and
    `PurviewSharedTestingOutputType` were documented as "too late in a `.csproj`". All four work
    there, because the SDK reads them only on items, or in `Sdk.targets`.
  - **`RootPackageJson`.** It was said to resolve a relative path from the project directory. It
    resolves from the build's working directory, so the docs and the schema now recommend an absolute
    path.
  - **Missing `package.json`.** A git repository with no root `package.json` fails the build. That
    is now stated, and `Getting-Started.md` gains a `package.json` step.
  - **Detection lists.** `IsAspireHostProject` also matches `Aspire.AppHost.Sdk`. `IsCLIProject` is
    a name rule. `*.ClientShared`/`ClientShared` are client-side names. Test detection accepts any
    `.<Word>Tests` suffix, and the 53 `TestType` names only drive `InternalsVisibleTo`.
  - **Repository root discovery.** The bootstrap's default walk (`Markers`) starts no git process, and
    `git rev-parse` runs only with `PurviewRepositoryRootStrategy=Git`.
  - **Templates and examples.** `templates/Directory.Packages.props` pins versions; it does not float
    them. The assembly identifier example was wrong. The quick-reference commands produced a nested
    folder.
  - **Analyzers and code fixes.** The `PDS0004` acronym lists were incomplete. The `PDS0002` code fix
    and the Move/Rename extensions refactoring are separate things.
  - **Skills.** Packages built with this SDK carry no `.agents/skills/` folder, so package-driven skill
    resolution finds nothing for them. The docs no longer claim otherwise.
  - **Pipeline.** `pipeline-tests` packs and validates like the other pipeline recipes.
  - **Published skills.** They no longer carry guidance about working in this repository.
- **Configuration tables corrected against the code.** `IncludeSource` was documented as `true` for
  packable projects; it is `false` for every project, and the documentation now says so (the default is
  unchanged). Properties that are unset by design (`PurviewRuntimeAsync`, `PurviewBrowserSafe`,
  `DisablePurviewServerSharedReferenceCheck`, `PurviewSkillsDryRun`, ...) no longer claim a `false`
  default. About fifty properties that were undocumented, or documented only in prose, now have rows,
  including new *Target framework sets*, *Project detection*, *Blazor WebAssembly and browser-safe
  libraries* and *Roslyn components and analyzers* tables in the README.
- The README and wiki *Compiler-visible SDK properties* tables now list every exported property; 22
  were missing from the README and 41 from the wiki.
- `PurviewHomepage`, `PurviewProjectUrl` and `PurviewDocsUrl` were documented as SDK properties. They are
  this repository's own convention in `src/Directory.Build.props`; the SDK neither defines nor reads
  them, and the docs now say that.
- Removed the README's `Sdk/.agents/**` packing row and the generated-`.gitignore` paragraph, which
  described the agent-folder mirror removed earlier.
- Made required settings and versions explicit in the README quick start, the wiki, and the
  `purview-buildsdk-adoption` and `purview-buildsdk-configuration` skills: `NamespacePrefix` is
  required and pre-import, the SDK version lives in `global.json`, the product version comes from
  `package.json`, and package versions live in `Directory.Packages.props`.
- Documented how to set the target framework(s) — an explicit `TargetFramework`/`TargetFrameworks`, a
  curated `PurviewTargetFrameworkSet`, or a redefined set — including the unconditional-declaration
  rule.
- Documented the compiler-visible properties (`CompilerVisibleProperty` → `build_property.*`) for
  analyzers and source generators.
- **Documented SourceLink end to end.** A new [SourceLink](docs/wiki/packaging/SourceLink.md) wiki page, a README
  section, and skill coverage now explain how to point `SourceLinkPackageName` at the provider for the
  repository host — including `Microsoft.SourceLink.AzureRepos.Git` for Azure DevOps Services and
  `Microsoft.SourceLink.AzureDevOpsServer.Git` plus a `SourceLinkAzureDevOpsServerGitHost` item for
  on-premises Azure DevOps Server / TFS, `Microsoft.SourceLink.GitLab` and
  `Microsoft.SourceLink.Bitbucket.Git` — that the provider needs a `PackageVersion` under Central
  Package Management, and how `ContinuousIntegrationBuild` follows the CI environment (`TF_BUILD` on
  Azure Pipelines). The previous documentation named `Microsoft.SourceLink.AzureDevOps.Git`, which does
  not exist.

### Fixed

- **`DisableSourceLink=true` now actually opts out of SourceLink.** `Sdk.props` re-assigned
  `DisableSourceLink` to `false` for every non-Roslyn project, clobbering a repository's pre-import
  value, so the documented opt-out silently had no effect and the GitHub provider was always added. The
  redundant assignment is removed; `Props/Defaults.props` already defaults it. `SourceLinkTests` pins the
  default provider, provider selection for another host, the test/shared-testing exclusions and the
  opt-out.

## [2.0.1] - 2026-11-12

### Changed

- **Runtime Async is off by default.** A `net11.0`+ target no longer compiles with
  `<Features>runtime-async=on</Features>` unless asked: it changes how every `await` compiles, so it
  is an opt-in rather than a baseline. Set `PurviewRuntimeAsync=true` — or `UseRuntimeAsync=true`,
  the name the .NET team uses — to enable it; an explicit `false` under either name keeps it off.
  This reverts the 2.0.0 default, so a repository that relied on it should opt in.

## [2.0.0] - 2026-11-11

### Added

- **`ServerShared` projects.** A `ServerShared*` project is referenced automatically by every
  non-client-side project, and never by a WebAssembly client, `PurviewBrowserSafe` library, Razor
  class library or `*.Client`/`*.Browser`/`*.Wasm`/`*.WebAssembly`/`*.Components` project.
  `IsClientSideProject` names the classification and can be set explicitly; `PRSHR0001` (client-side
  reference) and `PRSHR0002` (Shared reference) catch a reference that would still reach a browser.
  Opt the automatic reference out with `PurviewAutoServerSharedReference=false`.
- **.NET 11 compiler opt-ins.** A `net11.0`+ target compiles with Runtime Async
  (`<Features>runtime-async=on</Features>`) and the C# 15 updated memory-safety rules
  (`MemorySafetyRules=2`), gated per-TFM so the `net10.0` leg is unchanged. `AllowUnsafeBlocks` is
  left at the compiler default (`false`). Opt out with `PurviewRuntimeAsync=false`,
  `UseRuntimeAsync=false`, or `MemorySafetyRules=0`; an unrecognised tier fails with `PRCFG0001`.
- **Multithreaded builds.** A managed `Directory.Build.rsp` at the repository root enables MSBuild's
  `-mt` on .NET 11, bootstrapped like `.editorconfig` and `global.json` and refreshed only when its
  marker line is present. `PurviewMultiThreadedBuild=false` rewrites it to `-mt:false`.

### Changed

- **The default target frameworks are now `net10.0;net11.0`.** `Current` — the set that applies when a
  project selects none — resolves to the current LTS plus the latest release, so a project that declares
  neither `TargetFramework` nor `TargetFrameworks` now multi-targets. This is a breaking change: pin
  `PurviewTargetFrameworksCurrent`, select another set, or declare the TFM explicitly to keep the previous
  single-target output. `Supported` still names the same pair so a repository can pin the meaning rather
  than depend on where `Current` moves next.
- **`LangVersion` defaults to `latest` (C# 15)** instead of `preview`. C# 15 is the latest shipped
  language version and includes union declarations; `preview` remains available per project when a
  repository deliberately wants unshipped features. Roslyn components keep `LangVersion=latest`.

## [1.3.2] - 2026-11-09

### Changed

- **`UpdatePurviewSkills` is re-add only.** `skills add` reinstalls every skill it selects, so one
  re-add both refreshes the installed skills and adds any a source has gained; the trailing
  `skills update -y -p` added nothing and is no longer run.
- **`UpdatePurviewSkills` picks up new skills from every source.** A source no package ships used to be
  re-added naming only the skills `skills-lock.json` recorded, so a skill added to that repository
  since the last install was never installed. It is now re-added with `--skill "*"`. Package sources
  are unchanged: pinned to the package's commit, naming every skill that version ships.
- **The `purview-buildsdk-justfile` skill is now `purview-buildsdk-skills-justfile`.** A repository
  that installed the old name keeps it until it is removed: run `npx skills add purview-dev/build-sdk`
  for the new one and `npx skills remove purview-buildsdk-justfile` for the old.
- The skills-justfile skill now picks `skills_project` itself: an existing `Justfile` solution variable
  first, then a solution path an existing recipe uses, then the repository's only solution file.

### Removed

- `PurviewSkillsUpdateMode`, and `PRSKL0006` with it: there is no `skills update` step left to
  schedule. A value still set is ignored.

## [1.3.1] - 2026-11-09

### Added

- **`UpdatePurviewSkills`** re-adds every unique source in `skills-lock.json`, naming only the skills
  recorded for it, then runs `skills update -y -p`. A source a restored package ships is re-added at
  that package's commit, so package skills stay matched to the version referenced; every other source
  is re-added at latest. `PurviewSkillsUpdateMode` (`Auto` default, `Always`, `Never`) decides when
  `skills update` runs, and `PurviewSkillsDryRun=true` prints every command without running it (also
  honoured by `InstallPurviewSkills`). New diagnostics `PRSKL0006`–`PRSKL0008`.
- **`CheckPurviewSkills` and `FixPurviewSkills`** enforce `PurviewSkillsAgents` when it is set: only
  those agents' skill directories (plus the canonical `.agents/skills`) may exist at the repository
  root (`PRSKL0015`). Fix removes the skills CLI's links — never their targets — and folders left
  empty, and keeps and reports real files. The agent-to-directory map is the extensible
  `PurviewSkillsAgentDirectory` item group; an unknown agent is `PRSKL0016`.
- **Install, update and fix are explicit-only, enforced.** `InstallPurviewSkills`,
  `UpdatePurviewSkills` and `FixPurviewSkills` fail with `PRSKL0009` when pulled into a build, a
  restore or a design-time build — including by a consumer's own `AfterTargets="Build"` hook. Run
  them by name.
- **Skill targets run once per solution.** Pointed at a solution, every skill target used to run in
  every project — re-adding each source and re-running the CLI once per project, racing on
  `skills-lock.json`. Each project now hands the work to one elected leader (the first solution
  project, by path, that imports the SDK), and MSBuild's result cache runs it once. Resolution reads
  every solution project's restore graph, so a package only a test project references is found
  without choosing a project for it. Point recipes at the solution; no repository changes are needed
  otherwise. Two projects on different versions of one skill-shipping package are `PRSKL0017`.
  Package resolution also works in a multi-targeted project now: its outer build used to fail the
  per-framework condition and resolve nothing.
- New skill **`purview-buildsdk-justfile`**: adding or updating `just` recipes that run the skill
  targets, and replacing hand-written skill scripts with them.
- `just skills-update`, `just skills-fix`; `just skills-check` now runs `CheckPurviewSkills`, and the
  old recommendation report moved to `just skills-recommend`.

### Changed (breaking)

- **`PurviewSkillsRunner` defaults to `bun`** (`bun x skills@1`) instead of `npx`, and
  `PurviewSkillsRunnerCandidates` to `bun;npx;bunx;pnpm;yarn;deno`. Recommendation text now reads
  `bun x skills …`. Set `PurviewSkillsRunner=npx` to keep the previous behaviour.
- **`PurviewSkillsAgents` defaults to empty instead of `*`**: no `--agent` is passed and the skills CLI
  auto-detects agents. Set it to choose.

### Fixed

- Several agents in `PurviewSkillsAgents` were passed as one `--agent "a,b"`, which the skills CLI
  reads as a single agent named `a,b`. Each agent is now its own `--agent` flag.

## [1.3.0] - 2026-11-08

### Changed (breaking)

- **The skill recommendation hook is gone. No skill work happens during a build.** `RecommendPurviewSkillsOnBuild`
  (`AfterTargets="Build"`) and its `_PurviewSkillRecommendationBuildSetup` helper are removed. On a
  39-project solution the hook read and regex-scanned every project's `project.assets.json`
  (≈500 KB, 256 libraries each), made thousands of failing `Directory.Exists` calls, read the lockfile
  and walked the repository root — on every build, including a no-change rebuild — and every one of its
  inputs is repository-scoped, so the per-project cost was pure duplication of one repository-wide
  answer. Removed rather than defaulted off, because a repository that manages its skills deliberately
  (any repository with a `skills-lock.json`) was paying twice for an answer it had already acted on.
  Use `-t:RecommendPurviewSkills` to report and `-t:InstallPurviewSkills` to install.

- **Removed properties:** `EnablePurviewSkillRecommendations`, `PurviewSkillRecommendationsInCI` and
  `PurviewSkillRecommendationStampPath`. All three existed to gate and deduplicate the hook, including
  the `<repo root>/.purview/skill-recommendations.cache` stamp. A target a person invokes should answer
  every time it is asked, so no stamp is needed. They are also no longer compiler-visible.

### Added

- **`VerifyPurviewSkills`** — the skill state as a gate rather than advice. It reports the same
  classifications as `RecommendPurviewSkills` but escalates `PRSKL0010` (missing) and `PRSKL0011`
  (drifted) to **errors**, which is what a CI job should call. Those two are always factual and always
  fixed by one `npx skills add` command. `PRSKL0012` is reported as a warning rather than an error,
  because it also covers a package published without repository metadata — something only that
  package's author can fix — and `PRSKL0013`/`PRSKL0014` stay messages, because 0013 is an owner-scoped
  heuristic and 0014 fires on any clone of a repository that installs skills on demand. Safe because the
  target is only reachable by name: a build that did not ask is never broken by skill advice.

```bash
dotnet msbuild <project> -t:VerifyPurviewSkills
```

### Fixed

- **Incremental builds were broken outright.** `GenerateAssemblyInfoClassTarget` and
  `GenerateAssemblyIdentifierClassTarget` wrote their generated files with `Overwrite="true"` and no
  `WriteOnlyWhenDifferent`, giving them a fresh timestamp on every build. Both files are compile inputs,
  so `CoreCompile` — whose `Inputs` are `@(Compile)` — could never be up to date, and a no-change rebuild
  recompiled every project. Measured on a 39-project solution: a warm rebuild ran **38** `Csc`
  invocations totalling **111 s** of a 68 s build. `WriteOnlyWhenDifferent="true"` makes an unchanged
  build compile nothing. This also restores the value of `AccelerateBuildsInVisualStudio=true`, which
  defaults to on and was being defeated by the same timestamps.

- **`ValidatePurviewStylePolicy` read every `.editorconfig` in the chain twice** — once to find
  `root = true` and once to merge it. The SDK's own `.editorconfig` is 2,714 lines / 95 KB, so each
  project read it twice on every compile. The file is now read once and the lines are shared.

- **Both repository-file bootstraps ran during design-time builds.** `EnsureRepositoryEditorConfigTarget`
  and `EnsureRepositoryGlobalJsonTarget` were not guarded against `DesignTimeBuild`, and an IDE runs a
  design-time build for every edit, on every project, for as long as a solution is open. Each pass
  repeated the repository-root probe ladder and two full reads of the 95 KB `.editorconfig`
  (0.83 ms of I/O per project, per pass) and could never produce anything a design-time build uses.
  Both are now skipped when `DesignTimeBuild=true`; a real build still bootstraps them, which is the
  only thing that ever needed to.

- **`BuildSdk.IntegrationTests` had the same unconditional-rewrite defect**, in its own
  `WriteSdkSourcePaths` target: `Generated/SdkPaths.cs` was written with `Overwrite="true"` and no
  `WriteOnlyWhenDifferent`, so that project recompiled on every build even after the two SDK writers
  were fixed. It is fixed the same way, and a `Release` build of this repository with no changes now
  runs **zero** `Csc` invocations in 0.85 s.

### Tests

- `IncrementalBuildTests` pins both SDK-generated files: building an unchanged project twice must
  leave `GeneratedAssemblyInfo.cs` and `GeneratedAssemblyIdentifier.cs` with their original
  timestamps and content. A rewrite is what breaks incrementality, and a rewrite always moves the
  timestamp, so the test asserts the timestamp rather than counting compiler invocations.

### Repository

- **The tracked `.editorconfig` marker now self-syncs.** It names the SDK version that wrote the file,
  and it had drifted to `1.0.7` while `package.json` was on `1.2.x` — misleading for anyone reading the
  repository, though never wrong for a consumer, because packing stamps the version in. The file is now
  in `scripts/sync-version.ts`, so `just version-sync` rewrites it along with the quick-start snippets,
  `AdoptionPin_MatchesPackageJson` asserts it, and a new `just version-sync-check` fails the pre-commit
  hook when it is stale. `CHANGELOG.md` is deliberately excluded: its marker lines are history and keep
  the version they shipped with.

### Notes

- `docs/wiki/Performance.md` now carries measured numbers for a 39-project solution instead of a list
  of things that had not been measured. The headline, measured end to end on `changeops` with this
  version: a rebuild with **no source changes went from 67.7 s to 7.2 s**, and from **38 `Csc`
  invocations to 0**. A cold build went from 154.4 s to 103.2 s. Note that the *first* warm build after
  upgrading is slower than that (27.3 s), because the upgrade invalidates analyzers, `.editorconfig`
  and compiler state at once; steady state is reached on the next no-change build.

## [1.2.0] - 2026-10-28

### Added

- **Blazor WebAssembly clients, Blazor hosts and BFF hosts are detected.** `IsBlazorWebAssemblyProject`
  (`Microsoft.NET.Sdk.BlazorWebAssembly`), `IsRazorClassLibraryProject` (`Microsoft.NET.Sdk.Razor`),
  `IsBffProject` (a web SDK project named `*Bff`, or one setting `IsBffProject=true`) and
  `IsBlazorHostProject` (a web SDK project referencing a WebAssembly client) are all compiler-visible.
  A host never implies a BFF, because a host that keeps no tokens is not one. A host `<Name>` gets an
  automatic reference to its `<Name>.Client` unless it declares one (`PurviewAutoBlazorClientReference=false`
  opts out); any other pairing is resolved during the build by `ResolvePurviewBlazorHostProject`.

- **WebAssembly client defaults.** A client is `IsPackable=false` unless it says otherwise, gets no
  container defaults even beside a `Dockerfile` (`PublishAot` on a client is a publish that fails), and
  no automatic `../Shared*` reference, so a server-side library cannot join the browser download because
  of a folder name. Globalisation, trimming and the Blazor runtime settings are left to the Blazor SDK.
  `DisablePurviewBlazorWebAssemblyDefaults=true` restores the generic defaults.

- **`PurviewBrowserSafe` makes a library the browser downloads fail the build on what would fail at
  runtime.** On a class library it applies `IsAotCompatible=true`, `<SupportedPlatform Include="browser" />`
  and `WarningsAsErrors` for the trim, single-file, AOT and `CA1416` ids (`PurviewBrowserSafeWarningCodes`)
  — each only where the project set nothing, and without `TreatWarningsAsErrors`, so the rest of its
  warning policy is untouched. `PurviewBrowserSafeWarningsAsErrors=false` keeps them as warnings.

- **`PRBLZ0001`: every library a client downloads must have been declared browser-safe.** A client, and
  each browser-safe library, asks every `ProjectReference` for its `PurviewBrowserSafe` value and reports
  the ones that do not set it, so the guarantee covers the whole download rather than its first level.
  `PurviewBrowserSafe="true"` on the `ProjectReference` records the decision for a project that does not
  use this SDK; `PurviewBrowserSafeReferenceCheckAsError=true` makes it an error and
  `DisablePurviewBrowserSafeReferenceCheck=true` turns it off.

- **BFF hosts.** No packages are added — YARP, OpenID Connect and any BFF library are the application's
  choice — and the OpenAPI interceptor namespace is dropped, because a BFF that only forwards has no
  document to generate. Referencing `Microsoft.AspNetCore.OpenApi`, or `PurviewBffExposesApi=true`,
  keeps it.

- **`PDS0008` and `PDS0009` report a BFF authentication cookie that undoes the BFF.** `HttpOnly = false`
  lets script in the page read and replay the session; `SameSite = None` makes every BFF endpoint a
  cross-site request forgery target. Only a constant assigned to the `CookieAuthenticationOptions` cookie
  in a BFF project is reported — never the antiforgery cookie or the OpenID Connect correlation and nonce
  cookies, which legitimately differ.

- **`just version-sync`** rewrites the `Purview.BuildSdk` pins in the docs and `templates/global.json`
  from `package.json`.

- **`CA1034` is suppressed on an `extension` block (`PDS0007`).** A C# 14 extension block is compiled
  into a nested type inside its owning static class, so `CA1034` ("nested types should not be visible")
  fires on every one, reported at the `extension` keyword and naming nothing because the generated type
  has no source name. The nesting is the language's own encoding of the feature rather than a design
  choice, and `CA1034` ships at `error` in the shipped `.editorconfig`, so a single extension block
  failed the build. The suppression is narrow: only the block's own declaration is suppressed, and an
  ordinary nested type keeps the warning whether it sits beside the block or inside it. Add `PDS0007` to
  `NoWarn` to restore `CA1034` for every extension block.

- **An unmarked repository `.editorconfig` is now reported instead of being silently abandoned.** A
  repository that adopted the SDK before the version marker existed has a `.editorconfig` with no marker
  on its first line, which the refresh deliberately treats as the developer's own file — so it was never
  updated and nothing said so, leaving the repository pinned to a release-old rule set indefinitely.
  Overwriting it is not safe (nothing on disk distinguishes it from a hand-authored file), so the SDK
  warns once per repository per SDK version, naming `-p:PurviewRepoBootstrapMode=Always` as the one-command
  fix. The stamp lives at `.purview/editorconfig-adoption.cache` (`PurviewEditorConfigAdoptionStampPath`),
  CI builds are skipped so advice can never fail a `-warnaserror` build
  (`PurviewEditorConfigAdoptionWarningInCI=true` opts in), and either
  `PurviewEditorConfigAdoptionWarning=false` or the existing `PurviewEditorConfigAutoUpdate=false`
  silences it.

### Fixed

- **A bootstrapped `global.json` now pins the SDK version that was actually restored.**
  `Defaults.props` is imported before `Sdk.props` detects the version from the restored package folder,
  and it set a hand-maintained literal, so detection never ran: every bootstrapped `global.json` pinned
  whatever was last typed into `Defaults.props`, which went stale on each bump. The literal is gone. The
  folder name wins, and an SDK imported from anywhere else falls back to `Props/PackedVersion.props`,
  stamped from `$(PackageVersion)` at pack time. The remaining copy-paste pins in the docs are rewritten
  by `just version-sync`, and `PackagedVersionFallbackTests` names each stale one as `file:line`.

- **Pack-time skill validation now validates the repository it is packing.** `ValidatePurviewSkills` read
  the evaluation-time `PurviewSkillsSourceFolder`, which is resolved from `RepoRoot` and an `AGENTS.md`
  walk-up. On a CI runner `RepoRoot` is taken from `GITHUB_WORKSPACE`, and it can also arrive stale from
  the version-detection cache, so `pack` could validate a *different* repository's `skills/` folder —
  passing silently while the packed repository's malformed skill went unnoticed. The target now resolves
  the same root every other skill target uses (`ResolvePurviewSkillsRepositoryRoot`), so a malformed skill
  fails `pack` in its own repository whatever the ambient environment says.

## [1.1.0] - 2026-10-21

### Performance

- **The repository bootstrap no longer runs its inline task when there is nothing to do.**
  `SyncPurviewRepositoryFiles` is a `RoslynCodeTaskFactory` task, so MSBuild compiles it on first use —
  measured at **~724 ms per build** (3,490 ms with the task skipped against 4,214 ms with it forced, on a
  real project, median of 5). That was paid on every build of every project even when the task went on to
  decide the file was already correct. The "is there anything to do" decision now happens in MSBuild
  property functions, so an established repository never pays it. `Always` and `WarnOnDrift` still take the
  full path, as does a missing destination.
- **`PurviewRepositoryRootStrategy` replaces the unconditional `git` call.** Finding the repository root
  used to spawn `git rev-parse` for every project on every build. The new default, `Markers`, walks up for
  the nearest `.git/` using plain MSBuild path checks — no process. Set `PurviewRepositoryRootStrategy=Git`
  to restore the old behaviour; the two can disagree, because git reports the *worktree* root, so a
  submodule, a linked worktree, or a package nested inside a larger repository may resolve differently.

### Changed

- **`CentralPackageTransitivePinningEnabled` now defaults to `true`**, alongside the existing
  `ManagePackageVersionsCentrally` default. A transitive dependency can no longer resolve to a version
  the repository never named: a central pin below what a transitive requires fails restore with
  `NU1109` instead of resolving upward silently. Consumers may need to raise a pin on upgrade —
  `aspire-resourcekit` needed OpenTelemetry 1.20.0 → 1.20.1, required by `TUnit.OpenTelemetry`.
- **Every `PackageVersion` names a version; the floating `*` entries are gone.** A `PackageVersion` is
  already a minimum — NuGet resolves it unless something in the graph requires higher — so floating was
  never needed to stay current, and it made the packed result depend on the day it was built.
  `Purview.Skills.Testing` ships `TUnit.Core` and `TUnit.Assertions` as dependencies, so the floor it
  declared moved from 1.75.0 to 1.76.0 between 1.0.6 and 1.1.0 purely because time passed. The TUnit
  baseline is now `1.76.0`, chosen in `Directory.Packages.props` rather than inherited from a restore.
  `CentralPackageFloatingVersionsEnabled` is no longer needed and has been removed.
- The starter `templates/Directory.Packages.props` names versions for the same reason, and referenced
  `Microsoft.SourceLink.AzureDevOps.Git`, which does not exist on nuget.org — the package is
  `Microsoft.SourceLink.AzureRepos.Git`. The floating version hid it, because nothing ever restored it.
- **CI runs every test suite, not just the unit ones.** `Build:TestFilter` was
  `/*/*/*/*[Category=Unit]/`, and the SDK derives an assembly-level TUnit category from each project's name
  suffix — so `BuildSdk.IntegrationTests` was `Category=Integration` and its 416 tests, the repository's
  main safety net, never ran on a PR. The filter is now `/*/*/*/*/`.
- **`SkillsTesting.UnitTests` is in the PR matrix.** It was absent from `pr.yml` entirely, so its 47 tests
  ran nowhere in CI regardless of the filter. The matrix now lists every project under `src/tests`.

  Together these take the PR run from the unit suites alone to all 593 tests.

### Added

- `PDS0006`, a diagnostic suppressor for `CA1815` ("override `Equals` and the equality operators on value
  types") on union declarations. A union is a value type, so `CA1815` fires on every one, but C# does not
  synthesise value equality for a union and a union is read by matching its active case rather than by
  comparison — so the rule asks for members a union cannot meaningfully provide. Analyser support for unions
  is planned for .NET 12; until then this removes the need for a `#pragma warning disable CA1815` beside every
  union declaration. Both the `union` keyword and `System.Runtime.CompilerServices.UnionAttribute` are
  recognised. A union's case types, nested types and every other struct keep the warning. Add `PDS0006` to
  `NoWarn` to turn it off.

### Added

- **The bootstrapped `.editorconfig` carries a version marker and updates itself.** The first line is
  `# Purview.BuildSdk <version> - managed file, do not edit`, and a file carrying it is refreshed when the
  SDK version changes, so a repository never silently runs a release-old rule set. A `.editorconfig`
  *without* the marker belongs to the developer and is never touched — deleting the first line is a
  supported way to take ownership. Add your own rules in a second `.editorconfig` in a subdirectory
  (`src/.editorconfig`, no `root = true`): EditorConfig applies the nearest file last, so your settings win
  and survive every update. Turn the refresh off with `PurviewEditorConfigAutoUpdate=false`, or opt out
  entirely with `BootstrapEditorConfigToRepoRoot=false` / `PurviewRepoBootstrapMode=Never`.

  The version in the marker is **stamped at build and pack time** from the version being packed, so it can
  never disagree with the package a consumer restored and is not maintained by hand. The stamped copy is
  written to `obj/` and packed from there; the tracked `.editorconfig` is never rewritten by a build, which
  would otherwise leave a dirty working tree on CI and race when projects build in parallel. The stamp
  fails the build if the source file has lost its marker, because a packed file without one would never be
  recognised as SDK-managed in a consuming repository.

### Fixed

- **`.editorconfig`: `dotnet_style_prefer_foreach_explicit_cast_in_source` was set twice in overlapping
  sections** — `when_strongly_typed:warning` under `[*.{cs,csx,cake}]` and `when_strongly_typed` (no
  severity) under `[*.{cs,csx,cake,vb,vbx}]`. Both match a `.cs` file and the later section wins, so the
  intended `warning` severity was silently discarded for every C# file. Now declared once, with the
  severity, in the broader section.
- **`.editorconfig`: `csharp_prefer_system_threading_lock` was declared twice** in the same section, the
  first without a severity. The dead declaration is gone.
- `SplitExtensionsClassCodeRefactoringProvider` built its new documents with `NormalizeWhitespace()`, which
  imposes Roslyn's own defaults — four spaces, Allman braces — and so ignored the `.editorconfig` this SDK
  ships and bootstraps into every consuming repository. A split or move produced a file that failed that
  repository's own formatting check the moment it was created. The generated syntax now carries
  `Formatter.Annotation` and is formatted against the new document's real options.
- The same refactoring threw `InvalidOperationException` when a method symbol had no resolvable declaration
  — a method from metadata, or from a partial compilation the IDE is still building. That takes down the
  light-bulb for the whole refactoring with an exception the developer can neither read nor act on. Such a
  method is now skipped, and a receiver left with no resolvable methods is skipped entirely.

### Performance

- `Sdk.props` now reads the project file once and strips its XML comments once, sharing the result
  through `_PurviewProjectText`. The six project-text scans (`IsRoslynComponent`,
  `IsRoslynComponentOnly`, `IsPackable`, `TargetFramework(s)`, `RootNamespace`,
  `PurviewTargetFrameworkSet`) each used to re-read and re-strip the file, so every evaluation of
  every project paid six times over. Measured at ~0.59 ms saved per project, ~117 ms on a 200-project
  solution — see [`docs/wiki/Performance.md`](docs/wiki/development/Performance.md).

### Fixed

- Stray trailing space in the `Props/VersionDetection.props` import path in `Sdk.props`.
- `PurviewBuildSdkVersionForGlobalJson` fell back to `1.0.0`, so a bootstrapped `global.json` pinned a
  release that predated every fix in the SDK doing the bootstrapping. It now tracks `package.json`, and
  `PackagedVersionFallbackTests` fails the build if the two drift apart again.

### Documentation

- The quick-start `global.json` snippets in `README.md` and `docs/wiki/Getting-Started.md`, and
  `templates/global.json`, pinned `1.0.0` while 1.0.7 shipped. Now pinned to the current version and
  covered by `PackagedVersionFallbackTests`.
- `README.md` and `docs/wiki/Getting-Started.md` told adopters to copy `templates/Directory.Packages.props`
  "from this package". `templates/` is not packed; both now link to the file in git and say so.
- `docs/wiki/Home.md` still described the build-time `.agents/**` mirror, which was removed outright in
  1.0.6. Replaced with the skills-CLI behaviour that superseded it.
- The analyzer rule table in `README.md` omitted `PDS0005`, and `docs/wiki/Home.md` described
  `PDS0001`–`PDS0005` as analyzers. `PDS0001` and `PDS0005` are `DiagnosticSuppressor`s, which is why
  they are absent from `AnalyzerReleases.Shipped.md`; both documents now say so.
- Added `docs/wiki/Performance.md`.

## [1.0.7]

### Changed

- Moved `Purview.Architecture.Testing` out to `purview-dev/architests`.

## [1.0.6]

### Added

- Agent skills resolved and installed explicitly, delegating to the skills CLI; authored skills are
  validated at pack time.
- Assembly name and identity generation.

### Changed

- Migrated to the skills CLI as the single installer; the build-time `.agents/**` mirror was removed.
- Architecture discovery now fails loudly instead of silently.
- `RequiredCompilerFlags` declared explicitly; `Purview.Build` installed as a .NET local tool.

### Fixed

- A packing issue.

## [1.0.5]

### Documentation

- Corrected the test-detection and namespace-stripping claims.

## [1.0.4]

### Added

- Curated target-framework sets.
- Roslyn-component-only project support.
- Additional testing types and namespace prefixes.

### Fixed

- The single-TFM floor shadowing a project's `TargetFrameworks`.
- Target frameworks resolving empty under some conditions.
- `.editorconfig` values missing test projects.
- Source-generator resolution on pack.

[Unreleased]: https://github.com/purview-dev/build-sdk/compare/v1.3.2...HEAD
[1.3.2]: https://github.com/purview-dev/build-sdk/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/purview-dev/build-sdk/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/purview-dev/build-sdk/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/purview-dev/build-sdk/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/purview-dev/build-sdk/compare/v1.0.7...v1.1.0
[1.0.7]: https://github.com/purview-dev/build-sdk/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/purview-dev/build-sdk/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/purview-dev/build-sdk/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/purview-dev/build-sdk/releases/tag/v1.0.4
