# AGENTS.md

Guidance for AI coding agents working in this repository.

## Customization source of truth

Use `.agents/` as the default location for **generic agentic information** shared across agent runtimes.

- `.agents/agents/` — canonical, cross-agent agent workflow specs (authored, tracked)
- `.agents/prompts/` — canonical, cross-agent prompt specs (authored, tracked)
- `.agents/skills/` — **not authored.** Written by the skills CLI and gitignored; see below.

Use `.github/` customization files only as VS Code/Copilot wrappers or registration points when required by tooling.

## This repository's own skills are installed, not mirrored

Skills are authored at the repository root in `skills/<name>/SKILL.md`, and this repository installs its own through the skills CLI — the same mode it ships to consumers. Run it after adding, renaming or editing a skill:

```bash
just skills-install     # dotnet msbuild ... -t:InstallPurviewSkills -p:PurviewSkillsSource=.
```

The source is `.`, not a pinned git ref, because this repository *authors* these skills: installing from the working tree is what makes an edit visible to an agent without a push-and-reinstall round trip. The CLI records that portably as `"source": "."` / `"sourceType": "local"` in `skills-lock.json`, which **is committed** — it is the record of what should be installed, and `npx skills experimental_install` restores it in a fresh clone.

Everything the CLI writes is gitignored, because all of it is reproducible from `skills/` plus the lockfile:

| Path | Who writes it | Tracked |
| -- | -- | -- |
| `skills/<name>/` | authored by hand | yes |
| `skills-lock.json` | the CLI | yes |
| `.agents/skills/<name>/` | the CLI (its "universal" location, shared by most agents) | no |
| `.claude/skills/`, `agent/skills/` | the CLI (per-agent links into the universal location) | no |

Only `.agents/skills/` is ignored here, not the whole of `.agents/`, because `.agents/agents/` and `.agents/prompts/` are authored content.

**Skills have exactly one installer, and it is the skills CLI.** The `.agents` mirror does not copy them (`PurviewMirrorPackagedSkills`, default `false`). A package still ships `.agents/skills/<name>/`, but as *metadata* — it is what `ResolvePurviewSkillSources` reads to map a package to its skills and commit, not content to copy. The CLI's universal install location is that same folder, so two writers would mean the mirror silently overwrites a pinned install on the next build while `PRSKL0011` compares a lockfile ref against content the mirror already replaced. `SyncPurviewRepositoryFiles` takes `ExcludeRelativeFolders` for this; both mirror targets pass `skills`.

Link to a skill by its **authored** path (`./skills/<name>/SKILL.md`). A link into `.agents/skills/` points at a gitignored file and is broken in a fresh clone; `RepositorySkillsSelfTests` fails the build if one reappears.

## Purpose

This repo builds and tests `Purview.BuildSdk`, a reusable MSBuild SDK package plus analyzer and tests.

For full product behaviour and configuration, read [`README.md`](./README.md). Keep edits minimal, targeted, and convention-driven.

Repo-specific sub-agent definitions and prompts live under `src/src/BuildSdk/Sdk/.agents/` and are packed into the NuGet package as `.agents/**` by the standard `PurviewAutoSdkPack` `Sdk/` packaging logic.

**Skills are different: they are authored at the repository root in `skills/<name>/SKILL.md`** (see above). They have two audiences reached by two routes — a consuming .NET repository, which receives them mirrored into `.agents/` from the NuGet package, and the skills CLI (`npx skills`), which clones the *git* repository. The CLI only discovers skills at a root `skills/` folder, and the mirrored `.agents/` folder is gitignored in every consuming repository, so it is absent from a clone. One authored copy at the root serves both: `IncludePurviewSkillsInPackage` packs it into the package's `.agents/skills/`, so the existing mirror is unchanged.

Add a new skill under `skills/`, not under `Sdk/.agents/skills/`, then run `just skills-install`. `PurviewPackRepositorySkills` (set on `BuildSdk.csproj`) is what opts a project into packing them; it defaults to `false` so a repository with several packable projects does not ship the same skills in every package.

`Sdk/.agents/` holds only `agents/` and `prompts/` — there is no `Sdk/.agents/skills/` any more, so nothing regenerates a `.gitignore` under `.agents/skills/`.

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

### Package runners and the repository root

Two invariants govern how the CLI is launched, and both are load-bearing.

**Any package runner, defaulting to npx.** The CLI is published to npm and each runner spells "run this npm package" differently. `PurviewSkillsRunner` (default `npx`, because npx ships with Node itself) selects one; the mapping lives in the `PurviewSkillsRunnerLauncher` item group in `Sdk/Sdk.targets`, which carries an `Exec` value (version-pinned, for `InstallPurviewSkills`) and a `Display` value (unpinned, for recommendation text) per runner. A runner with no entry falls through to `<runner> $(PurviewSkillsPackage)`, so an unknown runner still works. Each invocation form was verified against the real tool — `npx -y`, `bunx`, `bun x`, `pnpm dlx`, `yarn dlx` (Yarn 2+ only), `deno run -A npm:` — because a wrong form is a command that simply does not run.

`DetectPurviewSkillsCli` (inline task) probes `PATH` for `skills` first, then `$(PurviewSkillsRunner)`, then `$(PurviewSkillsRunnerCandidates)`, honouring `PATHEXT` on Windows where these are `.cmd` shims. `PATH` is read directly rather than by spawning a version check: deterministic, no process, cannot hang on a cold download. The runner actually found wins over the configured preference. Nothing found is `PRSKL0004`; `PurviewSkillsCommand` overrides the launcher entirely and skips detection.

**Always the repository root.** `skills-lock.json` and the agent directories belong to the repository, not to a project, so `ResolvePurviewSkillsRepositoryRoot` resolves one root and every skill target runs from it: `PurviewSkillsInstallRoot` → a user-set `PurviewSkillsRepoRoot` → the marker walk → `PurviewSkillsRepoRoot`/`RepoRoot` as guessed → CI workspace variables → SourceLink's git `SourceRoot`. There is deliberately **no project-directory fallback** — that would scatter a lockfile and an agent directory through the tree, one per project. An unresolved root is `PRSKL0003` on install (naming `PurviewSkillsInstallRoot`), and silence for recommendations, because a build never fails over advice.

The marker walk is `FindPurviewRepositoryRoot` (inline task), driven by `PurviewRepositoryRootMarkers` — `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json`. Three things about it are load-bearing and easy to break:

- **It cannot be an MSBuild intrinsic.** `GetDirectoryNameOfFileAbove` matches *files only* and returns nothing for `.git/`, `.github/`, `.agents/` or `.claude/` — four of the nine markers. Verified, not assumed.
- **Two passes.** The first marker in the list is definitive and is searched all the way up (`.git/` *is* the repository root, so a monorepo sub-package does not win). Every other marker uses nearest-ancestor-wins, with list order breaking ties inside one directory. Searching them all to the top would let a stray `.claude/` in a developer's home directory outrank the real root.
- **The home directory and everything above it are never candidates.** Without that bound, a project outside any repository resolves to `~` and installs into it. A repository below the home directory is unaffected.

The marker walk outranks the evaluation-time `PurviewSkillsRepoRoot` guess (which only knows `RepoRoot` and an `AGENTS.md` walk-up), because an `AGENTS.md` can legitimately sit in a subdirectory. `_PurviewSkillsRepoRootWasSpecified` is captured in `Sdk.props` before defaulting so a value the *user* set still wins.

`ListPurviewSkills` reports runner availability but **never** errors on its absence — resolving and reporting needs no Node tooling at all.

Note that a target's `Condition` is evaluated *before* its `DependsOnTargets` run, so a property a dependency computes is still empty there. That is why the unresolved-root guard sits on the `EvaluatePurviewSkillState` task rather than on `RecommendPurviewSkills` — and equally why the build hook's CI/design-time gating works at all.

### Validating skills

`ValidatePurviewSkills` checks that every authored skill is well formed, and `IncludePurviewSkillsInPackage` depends on it — so a malformed skill fails the pack in the repository that owns it, rather than shipping a skill the CLI will silently skip. It checks:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description` (a `>-` block scalar counts),
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads the folder, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`, one per problem. When `PurviewSkillsToPack` is set, only that subset is checked, so one project is not failed by another project's skill. Prose quality and whether an agent obeys a skill are deliberately **not** checked — a build can only check that a skill is well formed.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillValidationTests.cs`.

### Recommending skills

Installation is explicit, but **recommendation runs during an ordinary build** — it is the one skill feature that does. `RecommendPurviewSkillsOnBuild` (`AfterTargets="Build"`) delegates to `RecommendPurviewSkills`, which can also be invoked by name:

```bash
dotnet msbuild <project> -t:RecommendPurviewSkills
```

`EvaluatePurviewSkillState` (inline task in `Sdk/Sdk.targets`) joins the skills each restored package ships against the CLI's own `skills-lock.json` and classifies every one: satisfied, missing (`PRSKL0010`), ref-drifted (`PRSKL0011`), unverifiable (`PRSKL0012`), orphaned (`PRSKL0013`), or recorded-but-never-restored (`PRSKL0014`). Both inputs are local, so it stays offline and spawns no process.

The lockfile is read directly rather than by parsing `npx skills list --json`, because that listing omits the `ref` — and the ref is the whole compatibility question. Reading a file also needs no Node tooling, which is what keeps recommendations working on a machine that can build .NET but has no Node. There is deliberately no competing lockfile.

Drift **never** recommends `npx skills update`: `update` installs the newest published skill, which is only correct when the newest skill happens to describe the referenced package version. `PRSKL0011` names the referenced package's own commit in a pinned `npx skills add` instead.

Four things keep it quiet, and all of them are load-bearing:

- design-time builds (`DesignTimeBuild`), CI builds (`ContinuousIntegrationBuild`/`TF_BUILD`/`GITHUB_ACTIONS`/`CI` unless `PurviewSkillRecommendationsInCI=true`), non-default TFM passes and `EnablePurviewSkillRecommendations=false` are all skipped outright;
- a fingerprint stamp (`<repo root>/.purview/skill-recommendations.cache`) means a given state is reported once per repository rather than once per project and again on every rebuild — the explicit target ignores the stamp;
- missing and drifted are warnings (actionable in one command), while unverifiable and orphaned are messages, so no amount of skill advice can break a build using `TreatWarningsAsErrors`; and
- a correctly installed set produces no output at all.

Orphan detection is **owner-scoped on purpose**: a lockfile cannot record who asked for an entry, so only an entry under an owner the project references — but from a repository it no longer references — is reported. A skill from any other owner is the developer's own and is never mentioned.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillRecommendationTests.cs`, with the shared on-disk fixtures (fake restored package, assets file, lockfile) in `SkillPackageFixtures.cs`. Runner mapping and repository-root resolution are tested in `PurviewSkillInstallCommandTests.cs`.

Two harness facts to know when adding tests here. `BuildingProject` is only `true` when the `Build` target actually runs, so tests driving the build hook by name must supply it — `AnOrdinaryBuild_ReportsSkillState` covers the genuine end-to-end path. And the throwaway solution is not a git repository and has no `AGENTS.md`, so the repository root does not resolve on its own: write an `AGENTS.md` beside the solution, or pass `-p:PurviewSkillsInstallRoot`. Never assert runner behaviour by letting a test actually invoke `npx`/`pnpm dlx` — that downloads from the network and is not a unit of this SDK.

### Installing from an explicit source

`PurviewSkillsSource` installs straight from a git source, bypassing package resolution, for a repository that wants a Purview skill it has no `PackageReference` for:

```bash
dotnet msbuild <project> -t:InstallPurviewSkills -p:PurviewSkillsSource=purview-dev/engineering-playbook
```

The caller owns the ref in that case — append `#<ref>` to pin it. This path needs no restore, so `PRSKL0001` does not apply.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillResolutionTests.cs`, including the guarantee that an ordinary restore-and-build writes no lockfile and creates no agent directory.

## Purview.Architecture.Testing

`src/src/ArchitectureTesting/` ships a second package: the architecture rule engine other repositories consume, so the policy is defined once rather than copied into every repo's test project.

Three layers, deliberately separated:

- **`ArchitecturePolicy` / `ProjectGraph`** — read `.config/architecture-policy.json` and the `.csproj` graph. No MSBuild or test-framework types.
- **`ArchitecturePolicyEvaluator`** — the rules, returning typed `ArchitectureViolation` lists per rule. Takes an in-memory graph, so a rule is tested as a three-project fixture rather than a repository on disk (`ArchitectureTesting.UnitTests`).
- **`PurviewArchitectureTests`** — a TUnit base class wrapping the evaluator, one `[Test]` per rule.

A consuming repository adds a policy file and `[InheritsTests] public sealed class ArchitectureTests : PurviewArchitectureTests;`.

**`[InheritsTests]` is load-bearing.** TUnit does not walk base types during discovery, so without it the class compiles, the run is green and nothing is enforced. `InheritedBaseClassTests` exists to prove discovery works — it is the test that would catch that regression, and it runs against a temporary fixture repository because this repo's own projects (an MSBuild SDK and Roslyn components) are not described by the responsibility catalogue.

The policy JSON Schema lives at the repository root (`architecture-policy.schema.json`), embedded as a resource for offline validation and published at its raw GitHub URL for editors — the same arrangement `Purview.Build` uses for `purview-build.json`.

This project sets `RootNamespace`, `AssemblyName` and `AutoIncludeUsings=false`, because the package namespace deliberately sits outside the repository's `Purview.BuildSdk` prefix; without that the SDK emits a `global using` for a namespace this assembly does not contain.

## Repository map

- `src/src/ArchitectureTesting/` — packable architecture rule engine (`Purview.Architecture.Testing`)
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

- [`tunit-test-runner` skill](./skills/tunit-test-runner/SKILL.md)
- [`dotnet-tunit` skill](./skills/dotnet-tunit/SKILL.md)
- [`tunit-filtering` skill](./skills/tunit-filtering/SKILL.md)

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
  - [`git-conventional-commits` skill](./skills/git-conventional-commits/SKILL.md)
  - [`lefthook-integration` skill](./skills/lefthook-integration/SKILL.md)

## Practical guardrails for agents

- Validate changes with targeted tests first, then broader suite as needed.
- When touching test behaviour, verify with MTP/TUnit-compatible invocation.
- Prefer linking users to existing docs over duplicating long explanations in chat.
- Keep generic guidance in `.agents/`; keep `.github/` copies thin and referential.
