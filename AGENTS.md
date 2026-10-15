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

Repo-specific sub-agent definitions and prompts live under `src/src/BuildSdk/Sdk/.agents/` and are packed into the NuGet package as `.agents/**` by the standard `PurviewAutoSdkPack` `Sdk/` packaging logic.

**Skills are different: they are authored at the repository root in `skills/<name>/SKILL.md`.** They have two audiences reached by two routes — a consuming .NET repository, which receives them mirrored into `.agents/` from the NuGet package, and the skills CLI (`npx skills`), which clones the *git* repository. The CLI only discovers skills at a root `skills/` folder, and the mirrored `.agents/` folder is gitignored in every consuming repository, so it is absent from a clone. One authored copy at the root serves both: `IncludePurviewSkillsInPackage` packs it into the package's `.agents/skills/`, so the existing mirror is unchanged.

Add a new skill under `skills/`, not under `Sdk/.agents/skills/`. `PurviewPackRepositorySkills` (set on `BuildSdk.csproj`) is what opts a project into packing them; it defaults to `false` so a repository with several packable projects does not ship the same skills in every package.

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

During packaging, the SDK injects a `.gitignore` file into each second-level folder under `Sdk/.agents` with the content `# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# This file is mirrored from a NuGet package, so ignore it too\n/.gitignore`, so the copied folder is ignored by Git in consuming repositories while keeping the folder structure discoverable. At mirror time the SDK also writes a generated `<destination root>/.gitignore` listing every mirrored file by path, which covers the folders it does not wholly own (`.agents/agents/`, `.agents/prompts/`) without hiding files the repository authored beside them.

Git only ignores untracked paths, so a mirrored file that is already tracked is rewritten by every upgrade that changes it and shows up as a local modification. The SDK cannot untrack it, so it reports `PurviewTrackedMirroredFile` (naming the paths and the `git rm --cached` command) on a build that mirrored that file; `DisableMirroredFileTrackingCheck=true` opts out. The check shells out to `git ls-files` only when something was mirrored, so an up-to-date build spawns no process.

Any edit, addition, or deletion in `src/src/BuildSdk/Sdk/.agents/` or in the repository-root `skills/` folder therefore changes the contents delivered to every repository that consumes this SDK.

Tests for this feature live in `src/tests/BuildSdk.IntegrationTests/AgentPackFolderTests.cs` (packaging) and `src/tests/BuildSdk.IntegrationTests/RepositoryFileSyncTests.cs` (repository mirroring, change detection, retry and failure behaviour).

## Explicit skill installation

The mirror above delivers skills into `.agents/`, which no agent reads natively. Installing them into the agent-specific directories is a separate, **explicit** step — it must never be a side effect of a restore, a build, a design-time build or a test run.

Two targets, neither with `BeforeTargets`/`AfterTargets`:

```bash
dotnet msbuild <project> -t:ListPurviewSkills      # diagnostics only, installs nothing
dotnet msbuild <project> -t:InstallPurviewSkills   # delegates to the skills CLI
```

`ResolvePurviewSkillSources` (inline task in `Sdk/Sdk.targets`) does the resolution. There is deliberately **no custom `skills.json` manifest**: a package that ships skills already carries both halves of the mapping, and the resolver reads them.

- `.agents/skills/<name>/` in the restored package says which skills it ships — the same folder the mirror already reads.
- The nuspec's `<repository url="..." commit="..." />` element, written by SourceLink for every package built by this SDK, says which git repository and which exact commit produced them.

That makes version compatibility exact by construction: the resolved ref is the commit the *restored package version* was built from, so an older package reference gets that version's skills rather than whatever is on the default branch. A package whose nuspec has no GitHub repository URL, or no commit, cannot be pinned — it is reported as `PRSKL0002` rather than installed from a guessed branch. `PRSKL0001` is raised when `project.assets.json` is missing, because resolution reads the restore graph and deliberately does not trigger a restore of its own.

Installation itself is delegated to the skills CLI (`npx skills`) rather than reimplemented, because the CLI owns the agent-specific destinations and their discovery conventions — which differ per agent and change independently of this SDK — and owns `skills-lock.json`, so a repository can restore the same set later with `npx skills experimental_install`. `PurviewSkillsAgents` (default `*`) narrows the target agents.

### Detecting the CLI

The CLI is Node tooling, so it is not guaranteed on a machine that can build .NET. `DetectPurviewSkillsCli` (inline task) probes `PATH` for `skills` first, then `npx`, honouring `PATHEXT` on Windows where both are `.cmd` shims. `PATH` is read directly rather than by spawning a version check: it is deterministic, costs no process, and cannot hang on a cold `npx` download.

- `skills` on `PATH` is used as-is — no download, works offline.
- Otherwise `npx -y skills@1`, pinned to a major so a future breaking CLI release cannot change what the target does.
- Neither found is `PRSKL0004`, naming the fix (install Node, or `npm install -g skills`) instead of surfacing a raw `'npx' is not recognized`.
- `PurviewSkillsCommand` overrides the launcher entirely and skips detection.

`ListPurviewSkills` reports CLI availability but **never** errors on its absence — resolving and reporting needs no Node tooling at all.

### Validating skills

`ValidatePurviewSkills` checks that every authored skill is well formed, and `IncludePurviewSkillsInPackage` depends on it — so a malformed skill fails the pack in the repository that owns it, rather than shipping a skill the CLI will silently skip. It checks:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description` (a `>-` block scalar counts),
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads the folder, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`, one per problem. When `PurviewSkillsToPack` is set, only that subset is checked, so one project is not failed by another project's skill. Prose quality and whether an agent obeys a skill are deliberately **not** checked — a build can only check that a skill is well formed.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillValidationTests.cs`.

### Installing from an explicit source

`PurviewSkillsSource` installs straight from a git source, bypassing package resolution, for a repository that wants a Purview skill it has no `PackageReference` for:

```bash
dotnet msbuild <project> -t:InstallPurviewSkills -p:PurviewSkillsSource=purview-dev/engineering-playbook
```

The caller owns the ref in that case — append `#<ref>` to pin it. This path needs no restore, so `PRSKL0001` does not apply.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillResolutionTests.cs`, including the guarantee that an ordinary restore-and-build writes no lockfile and creates no agent directory.

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
