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
| [`purview-buildsdk-skill-installation`](skills/purview-buildsdk-skill-installation/SKILL.md) | installing the skills a package ships, and the `PRSKL*` diagnostics |
| [`purview-buildsdk-repository-review`](skills/purview-buildsdk-repository-review/SKILL.md) | auditing a repository against the SDK's conventions |
| [`purview-buildsdk-justfile`](skills/purview-buildsdk-justfile/SKILL.md) | the root `Justfile` and the shared `.build/just/` modules: .NET, version and `bump`, pipeline, skill targets, utilities, docs site, and the exported environment |
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

The skill targets are a **thin wrapper over the standard skills CLI** (`bun x skills` by default), with nothing bespoke: no package-to-skill mapping, no metadata in packages, no competing lockfile. A repository publishes skills in its root `skills/`; the CLI installs them and records each in `skills-lock.json`.

```bash
dotnet msbuild <solution> -t:InstallPurviewSkills -p:PurviewSkillsSource=owner/repo   # skills add "owner/repo" --skill "*"
dotnet msbuild <solution> -t:InstallPurviewSkills                                    # skills experimental_install
dotnet msbuild <solution> -t:UpdatePurviewSkills    # skills add "<source>" --skill "*", once per lockfile source
dotnet msbuild <solution> -t:CheckPurviewSkills     # only the PurviewSkillsAgents directories exist
dotnet msbuild <solution> -t:FixPurviewSkills       # remove the CLI's links from any other agent directory
```

The caller owns the ref: append `#<ref>` to a source to pin it. With no source and no lockfile, install is `PRSKL0007`. This used to be package-driven — `ResolvePurviewSkillSources` read a `.agents/skills/` folder inside each restored package plus its nuspec commit, and `ListPurviewSkills`/`RecommendPurviewSkills`/`VerifyPurviewSkills` reported missing or drifted skills (`PRSKL0001`/`0002`/`0010`–`0014`/`0017`). All of that was removed: it was bespoke metadata that no package built by this SDK carried, so it never found anything.

The CLI owns the agent-specific destinations and their discovery conventions — which differ per agent and change independently of this SDK — and owns `skills-lock.json`, which is why installation is delegated rather than reimplemented.

`PurviewSkillsAgents` is **empty by default**, which passes no `--agent` at all, so the CLI auto-detects agents. Set, it becomes one `--agent` per name (`_ResolvePurviewSkillsAgents` splits on `,`, `;` and spaces) — the CLI's `--agent` does not split a comma-separated value, so the old `--agent "claude-code,codex"` form named one agent that does not exist. `dotnet msbuild -p:` itself splits on `,` and `;`, so a command line needs `%2C` or a quoted value; tests use `%2C`. A restore (`experimental_install`) is passed no agent flag.
### Package runners and the repository root

Two invariants govern how the CLI is launched, and both are load-bearing.

**Any package runner, defaulting to bun.** The CLI is published to npm and each runner spells "run this npm package" differently. `PurviewSkillsRunner` (default `bun`, i.e. `bun x skills@1`) selects one; the mapping lives in the `PurviewSkillsRunnerLauncher` item group in `Sdk/Sdk.targets`, which carries one version-pinned `Exec` launcher per runner. A runner with no entry falls through to `<runner> $(PurviewSkillsPackage)`, so an unknown runner still works. Each invocation form was verified against the real tool — `npx -y`, `bunx`, `bun x`, `pnpm dlx`, `yarn dlx` (Yarn 2+ only), `deno run -A npm:` — because a wrong form is a command that simply does not run.

`DetectPurviewSkillsCli` (inline task) probes `PATH` for `skills` first, then `$(PurviewSkillsRunner)`, then `$(PurviewSkillsRunnerCandidates)`, honouring `PATHEXT` on Windows where these are `.cmd` shims. `PATH` is read directly rather than by spawning a version check: deterministic, no process, cannot hang on a cold download. The runner actually found wins over the configured preference. Nothing found is `PRSKL0004`; `PurviewSkillsCommand` overrides the launcher entirely and skips detection.

**Always the repository root.** `skills-lock.json` and the agent directories belong to the repository, not to a project, so `ResolvePurviewSkillsRepositoryRoot` resolves one root and every skill target runs from it: `PurviewSkillsInstallRoot` → a user-set `PurviewSkillsRepoRoot` → the marker walk → `PurviewSkillsRepoRoot`/`RepoRoot` as guessed → CI workspace variables → SourceLink's git `SourceRoot`. There is deliberately **no project-directory fallback** — that would scatter a lockfile and an agent directory through the tree, one per project. An unresolved root is `PRSKL0003` (naming `PurviewSkillsInstallRoot`).

The marker walk is `FindPurviewRepositoryRoot` (inline task), driven by `PurviewRepositoryRootMarkers` — `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json`. Three things about it are load-bearing and easy to break:

- **It cannot be an MSBuild intrinsic.** `GetDirectoryNameOfFileAbove` matches *files only* and returns nothing for `.git/`, `.github/`, `.agents/` or `.claude/` — four of the nine markers. Verified, not assumed.
- **Two passes.** The first marker in the list is definitive and is searched all the way up (`.git/` *is* the repository root, so a monorepo sub-package does not win). Every other marker uses nearest-ancestor-wins, with list order breaking ties inside one directory. Searching them all to the top would let a stray `.claude/` in a developer's home directory outrank the real root.
- **The home directory and everything above it are never candidates.** Without that bound, a project outside any repository resolves to `~` and installs into it. A repository below the home directory is unaffected.

The marker walk outranks the evaluation-time `PurviewSkillsRepoRoot` guess (which only knows `RepoRoot` and an `AGENTS.md` walk-up), because an `AGENTS.md` can legitimately sit in a subdirectory. `_PurviewSkillsRepoRootWasSpecified` is captured in `Sdk.props` before defaulting so a value the *user* set still wins.

A dry run (`PurviewSkillsDryRun=true`) with no runner on `PATH` prints the configured runner's launcher rather than failing.

Note that a target's `Condition` is evaluated *before* its `DependsOnTargets` run, so a property a dependency computes is still empty there. That is why the unresolved-root check (`PRSKL0003`) and the `PRSKL0009` guard are `Error`s inside dependency targets rather than `Condition`s on the public targets.

### Validating skills

`ValidatePurviewSkills` runs `BeforeTargets="Pack"` and checks that every authored skill is well formed, so a malformed skill fails in the repository that owns it rather than reaching the CLI, which would silently skip it. It validates the folder under the root `ResolvePurviewSkillsRepositoryRoot` resolved — the same one every skill target uses — rather than the evaluation-time `PurviewSkillsSourceFolder`, because that guess only knows `RepoRoot` and an `AGENTS.md` walk-up: on a CI runner `RepoRoot` comes from `GITHUB_WORKSPACE`, so reading it there validated a different repository's skills and passed silently. It checks:

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

### Nothing about skills runs during a build

There used to be a `RecommendPurviewSkillsOnBuild` hook on `AfterTargets="Build"`. It was removed after
measurement — on a 39-project solution it read and regex-scanned every project's `project.assets.json`
on every build to print advice — and the recommendation targets went later with the package-to-skill
mapping. What remains is the explicit targets in "Explicit skill installation" above.

### Once per solution: the leader

Pointed at a solution, MSBuild runs a custom target in **every** project, so each skill target used
to re-run the CLI once per project. Every public skill target is now a thin wrapper:
`_ResolvePurviewSkillsProjects` reads the solution's project list from
`CurrentSolutionConfigurationContents` (the same value in every project, for `.sln` and `.slnx`;
outside a solution it is the project itself), asks each project whether it imports the SDK through
`_GetPurviewSkillsProjectInfo`, and elects as leader the first, by sorted path, that answered. The
wrapper then calls `_<Target>Core` on the leader through the `MSBuild` task. MSBuild runs one
project's target once per set of global properties and returns the cached result to every later
caller, so the work runs once — no lock and no stamp file.

Three things are load-bearing:

- **The leader is a consumer project, not a `.proj` shipped in the package.** The skill settings live
  in the consumer's `Directory.Build.props`, which a project in the NuGet cache would never import.
- **Every wrapper passes identical global properties** (`_PurviewSkillsLeaderRemoveProperties` strips
  `TargetFramework`/`RuntimeIdentifier`). Anything that varies per caller would give each caller its
  own cache entry and quietly bring back the fan-out.
- **The query's reply has its own item type** (`_PurviewSkillsProjectInfoReply`). Asking the calling
  project runs the query in the caller's own instance, so reusing the caller's item put the caller
  first in its own list, and every project elected itself.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillSolutionTests.cs`, which writes its
own `.slnx` because the harness's lists projects as `<File>` entries.

### Installing, updating, checking and fixing

**Install, update and fix run only when named — never automatically.** Two layers, both tested in `PurviewSkillTargetIsolationTests`: no SDK target hooks or depends on them (a structural scan of every `.props`/`.targets` under `Sdk/`, including `*DependsOn` properties), and `_GuardExplicitPurviewSkillsTarget` — first in each one's `DependsOnTargets` — fails with `PRSKL0009` when `BuildingProject`, `DesignTimeBuild` or `MSBuildIsRestoring` is true, which catches a consumer's own `AfterTargets="Build"` hook. A git hook or a recipe dependency that runs a target on its own is not a build and is not caught. Do not add a `BeforeTargets`/`AfterTargets` to any of them, and do not weaken the guard. `PurviewSkillTargetIsolationTests.OrdinaryBuild_DoesNotInstallSkills` pins that an ordinary restore-and-build writes no lockfile and creates no agent directory.

`InstallPurviewSkills` runs `skills add "<source>" --skill "*"` for `PurviewSkillsSource`, or `skills experimental_install` without one (`PRSKL0007` when there is no lockfile either).

`UpdatePurviewSkills` (`ReadPurviewSkillsLockSources` inline task) re-adds each unique lockfile source once, at its latest with `--skill "*"`, and **that is the whole update — `skills update` is never run**. `skills add` reinstalls every skill it selects (verified in the CLI source, skills 1.7.6: `installSkillForAgent` cleans each skill's folder and copies it again, skipping only when source and destination are the same folder), so one re-add both refreshes the installed skills and adds any the source has gained; `skills update` only re-fetches entries the lockfile already has. The lockfile's recorded names are deliberately not reused, which would leave out anything new. The re-adds use target batching (`_UpdatePurviewSkillsSource`) so each failure is attributed to its source (`PRSKL0008`, `ErrorAndContinue` so all are named); `-p:PurviewSkillsDryRun=true` prints and runs nothing. `PurviewSkillsUpdateMode` and `PRSKL0006` went with the `skills update` step.

`CheckPurviewSkills`/`FixPurviewSkills` (`EnforcePurviewSkillDirectories`) do nothing unless `PurviewSkillsAgents` names agents. Then the only agent skill directories allowed are those agents', per the `PurviewSkillsAgentDirectory` item group (copied from the CLI's agent table — extend it, don't hard-code), plus `.agents/skills`, the canonical store every agent directory links into. The authored `skills/` folder is protected even though openclaw installs there. Fix deletes links only (never their targets — `RemoveDirectory` on Windows, `unlink` on Unix) and folders left empty; a real file is kept and reported as `PRSKL0015`, because it cannot be told from hand-written content. Note the CLI writes copies, not links, for some agent selections, so those are kept too.

Tests live in `src/tests/BuildSdk.IntegrationTests/PurviewSkillInstallCommandTests.cs` (install commands, runner mapping and repository-root resolution) and `PurviewSkillUpdateTests.cs` (update, check and fix), with the lockfile writer in `SkillLockFileFixtures.cs`.

Two harness facts to know when adding tests here. `BuildingProject` is only `true` when the `Build` target actually runs, so a test that names a skill target but needs to look like a build must pass `-p:BuildingProject=true` itself — `PurviewSkillTargetIsolationTests` does exactly that to prove the `PRSKL0009` guard fires. And the throwaway solution is not a git repository and has no `AGENTS.md`, so the repository root does not resolve on its own: write an `AGENTS.md` beside the solution, or pass `-p:PurviewSkillsInstallRoot`. Never assert runner behaviour by letting a test actually invoke `npx`/`pnpm dlx` — that downloads from the network and is not a unit of this SDK; `PurviewSkillsCommand=echo` stands in for the CLI.
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
- `just skills-update` — `UpdatePurviewSkills` then `FixPurviewSkills`
- `just skills-check` / `just skills-fix` — enforce the `PurviewSkillsAgents` layout
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
and the `_after-bump` hook that runs `version-sync`. Everything else comes from the modules in
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
