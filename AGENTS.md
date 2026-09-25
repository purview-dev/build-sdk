# AGENTS.md

Guidance for AI coding agents working in this repository.

## Customization source of truth

Use `.agents/` as the default location for **generic agentic information** shared across agent runtimes.

- `.agents/skills/` — canonical, cross-agent skills
- `.agents/agents/` — canonical, cross-agent agent workflow specs
- `.agents/prompts/` — canonical, cross-agent prompt specs

Use `.github/` customization files only as VS Code/Copilot wrappers or registration points when required by tooling.

## Purpose

This repo builds and tests `Purview.BuildSdk`, a reusable MSBuild SDK package plus analyzer and tests.

For full product behaviour and configuration, read [`README.md`](./README.md). Keep edits minimal, targeted, and convention-driven.

Repo-specific agent content lives under `src/src/BuildSdk/Sdk/.agents/` and is packed into the NuGet package as `.agents/**` by the standard `PurviewAutoSdkPack` `Sdk/` packaging logic. Add new skills under that path so they automatically flow into consuming repositories without hardcoding individual skill names.

## AgentPack folder and downstream impact

**Hard requirement:** This SDK must pack the contents of `Sdk/` into the NuGet package so that downstream consumers of `Purview.BuildSdk` receive the same `Sdk/**` files. The `PurviewAutoSdkPack` feature is the mechanism that delivers this for standard consuming projects. Do not implement `Sdk/` packaging only for the `BuildSdk` project itself.

For packable projects, `PurviewAutoSdkPack` (default `true`) automatically adds `Sdk/**/*` as `None` items with `Pack="true"` and `Visible="true"`, mapping each file to the correct location in the package:

- `Sdk/.agents/**` → `.agents/**`
- `Sdk/.github/**` → `.github/**`
- `Sdk/build/**` → `build/**`
- `Sdk/buildTransitive/**` → `buildTransitive/**`
- `Sdk/buildMultiTargeting/**` → `buildMultiTargeting/**`
- `Sdk/*.md`, `Sdk/*.png`, `Sdk/*.jpg`, etc. → package root
- everything else under `Sdk/` → `Sdk/`

The `BuildSdk.csproj` itself is an MSBuild SDK, so it disables `PurviewAutoSdkPack` and explicitly packs its `Sdk/` contents instead. This is an exception for the SDK project only; every other project that consumes this SDK relies on `PurviewAutoSdkPack` to ship its `Sdk/` folder. Consuming repositories that use this SDK get the bundled agent folder mirrored into `$(AgentPackDestinationFolder)/` (default `.agents/`) before build when `EnableAgentFolderInPackage` is `true` (default).

The mirror is change-aware and lock tolerant: `SyncPurviewRepositoryFiles` (inline task in `Sdk/Sdk.targets`) skips unchanged files via the `<repo root>/.purview/agent-sync.cache` manifest, stages every write into a temporary file next to the destination before renaming it into place, treats "another project already wrote identical content" as success, retries quietly, and escalates only after `PurviewAgentFolderCopyRetries` attempts (`PurviewAgentFolderCopyFailureAsError=false` downgrades that to a warning). `PurviewSuppressCopyRetryWarnings` (default `true`) demotes the built-in copy task's retry notice (`MSB3026`) to a message, which is set in `Sdk.targets` so it can be toggled from the project file. The same task performs the `.editorconfig`/`global.json` bootstraps (`PurviewRepoBootstrapMode`: `IfMissing`/`Always`/`WarnOnDrift`/`Never`). `PurviewAgentFolderSourcePath` defaults to the package-level `.agents` folder and is deliberately defined in `Sdk/Sdk.props` — defining it in `Sdk/Props/Defaults.props` resolves `$(MSBuildThisFileDirectory)` to `Sdk/Props/` and silently breaks the packaged layout.

During packaging, the SDK injects a `.gitignore` file into each second-level folder under `Sdk/.agents` with the content `# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore`, so the copied folder is ignored by Git in consuming repositories while keeping the folder structure discoverable.

Any edit, addition, or deletion in `src/src/BuildSdk/Sdk/.agents/` therefore changes the contents delivered to every repository that consumes this SDK.

Tests for this feature live in `src/tests/BuildSdk.IntegrationTests/AgentPackFolderTests.cs` (packaging) and `src/tests/BuildSdk.IntegrationTests/RepositoryFileSyncTests.cs` (repository mirroring, change detection, retry and failure behaviour).

## Repository map

- `src/src/BuildSdk/` — packable MSBuild SDK package (`Purview.BuildSdk`)
- `src/src/Analyzers/` — Roslyn analyzer/source-generator assembly
- `src/src/CodeFixers/` — Roslyn code-fix assembly (`Purview.BuildSdk.CodeFixers`)
- `src/tests/Analyzers.UnitTests/` — analyzer-focused unit tests
- `src/tests/Analyzers.IntegrationTests/` — analyzer integration tests (Roslyn end-to-end analyzer/suppressor/code-fix behavior)
- `src/tests/BuildSdk.IntegrationTests/` — integration harness validating SDK behaviour
- `src/BuildSdk.slnx` — solution entry point

## Test project placement and namespace conventions

When adding or changing tests in this repository:

- Keep **analyzer unit tests** (pure algorithm/utility or direct Roslyn compilation assertions) in
  `src/tests/Analyzers.UnitTests/`.
- Keep **analyzer integration tests** (behavior spanning analyzer diagnostics, suppressors, and code fixes)
  in `src/tests/Analyzers.IntegrationTests/`.
- Keep **SDK integration harness tests** in `src/tests/BuildSdk.IntegrationTests/`.

Namespace expectations:

- `Analyzers.UnitTests` sources use `Purview.BuildSdk.Analyzers`
- `Analyzers.IntegrationTests` sources use
  `Purview.BuildSdk.Analyzers`
- `BuildSdk.IntegrationTests` sources use `Purview.BuildSdk`

Do not mix analyzer unit/integration tests in the same project unless explicitly requested.

## Canonical commands

Prefer `just` tasks:

- `just restore`
- `just build`
- `just test`
- `just lint-check`
- `just lint-fix`
- `just pack`

`dotnet` fallback uses `src/BuildSdk.slnx` and `Release`.

## Testing rules (important)

This repo uses **Microsoft.Testing.Platform** (`global.json`) and **TUnit** conventions.

- Prefer `just test` first.
- If filtering tests, use `--treenode-filter` (not `--filter`).
- When passing test-runner options, keep the `--` separator with `dotnet test`.

### Cross-platform requirement (non-negotiable)

**All tests and features must work identically on Windows, Linux, and macOS.** CI runs the full
suite on `ubuntu-latest`, so a test that only passes on Windows is a failing PR. This is a hard
requirement, not a preference:

- **Never hardcode platform-specific paths** in tests, analyzer configs, or fixtures — no
  Windows drive paths (`C:\...`), no backslash-only separators, no case-insensitive path assumptions.
- **Build paths with platform APIs**: `Path.Combine`, `Path.GetTempPath()`, `Path.DirectorySeparatorChar`,
  `Path.GetFullPath()`. When a test needs a fixed fake path, normalize Windows-style literals to the
  current platform (see `AnalyzerTestInfrastructure.NormalizeFakePath` and the local
  `NamespaceCalculatorTests.NormalizeFakePath` helpers) instead of passing them raw.
- **Feed production path-math code only native-format paths.** Code like
  `ExtensionsNamespaceHelper` uses `Path`/`Uri` relative-path logic; any non-native separator or
  drive-letter literal on Linux makes it return wrong results.
- **Do not rely on environment specifics** such as case-insensitive filesystems, a `C:` drive, or
  trailing-separator behaviour — they differ per OS.
- When writing analyzer/compiler tests, keep `SyntaxTree` file paths and `build_property.*` values
  (e.g. `ProjectDir`) consistent and native on every platform.
- The harness (`ProjectHarness`) already creates throwaway projects under `Path.GetTempPath()` —
  keep it that way; never introduce fixed absolute or Windows-style paths.

For filtering syntax and troubleshooting, see:

- [`tunit-test-runner` skill](./.agents/skills/tunit-test-runner/SKILL.md)
- [`dotnet-tunit` skill](./.agents/skills/dotnet-tunit/SKILL.md)
- [`tunit-filtering` skill](./.agents/skills/tunit-filtering/SKILL.md)

### Integration harness for complex validation

Use `src/tests/BuildSdk.IntegrationTests/Harness/ProjectHarness.cs` when validating behaviour that depends on MSBuild evaluation, import order, or generated project state.

- Create throwaway projects with `ProjectHarness.For(...).BuildAsync()` (or `CreateAsync`/`CreateWithContentAsync`).
- Prefer harness evaluation helpers over brittle log parsing:
  - `GetPropertyAsync` / `GetPropertiesAsync`
  - `GetItemIdentitiesAsync` / `GetProjectReferencesAsync`
  - `GetPreprocessProjectAsync` for evaluated project inspection
- Use `BuildAsync(restore: true)` when package restore/build behaviour is part of the scenario.
- Keep scenarios minimal and deterministic; isolate one behaviour per test.
- For import-time behaviour, set required pre-import properties in harness setup (before `Sdk.props` import).

Supporting files:

- `src/tests/BuildSdk.IntegrationTests/Harness/ProjectHarness.Builder.cs`
- `src/tests/BuildSdk.IntegrationTests/TestHelpers.cs`

## Conventions to preserve

- Keep project naming aligned with repo conventions in [`README.md`](./README.md#project-naming-guide).
- Respect Central Package Management in `Directory.Packages.props`.
- Avoid unrelated refactors or formatting-only churn unless requested.
- Follow existing style and keep changes small and testable.

## Release and commit workflow

- Versioning is driven by `package.json`
- Use existing commit conventions:
  - [`git-conventional-commits` skill](./.agents/skills/git-conventional-commits/SKILL.md)
  - [`lefthook-integration` skill](./.agents/skills/lefthook-integration/SKILL.md)

## Practical guardrails for agents

- Validate changes with targeted tests first, then broader suite as needed.
- When touching test behaviour, verify with MTP/TUnit-compatible invocation.
- Prefer linking users to existing docs over duplicating long explanations in chat.
- Keep generic guidance in `.agents/`; keep `.github/` copies thin and referential.
