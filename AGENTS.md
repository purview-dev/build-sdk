# AGENTS.md

Guidance for AI coding agents working in this repository.

## Customization source of truth

Use `.agents/` as the default location for **generic agentic information** shared across agent runtimes.

- `.agents/agents/` — canonical, cross-agent agent workflow specs (authored, tracked)
- `.agents/prompts/` — canonical, cross-agent prompt specs (authored, tracked)
- `.agents/skills/` — **not authored.** Skills installed from *other* sources, written by the skills CLI and gitignored; see below.

Use `.github/` customization files only as VS Code/Copilot wrappers or registration points when required by tooling.

## Two skills folders, two purposes

This distinction is the one most easily got wrong, and getting it wrong produces two copies that nothing keeps in step.

| Folder | Holds | Written by | Tracked |
| -- | -- | -- | -- |
| `skills/<name>/` | the skills this repository **authors and publishes** | by hand | yes |
| `skills-lock.json` | the record of skills installed **from other sources** | the skills CLI | yes |
| `.agents/skills/<name>/` | skills installed from other sources (the CLI's "universal" location) | the CLI | no |
| `.claude/skills/`, `agent/skills/` | per-agent links into the universal location | the CLI | no |

### `skills/` is published, not installed

Every skill in `skills/` describes a product this repository ships — `Purview.BuildSdk` or `Purview.Skills.Testing` — and exists so **another** repository can install it:

```bash
npx skills add purview-dev/build-sdk
```

They are not installed into this repository's own agent directories. An agent working here reads them at their authored path, which is what AGENTS.md links to, and what the skills CLI clones. Installing them into `.agents/skills/` as well would put this repository's own published advice into the folder reserved for other people's, and leave two copies to drift apart. `RepositorySkillsSelfTests.AuthoredSkillsAreNotInstalledIntoTheAgentDirectories` enforces that.

Naming follows from the same fact. Skills install into one flat directory per agent, shared with every skill a developer has installed from anywhere, so **every skill here carries the `purview-buildsdk-` prefix**. `sdk-*`, `project-*`, `test-*` and `build-*` are far too common to use unprefixed, and the loser of a collision is whichever installed second. `RepositorySkillTests.EverySkill_IsNamedForThisProduct` enforces that.

Guidance about working *in this repository* belongs in this file, not in a skill — a skill here ships to someone else.

### `.agents/skills/` is for skills from elsewhere

To add one:

```bash
just skills-add purview-dev/engineering-playbook      # append '#<ref>' to pin it
```

Everything the CLI writes is gitignored, because it is all reproducible from the committed `skills-lock.json` with `npx skills experimental_install`. Only `.agents/skills/` is ignored, not the whole of `.agents/`, because `.agents/agents/` and `.agents/prompts/` are authored content.

**Skills have exactly one installer, and it is the skills CLI.** The SDK writes nothing into an agent directory at all — the `.agents` mirror that used to copy a package's `agents/` and `prompts/` has been removed outright. A package still ships `.agents/skills/<name>/`, but as *metadata*: it is what `ResolvePurviewSkillSources` reads to map a package to its skills and commit, not content to copy.

The single-writer rule is why. The CLI's universal install location is that same folder, so a second writer would mean the mirror silently overwriting a pinned install on the next build while `PRSKL0011` compared a lockfile ref against content the mirror had already replaced. See "The agent-folder mirror was removed" below for what went with it.

Link to a skill by its **authored** path (`./skills/<name>/SKILL.md`). A link into `.agents/skills/` points at a gitignored file and is broken in a fresh clone; `RepositorySkillsSelfTests` fails the build if one reappears.

## Purpose

This repo builds and tests `Purview.BuildSdk`, a reusable MSBuild SDK package plus analyzer and tests.

For full product behaviour and configuration, read [`README.md`](./README.md). Keep edits minimal, targeted, and convention-driven.

**Prefer a skill to a prompt, and a skill to a shipped sub-agent.** This SDK used to ship two prompts and two sub-agent definitions under `Sdk/.agents/`. All four are gone: `sdk-review-repository-shape` and `sdk-repository-rationaliser` became `purview-buildsdk-repository-review`, `sdk-consumer-setup` became `purview-buildsdk-adoption`, and `sdk-diagnose-agent-folder-copy` described a feature that no longer exists. A prompt or a mirrored agent file is inert — it lands somewhere no agent reads natively, so somebody has to know it exists. A skill carries a `description` that an agent matches against the task, so it fires when it is relevant. Anything task-shaped and reusable belongs in `skills/`.

**Skills are authored at the repository root in `skills/<name>/SKILL.md`** (see above), and published only through the skills CLI (`npx skills`), which clones the *git* repository and only discovers a root `skills/` folder. No package carries them.

Add a new skill under `skills/` and name it `purview-buildsdk-<topic>`. Nothing packs them into a NuGet package — `ValidatePurviewSkills` runs before `Pack` so a malformed skill fails here, and the CLI delivers them from git.

The authored set, and what each one answers:

| Skill | Answers |
| -- | -- |
| [`purview-buildsdk-adoption`](./skills/purview-buildsdk-adoption/SKILL.md) | wiring the SDK into a repository, and why a property had no effect |
| [`purview-buildsdk-configuration`](./skills/purview-buildsdk-configuration/SKILL.md) | which property controls this, and where must it be set |
| [`purview-buildsdk-project-layout`](./skills/purview-buildsdk-project-layout/SKILL.md) | where does a project go, and what is it called |
| [`purview-buildsdk-project-detection`](./skills/purview-buildsdk-project-detection/SKILL.md) | why did the SDK do that automatically |
| [`purview-buildsdk-testing`](./skills/purview-buildsdk-testing/SKILL.md) | writing TUnit tests, and running them under MTP |
| [`purview-buildsdk-skill-tests`](./skills/purview-buildsdk-skill-tests/SKILL.md) | authoring a skill, and validating it with `Purview.Skills.Testing` |
| [`purview-buildsdk-skill-installation`](./skills/purview-buildsdk-skill-installation/SKILL.md) | installing the skills a package ships, and the `PRSKL*` diagnostics |
| [`purview-buildsdk-repository-review`](./skills/purview-buildsdk-repository-review/SKILL.md) | auditing a repository against the SDK's conventions |

After editing one, run `just skills-validate` for the pack-time check and `just test` for the full rule set.

There is no `Sdk/.agents/` folder any more, and the SDK writes nothing into an agent directory.

## Sdk folder packaging and downstream impact

**Hard requirement:** This SDK must pack the contents of `Sdk/` into the NuGet package so that downstream consumers of `Purview.BuildSdk` receive the same `Sdk/**` files. The `PurviewAutoSdkPack` feature is the mechanism that delivers this for standard consuming projects. Do not implement `Sdk/` packaging only for the `BuildSdk` project itself.

For packable projects, `PurviewAutoSdkPack` (default `true`) automatically adds `Sdk/**/*` as `None` items with `Pack="true"` and `Visible="true"`, mapping each file to the correct location in the package:

- `Sdk/.github/**` → `.github/**`
- `Sdk/build/**` → `build/**`
- `Sdk/buildTransitive/**` → `buildTransitive/**`
- `Sdk/buildMultiTargeting/**` → `buildMultiTargeting/**`
- `Sdk/*.md`, `Sdk/*.png`, `Sdk/*.jpg`, etc. → package root
- everything else under `Sdk/` → `Sdk/`

The `BuildSdk.csproj` itself is an MSBuild SDK, so it disables `PurviewAutoSdkPack` and explicitly packs its `Sdk/` contents instead. This is an exception for the SDK project only; every other project that consumes this SDK relies on `PurviewAutoSdkPack` to ship its `Sdk/` folder.

### The agent-folder mirror was removed

The SDK used to mirror a package's `.agents/**` into the consuming repository before build. That whole feature is gone — `EnableAgentFolderInPackage`, `AgentPackDestinationFolder`, `PurviewAgentFolderSourcePath`, the `PurviewAgentFolderCopy*` properties, `PurviewAgentSyncManifestPath`, `DisableMirroredFileTrackingCheck`/`PurviewTrackedMirroredFile`, the `agent-sync.cache` manifest, the generated destination `.gitignore` files, and the `Mirror` mode of `SyncPurviewRepositoryFiles` along with it.

It was removed rather than defaulted off because it duplicated the skills CLI, and duplication here was not neutral: the CLI's universal install location is `.agents/skills/`, so the two were two writers on one set of paths. The mirror would silently overwrite a pinned install on the next build while `PRSKL0011` compared a lockfile ref against content the mirror had already replaced. Once skills are the only thing worth delivering, and the CLI delivers them, the mirror had nothing left to carry.

One consequence worth knowing: `.agents` used to be **excluded** from the scan that decides whether a lockfile has ever been restored (`EvaluatePurviewSkillState`), precisely because the mirror wrote there and finding it proved nothing. It is now **included** — excluding it would report `PRSKL0014` against a repository whose skills are correctly installed. `PurviewSkillRecommendationTests.AgentsSkillsFolder_CountsAsARestoredInstall` pins that.

`SyncPurviewRepositoryFiles` (inline task in `Sdk/Sdk.targets`) survives, doing only what it still needs to: the `.editorconfig`/`global.json` bootstraps (`PurviewRepoBootstrapMode`: `IfMissing`/`Always`/`WarnOnDrift`/`Never`). Every project in a solution writes those same destinations, so an existing file is never overwritten unless asked — which turns a lost race between parallel projects into a no-op — each write is staged into a temporary file beside the destination and renamed into place, retries are quiet, and only a failure surviving every retry is reported. `PurviewSuppressCopyRetryWarnings` (default `true`) demotes the built-in copy task's `MSB3026` notice to a message.

Any edit, addition, or deletion in the repository-root `skills/` folder changes the contents delivered to every repository that consumes this SDK.

Tests live in `src/tests/BuildSdk.IntegrationTests/RepositoryFileSyncTests.cs` (bootstrap writes and the style policy that reads them).
## Explicit skill installation

Installing skills into the agent-specific directories is a separate, **explicit** step — it must never be a side effect of a restore, a build, a design-time build or a test run.

Two targets, neither with `BeforeTargets`/`AfterTargets`:

```bash
dotnet msbuild <project> -t:ListPurviewSkills      # diagnostics only, installs nothing
dotnet msbuild <project> -t:InstallPurviewSkills   # delegates to the skills CLI
```

`ResolvePurviewSkillSources` (inline task in `Sdk/Sdk.targets`) does the resolution. There is deliberately **no custom `skills.json` manifest**: a package that ships skills already carries both halves of the mapping, and the resolver reads them.

- `.agents/skills/<name>/` in the restored package says which skills it ships.
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

`ValidatePurviewSkills` runs `BeforeTargets="Pack"` and checks that every authored skill is well formed, so a malformed skill fails in the repository that owns it rather than reaching the CLI, which would silently skip it. It validates the folder under the root `ResolvePurviewSkillsRepositoryRoot` resolved — the same one install and recommendation use — rather than the evaluation-time `PurviewSkillsSourceFolder`, because that guess only knows `RepoRoot` and an `AGENTS.md` walk-up: on a CI runner `RepoRoot` comes from `GITHUB_WORKSPACE`, so reading it there validated a different repository's skills and passed silently. It checks:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description` (a `>-` block scalar counts),
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads the folder, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`, one per problem. Every authored skill is checked: nothing is packed any more, so there is no per-project subset to narrow to. Prose quality and whether an agent obeys a skill are deliberately **not** checked — a build can only check that a skill is well formed.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillValidationTests.cs`.

## Purview.Skills.Testing

`src/src/SkillsTesting/` ships a second package: the Agent Skills conformance rule set. It is shaped for the same reason as every rule engine: the rules are defined once rather than reimplemented in every repository that authors skills.

Three layers, deliberately separated:

- **`SkillManifestReader` / `SkillCatalogue`** — read `skills/*/SKILL.md`. No MSBuild and no test-framework types. The frontmatter parser covers the subset of YAML the specification defines rather than taking a YAML dependency, because this assembly is referenced by a consuming repository's test project and a transitive YAML parser is a dependency conflict waiting to happen for a format whose whole surface is six fields. Anything it cannot parse becomes a reported structural error, never a silent drop.
- **`SkillPolicyEvaluator`** — the rules, returning typed `SkillViolation` lists. It takes a `SkillCatalogue`, so a rule is tested against a fixture folder rather than a repository on disk (`SkillsTesting.UnitTests`).
- **`PurviewSkillTests`** — a TUnit base class, one `[Test]` per rule.

A consuming repository adds `[InheritsTests] public sealed class SkillTests : PurviewSkillTests;`. **`[InheritsTests]` is load-bearing** for the same reason it is in the architecture package: TUnit does not walk base types during discovery, so without it the class compiles, the run is green and nothing is validated. `InheritedBaseClassTests` proves discovery works, against a temporary fixture so the assertion is about discovery rather than about whatever is authored in `skills/` today.

The constraints come from <https://agentskills.io/specification>: `name` is 1–64 characters of lowercase `a-z0-9` and single hyphens matching the folder name; `description` is 1–1024 characters; `license`, `compatibility` (≤500), `metadata` (string→string) and `allowed-tools` are the only other defined fields; `SKILL.md` should stay under 500 lines with the detail in `references/`.

Two rules are worth knowing the reasoning for. A repository with **no** `skills/` folder passes everything, because authoring skills is optional — but an **empty** folder fails `skills-discovery`, because every other rule is vacuous against an empty catalogue, exactly as `graphDiscovery` guards the architecture rules. And `skill-bundled-resources` rejects a link that resolves *outside* the skill folder, not only a missing one: only the skill's own folder is installed, so such a link works in this repository and nowhere else.

### The repository-root walk exists twice

`SkillRepositoryRoot` is a managed port of the SDK's `FindPurviewRepositoryRoot` inline task, with the same `PurviewRepositoryRootMarkers` defaults. It has to be: the build installs skills into the root MSBuild resolves, and the tests validate the skills in the root the managed port resolves, so a divergence means validating one folder while installing another — and neither side would report anything.

`SkillRepositoryRootParityTests` in `BuildSdk.IntegrationTests` is what catches that. It drives the real MSBuild target through `ProjectHarness` and the real managed resolver over the same folder and compares both the root and the matched marker, for every default marker and for the definitive-marker and nearest-marker cases. Each implementation is independently tested against its own expectations, so both would stay green while drifting apart; only the comparison catches it.

`RepositorySkillTests` is this repository's self-validation. It deliberately has **no `RepositoryRoot` override**, so an ordinary test run exercises the default resolution, and `SkillsFolder_ResolvesToThisRepository` asserts that it landed here — the inherited rules would otherwise all pass vacuously against a catalogue that was never found.

Tests live in `src/tests/SkillsTesting.UnitTests/`, with the repository-level ones in `src/tests/BuildSdk.IntegrationTests/RepositorySkillTests.cs`, `SkillRepositoryRootParityTests.cs` and `RepositorySkillsSelfTests.cs`.

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

## Repository map

- `src/src/SkillsTesting/` — packable agent-skill rule engine (`Purview.Skills.Testing`)
- `src/src/BuildSdk/` — packable MSBuild SDK package (`Purview.BuildSdk`)
- `src/src/Analyzers/` — Roslyn analyzer/source-generator assembly
- `src/src/CodeFixers/` — Roslyn code-fix assembly (`Purview.BuildSdk.CodeFixers`)
- `src/tests/Analyzers.UnitTests/` — analyzer-focused unit tests
- `src/tests/Analyzers.IntegrationTests/` — analyzer integration tests (Roslyn end-to-end analyzer/suppressor/code-fix behavior)
- `src/tests/SkillsTesting.UnitTests/` — skill rule tests, against fixture skills folders
- `src/tests/BuildSdk.IntegrationTests/` — integration harness validating SDK behaviour
- `skills/` — the agent skills this repository authors and publishes
- `src/BuildSdk.slnx` — solution entry point

## Test project placement and namespace conventions

When adding or changing tests in this repository:

- Keep **analyzer unit tests** (pure algorithm/utility or direct Roslyn compilation assertions) in
  `src/tests/Analyzers.UnitTests/`.
- Keep **analyzer integration tests** (behavior spanning analyzer diagnostics, suppressors, and code fixes)
  in `src/tests/Analyzers.IntegrationTests/`.
- Keep **SDK integration harness tests** in `src/tests/BuildSdk.IntegrationTests/`.
- Keep **skill rule tests** in `src/tests/SkillsTesting.UnitTests/`, against fixture skills folders.
- Keep tests that assert something about **this repository** — its own skills, its own arrangement —
  in `src/tests/BuildSdk.IntegrationTests/`, not in the rule-engine unit test projects. A rule
  engine's tests must stay independent of what this repository happens to contain today.

Namespace expectations:

- `Analyzers.UnitTests` sources use `Purview.BuildSdk.Analyzers`
- `Analyzers.IntegrationTests` sources use
  `Purview.BuildSdk.Analyzers`
- `BuildSdk.IntegrationTests` sources use `Purview.BuildSdk`
- `SkillsTesting.UnitTests` sources use `Purview.Skills.Testing.UnitTests`

Do not mix analyzer unit/integration tests in the same project unless explicitly requested.

## Canonical commands

Prefer `just` tasks:

- `just restore`
- `just build`
- `just test`
- `just lint-check`
- `just lint-fix`
- `just pack`
- `just skills-validate` — the pack-time check over the authored skills
- `just skills-add <source>` — install a skill from another repository into `.agents/skills/`
- `just version-sync` — rewrite the `Purview.BuildSdk` pins in the docs and `templates/global.json`
  after bumping `package.json`

Nothing packed carries a hand-maintained version. The `.editorconfig` marker and
`Props/PackedVersion.props` (the `global.json` fallback when the SDK is not in a versioned package
folder) are stamped at pack time from `$(PackageVersion)`. Only the copy-paste pins in the docs live in
git, and `PackagedVersionFallbackTests` names each stale one as `file:line`.

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

For authoring conventions, filtering syntax and troubleshooting, see the
[`purview-buildsdk-testing` skill](./skills/purview-buildsdk-testing/SKILL.md) and its
[`treenode-filter` reference](./skills/purview-buildsdk-testing/references/treenode-filter.md).

### Integration harness for complex validation

Use `src/tests/BuildSdk.IntegrationTests/Harness/ProjectHarness.cs` when validating behaviour that depends on MSBuild evaluation, import order, or generated project state.

- Create throwaway projects with `ProjectHarness.For(...).BuildAsync()` (or `CreateAsync`/`CreateWithContentAsync`).
- Prefer harness evaluation helpers over brittle log parsing:
  - `GetPropertyAsync` / `GetPropertiesAsync`
  - `GetItemIdentitiesAsync` / `GetProjectReferencesAsync`
  - `GetPreprocessProjectAsync` for evaluated project inspection
- Use `BuildAsync(restore: true)` when package restore/build behaviour is part of the scenario.
- Spawn processes through `Infra/ProcessRunner` (or `ProjectHarness.RunProcessAsync`), never `new Process`.
  It strips the CI runner's environment — `GITHUB_WORKSPACE` moves the SDK's idea of the repository root,
  and `CI`/`GITHUB_ACTIONS`/`TF_BUILD` change what is suppressed — so a test cannot pass locally and fail
  on `ubuntu-latest`. Use `RunWithEnvironmentAsync` only to opt one variable back in deliberately.
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

- Versioning is driven by `package.json`.
- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat`, `fix`, `docs`, `test`,
  `refactor`, `chore`, `build`, `ci`. Prefer small, reviewable commits, and do not mix unrelated
  changes in one.
- State the validation commands you ran before declaring a change done.
- Git hooks are configured in `.config/lefthook.yml`. Keep fast checks in `pre-commit` and slower
  ones in `pre-push`, keep the commands cross-platform, and never make a commit depend on the
  network.

## Practical guardrails for agents

- Validate changes with targeted tests first, then broader suite as needed.
- When touching test behaviour, verify with MTP/TUnit-compatible invocation.
- Prefer linking users to existing docs over duplicating long explanations in chat.
- Keep guidance about working in this repository in this file; keep `.github/` copies thin and referential.
