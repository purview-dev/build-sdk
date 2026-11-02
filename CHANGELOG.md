# Changelog

All notable changes to `Purview.BuildSdk` and `Purview.Skills.Testing`.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions come from the
`version` field in `package.json`, which is the single source of truth: a merge to `main` publishes
that version to nuget.org.

This file was reconstructed from the conventional-commit history at 1.0.7, so entries before that
release summarise what the commits recorded rather than what was written at the time.

## [Unreleased]

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
  solution — see [`docs/wiki/Performance.md`](docs/wiki/Performance.md).

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

[Unreleased]: https://github.com/purview-dev/build-sdk/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/purview-dev/build-sdk/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/purview-dev/build-sdk/compare/v1.0.7...v1.1.0
[1.0.7]: https://github.com/purview-dev/build-sdk/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/purview-dev/build-sdk/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/purview-dev/build-sdk/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/purview-dev/build-sdk/releases/tag/v1.0.4
