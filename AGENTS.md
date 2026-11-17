# AGENTS.md

Guidance for AI coding agents working in this repository.

## Customization source of truth

Use `.agents/` as the default location for **generic agentic information** shared across agent runtimes.

- `.agents/agents/` — canonical, cross-agent agent workflow specs (authored, tracked)
- `.agents/prompts/` — canonical, cross-agent prompt specs (authored, tracked)
- `.agents/skills/` — skills installed from *other* sources by the skills CLI (gitignored), plus any **repository-local** skill authored in place and tracked; see below.

Use `.github/` customization files only as VS Code/Copilot wrappers or registration points when required by tooling.

## Two skills folders, two purposes

This distinction is the one most easily got wrong, and getting it wrong produces two copies that nothing keeps in step.

| Folder | Holds | Written by | Tracked |
| -- | -- | -- | -- |
| `skills/<name>/` | the skills this repository **authors and publishes** | by hand | yes |
| `skills-lock.json` | the record of skills installed **from other sources** | the skills CLI | yes |
| `.agents/skills/<name>/` | skills installed from other sources (the CLI's "universal" location) | the CLI | no |
| `.agents/skills/<name>/` | a **repository-local** skill — guidance for working in *this* repository, never published | by hand | yes |
| `.claude/skills/`, `agent/skills/` | per-agent links into the universal location | the CLI | no |

### `skills/` is published, not installed

Every skill in `skills/` describes a product this repository ships — `Purview.BuildSdk` or `Purview.Skills.Testing` — and exists so **another** repository can install it:

```bash
npx skills add purview-dev/build-sdk
```

They are not installed into this repository's own agent directories. An agent working here reads them at their authored path, which is what AGENTS.md links to, and what the skills CLI clones. Installing them into `.agents/skills/` as well would put this repository's own published advice into the folder reserved for other people's, and leave two copies to drift apart. `RepositorySkillsSelfTests.AuthoredSkillsAreNotInstalledIntoTheAgentDirectories` enforces that.

Naming follows from the same fact. Skills install into one flat directory per agent, shared with every skill a developer has installed from anywhere, so **every skill here carries the `purview-buildsdk-` prefix**. `sdk-*`, `project-*`, `test-*` and `build-*` are far too common to use unprefixed, and the loser of a collision is whichever installed second. `RepositorySkillTests.EverySkill_IsNamedForThisProduct` enforces that.

Guidance about working *in this repository* belongs in this file, or in a **repository-local** skill under `.agents/skills/` (which is not published) — a skill under `skills/` ships to someone else.

### `.agents/skills/` — installed skills, plus the one local skill

To add an installed skill:

```bash
just skills-add purview-dev/engineering-playbook      # append '#<ref>' to pin it
```

Everything the CLI writes is gitignored, because it is all reproducible from the committed `skills-lock.json` with `npx skills experimental_install`. Only `.agents/skills/*` is ignored by name, not the whole of `.agents/`, because `.agents/agents/` and `.agents/prompts/` are authored content.

One entry under `.agents/skills/` is **tracked**: `purview-buildsdk-settings-docs-sync`, a repository-local skill. It is guidance about working *in this repository*, so it must not ship to a consumer and cannot live under `skills/`, and `.agents/skills/` is the one location an agent reads natively here. `.gitignore` un-ignores it by name; `RepositorySkillsSelfTests.InstalledSkillsAreNotTracked` still fails if any skill the CLI *installed* — anything recorded in `skills-lock.json` — is tracked.

**Skills have exactly one installer, and it is the skills CLI.** The SDK writes nothing into an agent directory at all — the `.agents` mirror that used to copy a package's `agents/` and `prompts/` has been removed outright. The SDK packs no skills into a package and reads none from one: skills live in a repository's root `skills/` and are installed from git by the standard CLI.

The single-writer rule is why. The CLI's universal install location is that same folder, so a second writer would mean the mirror silently overwriting a pinned install on the next build. See "The agent-folder mirror was removed" below for what went with it.

Link to a published skill by its **authored** path (`./skills/<name>/SKILL.md`). A link into `.agents/skills/` points at a CLI-installed, gitignored file and is broken in a fresh clone; `RepositorySkillsSelfTests` fails the build if one reappears. The repository-local skill needs no link here — the agent runtime finds it.

## Purpose

This repo builds and tests `Purview.BuildSdk`, a reusable MSBuild SDK package plus analyzer and tests.

For full product behaviour and configuration, read [`README.md`](README.md). Keep edits minimal, targeted, and convention-driven.

**Prefer a skill to a prompt, and a skill to a shipped sub-agent.** This SDK used to ship two prompts and two sub-agent definitions under `Sdk/.agents/`. All four are gone: `sdk-review-repository-shape` and `sdk-repository-rationaliser` became `purview-buildsdk-repository-review`, `sdk-consumer-setup` became `purview-buildsdk-adoption`, and `sdk-diagnose-agent-folder-copy` described a feature that no longer exists. A prompt or a mirrored agent file is inert — it lands somewhere no agent reads natively, so somebody has to know it exists. A skill carries a `description` that an agent matches against the task, so it fires when it is relevant. Anything task-shaped and reusable belongs in `skills/`.

**Skills are authored at the repository root in `skills/<name>/SKILL.md`** (see above), and published only through the skills CLI (`npx skills`), which clones the *git* repository and only discovers a root `skills/` folder. No package carries them.

Add a new skill under `skills/` and name it `purview-buildsdk-<topic>`. Nothing packs them into a NuGet package — `ValidatePurviewSkills` runs before `Pack` so a malformed skill fails here, and the CLI delivers them from git.

The authored set, and what each one answers:

| Skill | Answers |
| -- | -- |
| [`purview-buildsdk-adoption`](skills/purview-buildsdk-adoption/SKILL.md) | wiring the SDK into a repository, required settings and versions, and why a property had no effect |
| [`purview-buildsdk-configuration`](skills/purview-buildsdk-configuration/SKILL.md) | which property controls this, where must it be set, how to set the target framework(s), and which SourceLink provider matches the repository host |
| [`purview-buildsdk-project-layout`](skills/purview-buildsdk-project-layout/SKILL.md) | where does a project go, what is it called, and which target framework(s) it uses |
| [`purview-buildsdk-project-detection`](skills/purview-buildsdk-project-detection/SKILL.md) | why did the SDK do that automatically, target framework included |
| [`purview-buildsdk-testing`](skills/purview-buildsdk-testing/SKILL.md) | writing TUnit tests, and running them under MTP |
| [`purview-buildsdk-skill-tests`](skills/purview-buildsdk-skill-tests/SKILL.md) | authoring a skill, and validating it with `Purview.Skills.Testing` |
| [`purview-buildsdk-skill-installation`](skills/purview-buildsdk-skill-installation/SKILL.md) | installing, updating and tidying skills with the `skills-*` recipes, `.config/purview-build-sdk.json`, and the `PRSKL*` diagnostics |
| [`purview-buildsdk-repository-review`](skills/purview-buildsdk-repository-review/SKILL.md) | auditing a repository against the SDK's conventions |
| [`purview-buildsdk-justfile`](skills/purview-buildsdk-justfile/SKILL.md) | the root `Justfile` and the shared `.build/just/` modules: .NET, version and `bump`, pipeline, the skill recipes and `skills.mts`, utilities, docs site, and the exported environment |
| [`purview-buildsdk-msbuild-properties`](skills/purview-buildsdk-msbuild-properties/SKILL.md) | adding, renaming, deprecating or removing an MSBuild property, its `.buildschema.json` sidecar, and `ValidatePurviewPropertySchema` (`PRPSC*`) |
| [`purview-buildsdk-docs-sync`](./skills/purview-buildsdk-docs-sync/SKILL.md) | keeping the skills, agent instructions, READMEs and `docs/wiki/` true to the code, and the wiki hierarchical |

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

It was removed rather than defaulted off because it duplicated the skills CLI, and duplication here was not neutral: the CLI's universal install location is `.agents/skills/`, so the two were two writers on one set of paths. The mirror would silently overwrite a pinned install on the next build. Once skills are the only thing worth delivering, and the CLI delivers them, the mirror had nothing left to carry.

`SyncPurviewRepositoryFiles` (inline task in `Sdk/Sdk.targets`) survives, doing only what it still needs to: the `.editorconfig`/`global.json` bootstraps (`PurviewRepoBootstrapMode`: `IfMissing`/`Always`/`WarnOnDrift`/`Never`). Every project in a solution writes those same destinations, so an existing file is never overwritten unless asked — which turns a lost race between parallel projects into a no-op — each write is staged into a temporary file beside the destination and renamed into place, retries are quiet, and only a failure surviving every retry is reported. `PurviewSuppressCopyRetryWarnings` (default `true`) demotes the built-in copy task's `MSB3026` notice to a message.

Any edit, addition, or deletion in the repository-root `skills/` folder changes the contents delivered to every repository that consumes this SDK.

Tests live in `src/tests/BuildSdk.IntegrationTests/RepositoryFileSyncTests.cs` (bootstrap writes and the style policy that reads them).
## Explicit skill installation

Installing skills into the agent-specific directories is a separate, **explicit** step — it must never be a side effect of a restore, a build, a design-time build or a test run.

It is **not MSBuild**. The `skills-*` recipes in the `purview-buildsdk-justfile` skill's `skills.just` run `skills.mts` beside it, a thin wrapper over the standard skills CLI (`bun x skills` by default), with nothing bespoke: no package-to-skill mapping, no metadata in packages, no competing lockfile. A repository publishes skills in its root `skills/`; the CLI installs them and records each in `skills-lock.json`.

```bash
just skills-add owner/repo     # skills add "owner/repo" --skill "*"
just skills-install            # skills experimental_install
just skills-update             # skills add "<source>" --skill "*", once per lockfile source; then fix
just skills-check              # only the selected agents' directories exist
just skills-fix                # remove the CLI's links from any other agent directory
```

The MSBuild targets that used to do this — `InstallPurviewSkills`, `UpdatePurviewSkills`, `CheckPurviewSkills`, `FixPurviewSkills`, with the solution-leader election, `DetectPurviewSkillsCli`, `ReadPurviewSkillsLockSources`, `EnforcePurviewSkillDirectories`, the `PurviewSkillsRunnerLauncher`/`PurviewSkillsAgentDirectory` items and the `PurviewSkillsAgents`/`Runner`/`RunnerCandidates`/`Package`/`Command`/`Source`/`LockFile`/`DryRun` properties — were **removed**, not wrapped. Each run evaluated every project in the solution (about 5 s here, against 0.4 s for the script), and failed outright whenever one project did not evaluate — exactly when a repository may need new skills to fix its build. `PRSKL0003` and `PRSKL0009` went with them. Do not reintroduce an MSBuild skill target.

### Three files, one source

`skills.mts` is the source. `skills.mjs` is **generated** from it by `scripts/sync-skills-script.mjs`, which uses Node's own `module.stripTypeScriptTypes` (Node 22.13+) and then removes the characters it blanked, so the output is the source minus its types with every comment kept. Edit `skills/purview-buildsdk-justfile/assets/just/skills.mts`, then `just skills-script-sync` regenerates `skills.mjs` and copies both into `.build/just/`. `SkillsScriptTests.GeneratedJavaScript_MatchesTheTypeScriptSource` fails while they differ (and skips on a Node without the stripper); `JustModuleParityTests` fails while `.build/just/` differs from the skill. Keep `skills.mts` to erasable TypeScript — no `enum`, `namespace` or parameter properties — and keep explicit generics off calls (`const m: Map<K, V> = new Map()`), which the generator's clean-up assumes.

The recipes pick the runtime in a small `sh` function: Bun with `skills.mts`, then Deno with `skills.mts`, then Node with `skills.mjs`; `PURVIEW_SKILLS_RUNTIME` overrides. `just`'s `which()` is deliberately not used — it needs an unstable setting.

### Settings

`.config/purview-build-sdk.json`, section `Skills`: `Agents`, `Runner`, `RunnerCandidates`, `Package`, `Command`, `LockFile`, `DryRun`, `AgentDirectories`. Highest first: a command-line option (`--agents`, `--runner`, `--package`, `--command`, `--lock-file`, `--dry-run`), a `PURVIEW_SKILLS_*` environment variable, the file, the default. Invalid JSON or a wrong type is `PRSKL0018`; an unknown key is `PRSKL0019` (warning). The PascalCase keys match `.config/purview-build.json`.

`Agents` is **empty by default**, which passes no `--agent` at all, so the CLI auto-detects agents. Set, it becomes one `--agent` per name — the CLI's `--agent` does not split a comma-separated value. A restore (`experimental_install`) is passed no agent flag.

### Package runners and the repository root

**Any package runner, defaulting to bun.** The `launchers` table in `skills.mts` carries one version-pinned launcher per runner (`bun x`, `npx -y`, `bunx`, `pnpm dlx`, `yarn dlx` (Yarn 2+), `deno run -A npm:`), each verified against the real tool. A runner with no entry falls through to `<runner> <package>`. `PATH` is probed directly for `skills`, then `Runner`, then `RunnerCandidates`, honouring `PATHEXT` on Windows — no version-check process, nothing to hang on. The runner actually found wins. Nothing found is `PRSKL0004`; `Command` overrides the launcher and skips detection. A dry run with no runner prints the configured runner's launcher. Every command is printed (`Agent skills: <command>`) before it runs, which is what the tests assert on.

**Always the repository root.** The recipes pass `--root` as the directory holding the root `Justfile`; run by hand, the script uses the nearest ancestor holding `.git`, else the current directory. Every CLI command runs with that as its working directory.

### Update, check and fix

`update` re-adds each unique lockfile source once, at its latest with `--skill "*"`, and **that is the whole update — `skills update` is never run**. `skills add` reinstalls every skill it selects (verified in the CLI source, skills 1.7.6: `installSkillForAgent` cleans each skill's folder and copies it again), so one re-add both refreshes the installed skills and adds any the source has gained. The lockfile's recorded names are deliberately not reused, which would leave out anything new. Every source is attempted and each failure named (`PRSKL0008`); `--dry-run` prints and runs nothing.

`check`/`fix` do nothing unless agents are selected. Then the only agent skill directories allowed are those agents', per the `defaultAgentDirectories` table in `skills.mts` (copied from the CLI's agent table — extend it, or let a repository add to it through `AgentDirectories`), plus `.agents/skills`, the canonical store. The authored `skills/` folder is protected even though openclaw installs there. Fix deletes links only (never their targets) and folders left empty; a real file is kept and reported as `PRSKL0015`. The CLI writes copies, not links, for some agent selections, so those are kept too.

Tests live in `src/tests/BuildSdk.IntegrationTests/SkillsScriptTests.cs`. Each behaviour test runs once per runtime (Node with `skills.mjs`, Bun and Deno with `skills.mts`) and skips a runtime that is not on `PATH`. `--command echo` stands in for the CLI: never let a test reach `bun x` or `npx`, which download from the network. An empty `PATH` stands in for a machine with no runner.

### Validating skills

`ValidatePurviewSkills` runs `BeforeTargets="Pack"` and checks that every authored skill is well formed, so a malformed skill fails in the repository that owns it rather than reaching the CLI, which would silently skip it. It validates the folder under the root `ResolvePurviewSkillsRepositoryRoot` resolved — the same one `SkillRepositoryRoot` resolves in the tests — rather than the evaluation-time `PurviewSkillsSourceFolder`, because that guess only knows `RepoRoot` and an `AGENTS.md` walk-up: on a CI runner `RepoRoot` comes from `GITHUB_WORKSPACE`, so reading it there validated a different repository's skills and passed silently. It checks:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description` (a `>-` block scalar counts),
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads the folder, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`, one per problem. Every authored skill is checked: nothing is packed any more, so there is no per-project subset to narrow to. Prose quality and whether an agent obeys a skill are deliberately **not** checked — a build can only check that a skill is well formed.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillValidationTests.cs`.

## The property schema

`src/src/BuildSdk/Sdk/Sdk.props.buildschema.json` is the one canonical description of every SDK
property: description, type and default. It is an MSBuild Editor sidecar — the editor reads
`<imported file>.buildschema.json`, so it must keep that exact name beside `Sdk.props`, and it is packed
to `Sdk/Sdk.props.buildschema.json` by the ordinary `Sdk/**` item (`PackedSdk_PropertySchema_IsPackedBesideSdkProps`).
Any file newly packed under `Sdk/` — a sidecar, a `Targets/*.targets` — also needs an entry in
`.config/purview-build.json` `PackValidation.RequiredContent`, or the pipeline's pack validation fails
with `undeclared content`.

`PropertySchemaTests` holds everything else to it, from a static read of the shipped XML plus one
evaluated reference project: every public property defined has an entry and every entry is defined or
read; every `CompilerVisibleProperty` has an entry and every `build_property.*` an analyzer reads is
exported; every `defaultValue` equals the evaluated default; a description claims "set it before the
`Sdk.props` import" exactly when `Sdk.props` (or a file it imports) reads the property; the README and
`docs/wiki/Configuration-Reference.md` tables list every entry with the same default; and the file
validates against the meta-schema vendored in `src/tests/BuildSdk.IntegrationTests/Schemas/` (offline,
by `DraftFourValidator`, which fails if the meta-schema uses a keyword it does not implement).

The deliberate exceptions — properties MSBuild owns, unprefixed internals, machine-dependent defaults —
live in `PropertySchema/SdkPropertyClassification.cs`, each with its reason. Prefer a `_` prefix for a new
internal property over adding to a list. The repository-local `purview-buildsdk-settings-docs-sync`
skill has the step-by-step sweep.

`Targets/PropertySchema.targets` ships the reusable half of that check as `ValidatePurviewPropertySchema`
(`PRPSC0001`–`PRPSC0004`), opt-in through `EnablePurviewPropertySchemaValidation` for packages that author
their own sidecars. It never runs in an ordinary build. `PropertySchemaValidationTargetTests` runs it
against this SDK's own `Sdk/` folder, so the shipped check and the test suite cannot disagree.

`Targets/TargetFrameworkOwnership.targets` is imported through the .NET SDK's `BeforeMicrosoftNETSdkTargets`
hook — after the project body, before `IsCrossTargetingBuild` is decided — so an explicit
`TargetFramework`/`TargetFrameworks` set anywhere other than `Directory.Build.targets` wins over a curated
set. It never fires in an inner build (`TargetFramework` global), which `Sdk.props` detects by trying to
overwrite the value.

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

## Repository map

- `src/src/SkillsTesting/` — packable agent-skill rule engine (`Purview.Skills.Testing`)
- `src/src/BuildSdk/` — packable MSBuild SDK package (`Purview.BuildSdk`)
- `src/src/Analyzers/` — Roslyn analyzer/source-generator assembly
- `src/src/CodeFixers/` — Roslyn code-fix assembly (`Purview.BuildSdk.CodeFixers`)
- `src/tests/Analyzers.UnitTests/` — analyzer-focused unit tests
- `src/tests/Analyzers.IntegrationTests/` — analyzer integration tests (Roslyn end-to-end analyzer/suppressor/code-fix behavior)
- `src/tests/SkillsTesting.UnitTests/` — skill rule tests, against fixture skills folders
- `src/tests/BuildSdk.IntegrationTests/` — integration harness validating SDK behaviour
- `docs/wiki/` — the wiki purview.dev publishes: three root pages, everything else in one topic folder (see below)
- `skills/` — the agent skills this repository authors and publishes
- `.build/just/` — the `just` modules the root `Justfile` imports, copied from the `purview-buildsdk-justfile` skill
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
- `just skills-update` — re-add every `skills-lock.json` source, then `skills-fix`
- `just skills-check` / `just skills-fix` — enforce the `Skills.Agents` layout from `.config/purview-build-sdk.json`
- `just skills-script-sync` / `just skills-script-check` — regenerate or check `skills.mjs` from `skills.mts`
- `just skills-install` — restore `skills-lock.json` (`skills experimental_install`)
- `just version` — the version in `package.json`
- `just bump major|minor|patch` — bump it, resetting the parts after the one bumped, then `version-sync`
- `just version-sync` — rewrite the `Purview.BuildSdk` pins in the docs, `templates/global.json` and
  `.editorconfig` marker after bumping `package.json`
- `just version-sync-check` — the same check without writing; what the pre-commit hook runs
- `just properties-check` — every SDK property against `Sdk/Sdk.props.buildschema.json`
- `just pipeline-pr` / `just pipeline-pack-validate` / `just pipeline-local-release` — the Purview.Build
  pipeline locally
- `just scrub` — stop the build servers, delete `bin/`/`obj/`/`artifacts/`, restore

### How the Justfile is arranged

The root `Justfile` imports one barrel, `.build/just/index.just`, and holds only what is specific to
this repository: `solution_file`, `version-sync`, `properties-check`, `test-linux`, `skills-validate`,
`skills-script-sync`/`skills-script-check`, and the `_after-bump` hook that runs `version-sync`. Everything else comes from the modules in
`.build/just/`.

**Those modules are the published `purview-buildsdk-justfile` skill's `assets/just/`, copied verbatim.**
Change a module in `skills/purview-buildsdk-justfile/assets/just/` and copy it to `.build/just/`;
`JustModuleParityTests` fails while the two differ, and fails if `.build/just/` holds a file the
skill does not ship. Editing only the `.build/just/` copy would leave consumers with the old recipe,
and the next copy would overwrite the edit. `docs.just` is deliberately absent here: the barrel
imports it with `import?`, and this repository has no pm-docs-site.

The root sets `allow-duplicate-variables` and `allow-duplicate-recipes` so that its definitions
override a module's defaults. The flip side is that a recipe left in the root with a module's name
silently replaces the module's, so do not copy one back.

Nothing packed carries a hand-maintained version. `Props/PackedVersion.props` (the `global.json`
fallback when the SDK is not in a versioned package folder) is stamped at pack time from
`$(PackageVersion)`. Only the copy-paste pins live in git — the quick-start snippets, the starter
template, and this repository's own `.editorconfig` marker, so that reading the tracked file never
shows a release-old number. `PackagedVersionFallbackTests` names each stale one as `file:line`, and
`just version-sync-check` fails the commit before it can land.

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
[`purview-buildsdk-testing` skill](skills/purview-buildsdk-testing/SKILL.md) and its
[`treenode-filter` reference](skills/purview-buildsdk-testing/references/treenode-filter.md).

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
- `src/tests/BuildSdk.IntegrationTests/Infra/TestHelpers.cs`

## Conventions to preserve

- Keep project naming aligned with repo conventions in [`README.md`](README.md#project-naming-guide).
- Respect Central Package Management in `Directory.Packages.props`.
- Avoid unrelated refactors or formatting-only churn unless requested.
- Follow existing style and keep changes small and testable.

## Documentation

The written surfaces are the authored skills, this file, the READMEs and `docs/wiki/`. They are
kept true to the **code**, never to each other, by the published
[`purview-buildsdk-docs-sync`](./skills/purview-buildsdk-docs-sync/SKILL.md) skill. A settings change
also follows the repository-local `purview-buildsdk-settings-docs-sync` sweep.

- **READMEs.** `README.md` is the repository README *and* the `Purview.BuildSdk` package README:
  `BuildSdk.csproj` packs it. `src/src/SkillsTesting/README.md` is the `Purview.Skills.Testing`
  package README, because a packable project's own `README.md` wins over the repository's.
- **The wiki is hierarchical.** purview.dev publishes `docs/wiki/` and reads `_Sidebar.md` for page
  order, so a page's path is its URL.
  - Three pages stay at the root because the site depends on them: `Getting-Started.md` is its
    `docs.rootPage`, and `Configuration-Reference.md` and `Repository-Bootstrap.md` are its `docsPage`
    deep links.
  - Every other page lives in exactly one topic folder: `concepts/`, `configuration/`, `testing/`,
    `analyzers/`, `packaging/`, `tooling/` or `development/`.
  - Every page is listed once in `_Sidebar.md` and in its section's table in `Home.md`, under a
    `## ` heading that matches the sidebar label.
- **`DocumentationWikiTests` enforces all of that**, plus link resolution and one `# ` title per page,
  and checks every `README.md`/`AGENTS.md` link into the wiki.
- **Moving a page changes its published URL.** Give the user the old and new slugs for the site's
  `docs.redirects` (in `purview-dev/purview-dev`, `src/src/data/projects/build-sdk.yml`).
- `PropertySchemaTests` reads property tables from every wiki page, in any folder.

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
