# Agent Skills

[Agent Skills](https://skills.sh/) are folders of `SKILL.md` guidance that coding agents load on
demand. Purview publishes them in the standard format and installs them with the standard
[`skills` CLI](https://skills.sh/) — `npx skills add`, `npx skills update`, `npx skills remove`.

`Purview.BuildSdk` deliberately does **not** reimplement any of that. It knows one thing the CLI
cannot know: which skills are relevant to the NuGet packages a project actually references, and at
which version. So the SDK **discovers and recommends**, and the CLI **installs**.

> The SDK recommends commands. It never runs them. Nothing on this page installs, updates or removes
> a skill as a side effect of a restore, a build, a design-time build or a test run.

## The boundary

```mermaid
flowchart TD
    NuGet["Purview NuGet references<br/>(project.assets.json)"]
    Metadata["Package-to-skill metadata<br/>(.agents/skills/ + nuspec repository commit)"]
    Installed["Local skill state<br/>(skills-lock.json)"]
    SDK["Purview Build SDK"]
    Decision{"Skill status"}

    Add["Suggest: npx skills add owner/repo#commit"]
    Update["Suggest: pinned re-add<br/>(not a blind npx skills update)"]
    List["Suggest: npx skills list"]
    Restore["Suggest: npx skills experimental_install"]
    Use["Optional: npx skills use"]
    OK["No recommendation"]

    CLI["Standard skills CLI"]
    Lock["skills-lock.json"]
    Agents["Agent skill directories"]

    NuGet --> SDK
    Metadata --> SDK
    Installed --> SDK
    SDK --> Decision

    Decision -->|Missing| Add
    Decision -->|Ref drift| Update
    Decision -->|Unknown| List
    Decision -->|Lockfile not restored| Restore
    Decision -->|Compatible| OK

    Add --> CLI
    Update --> CLI
    List --> CLI
    Restore --> CLI
    Use --> CLI

    CLI --> Lock
    CLI --> Agents
```

| Component | Owns |
| -- | -- |
| Purview libraries | Their API guidance, authored as skills in their own repository |
| Skill publication | Making those skills reachable by `npx skills add` |
| Package-to-skill metadata | Which package ships which skills, and at which commit |
| Purview Build SDK | Reading package references, evaluating local state, recommending commands |
| Skills CLI | Installing, updating, removing, source tracking, agent-specific destinations |
| Consuming repository | Its installed skills and its `skills-lock.json` |

## Publication

Skills are authored at the **repository root**, in `skills/<name>/SKILL.md`, and published two ways
from that one copy:

- **To the skills CLI**, which clones the git repository and discovers a root `skills/` folder.
- **To the NuGet package**, where `PurviewPackRepositorySkills` packs them into the package's
  `.agents/skills/`, which the [agent folder](Agent-Folder.md) mirror copies into a consuming
  repository's `.agents/`.

Both routes matter: the mirrored `.agents/` folder is gitignored in every consuming repository, so
it is absent from a clone and cannot serve the CLI.

```bash
npx skills add purview-dev/build-sdk                    # every skill
npx skills add purview-dev/build-sdk --skill tunit-filtering
npx skills add purview-dev/build-sdk --list             # see what is there, install nothing
```

Skills live **alongside the library they describe**, in that library's repository, rather than in
one central skills repository. That is what makes version compatibility exact: a package and its
skills are built from the same commit, so the package itself carries the mapping (see below). A
central repository would have to re-derive that association, and would drift from the libraries it
documents.

`PurviewSkillsToPack` narrows which skills a given project packs, so a repository producing several
packages ships each skill with the library it belongs to rather than duplicating all of them
everywhere.

## Authoring

`npx skills init <name>` scaffolds a skill; move it under `skills/<name>/` and adapt the frontmatter
to Purview conventions. `ValidatePurviewSkills` then checks it at pack time, and
`IncludePurviewSkillsInPackage` depends on it — so a malformed skill fails the build of the
repository that owns it rather than shipping a skill the CLI silently skips. It checks that:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description`,
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads
  the folder name, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`. Prose quality is deliberately not checked: a build can only
check that a skill is well formed.

Keep shared engineering guidance in one skill and link to it, rather than copying it into every
library's skill.

## How the SDK maps a package to its skills

There is **no custom `skills.json` manifest**. A package that ships skills already carries both
halves of the mapping, and the SDK reads them:

| Half | Source |
| -- | -- |
| Which skills this package ships | `.agents/skills/<name>/` inside the restored package |
| Which git repository and commit built them | the nuspec's `<repository url="…" commit="…" />`, written by SourceLink |

Both are local to the restored package, so resolution is offline and triggers no restore of its own.
A package whose nuspec has no GitHub repository URL, or no commit, **cannot** be pinned; it is
reported rather than installed from a guessed default branch.

## Recommendations

Recommendations are the one part of this feature that runs during an ordinary build. They read the
restore graph and the CLI's own `skills-lock.json`, both local; they recommend commands and never
run them; and they never fail a build.

```bash
dotnet msbuild <project> -t:RecommendPurviewSkills   # explicit, always reports
```

Every skill a referenced package ships is classified:

| State | Diagnostic | Severity | Recommendation |
| -- | -- | -- | -- |
| Installed at the referenced commit | *(none)* | — | — |
| Not installed | `PRSKL0010` | Warning | `npx skills add "<owner/repo>#<commit>" --skill …` |
| Installed from a different ref | `PRSKL0011` | Warning | pinned re-add at the referenced commit |
| Compatibility unverifiable | `PRSKL0012` | Message | `npx skills list` |
| From a repository no longer referenced | `PRSKL0013` | Message | `npx skills remove "<name>"` |
| Lockfile present, nothing restored | `PRSKL0014` | Message | `npx skills experimental_install` |

Severity splits on what a developer can act on. A missing or drifted skill is one command away, so
it is a warning. An unverifiable one usually is not — a package without SourceLink metadata cannot
be pinned by anybody — so it stays a message and can never break a build that treats warnings as
errors. Warnings are suppressible in the usual way:

```xml
<PropertyGroup>
  <MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);PRSKL0010;PRSKL0011</MSBuildWarningsAsMessages>
</PropertyGroup>
```

**Unverifiable** (`PRSKL0012`) covers three distinct cases, all of which mean "there is nothing
reliable to compare against": the package cannot be pinned at all; the skill was installed without a
ref; or a skill of the same name is installed from some other repository. The last is never treated
as drift and never overwritten — somebody installed it deliberately.

**Orphan detection** (`PRSKL0013`) is **owner-scoped on purpose**. A lockfile cannot record who
asked for an entry, so only an entry under an owner the project *does* reference, but from a
repository it *no longer* references, is reported. A skill from any other owner is the developer's
own and is never mentioned. This is a heuristic, which is why it is a message and why nothing is
ever removed automatically.

### Staying quiet

| Situation | Behaviour |
| -- | -- |
| Everything installed and compatible | No output at all |
| Design-time build (`DesignTimeBuild=true`) | Skipped |
| CI build (`ContinuousIntegrationBuild`, `TF_BUILD`, `GITHUB_ACTIONS`, `CI`) | Skipped unless opted in |
| Non-default target framework in a multi-targeting build | Skipped |
| Same state as last reported | Skipped |

The last one is deduplication. A solution has many projects sharing one repository, and a rebuild
changes nothing, so a fingerprint of the reported state is kept in
`<repo root>/.purview/skill-recommendations.cache`. The same advice is printed once, and again only
when the state it describes actually changes. The explicit `-t:RecommendPurviewSkills` target
ignores the stamp and always answers.

### Configuration

| Property | Default | Description |
| -- | -- | -- |
| `EnablePurviewSkillRecommendations` | `true` | Reports skill state after an ordinary build. |
| `PurviewSkillRecommendationsInCI` | `false` | Set `true` to report on CI builds too. |
| `PurviewSkillRecommendationStampPath` | `<repo root>/.purview/skill-recommendations.cache` | Overrides the deduplication stamp. |
| `PurviewSkillsLockFile` | `<repo root>/skills-lock.json` | Overrides where the CLI's lockfile is read from. |
| `PurviewSkillsAgents` | `*` | Agents named in the recommended install command. |
| `PurviewSkillsRunner` | `npx` | Package runner used to launch the CLI. |
| `PurviewSkillsRunnerCandidates` | `npx;bunx;bun;pnpm;yarn;deno` | Probed in order after `PurviewSkillsRunner`. |
| `PurviewSkillsPackage` | `skills@1` | Package spec the executed launcher pins to. |
| `PurviewSkillsInstallRoot` | *(resolved)* | Overrides the repository root every skill target runs from. |
| `PurviewRepositoryRootMarkers` | `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json` | Ordered markers that identify a repository root; the first is definitive. |

## Version compatibility

NuGet package versions and skill versions evolve independently, so a project on an older library
must not be advised to install skills that describe a newer API.

The mechanism is a **commit pin**, and it is exact by construction. The nuspec of the *restored
package version* names the commit it was built from, and the CLI accepts `owner/repo#<ref>` and
records that ref in `skills-lock.json`. Comparing the two is the whole compatibility check:

```text
package Purview.Example 1.2.3  →  nuspec commit  0123456…
skills-lock.json: purview-alpha →  ref           aaaaaaa…   ← drift
```

This is why **`npx skills update` is not recommended on drift**. `update` installs the newest
published skill, which is correct only when the newest skill happens to describe the referenced
package version — something that cannot be known offline, and that is false for any project not on
the latest package. `PRSKL0011` therefore names the referenced package's own commit instead:

```bash
npx skills add "purview-dev/purview-example#0123456789abcdef0123456789abcdef01234567" \
  --skill "purview-alpha" --agent "*" -y
```

`npx skills update` remains the right command when the intent is to track the latest published
skills — for example right after upgrading the package reference, once the new version is restored
and the SDK reports the state as compatible again.

> Note the repeated `--skill` flag. The CLI does not split a comma-separated value: given one, it
> matches nothing and reports "No matching skills found".

## The lockfile

`skills-lock.json` is the CLI's own record of what is installed and where it came from. The SDK
reads it and introduces **no competing lockfile**. Reading it rather than parsing CLI output is
deliberate: the JSON listing (`npx skills list --json`) is machine-readable but omits the `ref`, and
the ref is the entire question. The lockfile carries it, and reading a file costs no Node tooling —
which is what keeps recommendations working on a machine that can build .NET but has no Node.

A lockfile is a record of an install, not proof of one: a fresh clone carries a committed
`skills-lock.json` and no installed skills at all. The SDK detects that case — a lockfile with no
agent skill directory anywhere at the repository root — and reports `PRSKL0014`:

```bash
npx skills experimental_install   # restore everything in skills-lock.json
```

This command is **experimental**, so it is suggested and never required, never invoked
automatically, and always offered with the supported fallback (`npx skills add`). Prefer a stable
restore command if the CLI gains one. The detection looks for any root dot-folder containing a
`skills` folder rather than hard-coding agent directories, which belong to the CLI and change
independently of this SDK; the SDK's own `.agents/` mirror is excluded.

## Installing from the SDK

Installation is **explicit**. Neither target has `BeforeTargets`/`AfterTargets`:

```bash
dotnet msbuild <project> -t:ListPurviewSkills      # diagnostics only, installs nothing
dotnet msbuild <project> -t:InstallPurviewSkills   # delegates to the skills CLI
```

`InstallPurviewSkills` shells out to the CLI rather than reimplementing it, because the CLI owns the
agent-specific destinations and their discovery conventions — which differ per agent and change
independently of this SDK — and owns `skills-lock.json`.

To install a Purview skill the project has no `PackageReference` for, name the source directly. The
caller owns the ref in that case; append `#<ref>` to pin it:

```bash
dotnet msbuild <project> -t:InstallPurviewSkills -p:PurviewSkillsSource=purview-dev/engineering-playbook
```

### Package runners

The CLI is published to npm, and every package runner spells "run this npm package" its own way.
`PurviewSkillsRunner` picks one; it defaults to **`npx`**, because npx ships with Node itself and is
therefore the one runner any machine with Node at all already has.

| `PurviewSkillsRunner` | Executed | Recommended in diagnostics |
| -- | -- | -- |
| `npx` *(default)* | `npx -y skills@1` | `npx skills …` |
| `bunx` | `bunx skills@1` | `bunx skills …` |
| `bun` | `bun x skills@1` | `bun x skills …` |
| `pnpm` | `pnpm dlx skills@1` | `pnpm dlx skills …` |
| `yarn` | `yarn dlx skills@1` | `yarn dlx skills …` |
| `deno` | `deno run -A npm:skills@1` | `deno run -A npm:skills …` |
| `skills` | `skills` | `skills …` |

A runner this SDK has never heard of still works: it falls through to `<runner> skills@1`, the form
most new runners adopt.

```xml
<PropertyGroup>
  <PurviewSkillsRunner>pnpm</PurviewSkillsRunner>
</PropertyGroup>
```

`yarn dlx` needs Yarn 2+; Yarn 1 has no `dlx`. The executed launcher pins the CLI to a major so a
breaking release cannot change what `InstallPurviewSkills` does — `PurviewSkillsPackage` (default
`skills@1`) overrides the pin. Recommendation text deliberately drops it: that is a command for a
person to run, and `npx skills add` is the form they know.

### Installing in the repository that authors the skills

A repository that *authors* skills has no `PackageReference` to itself, and pinning to a git commit
would be wrong anyway: an edit to `skills/<name>/SKILL.md` would not reach an agent until it was
pushed and reinstalled. Install from the working tree instead:

```bash
dotnet msbuild <project> -t:InstallPurviewSkills -p:PurviewSkillsSource=.
```

The CLI records that portably — `"source": "."`, `"sourceType": "local"` — so the resulting
`skills-lock.json` survives a clone and can be committed. `Purview.BuildSdk` uses exactly this for
its own skills; see `just skills-install`.

What the CLI writes should be gitignored, since all of it is reproducible from `skills/` plus the
lockfile:

| Path | Written by | Tracked |
| -- | -- | -- |
| `skills/<name>/` | authored by hand | yes |
| `skills-lock.json` | the CLI | yes |
| `.agents/skills/<name>/` | the CLI's "universal" location | no |
| `.claude/skills/`, `agent/skills/`, … | per-agent links into it | no |

> **The CLI's universal location is `.agents/skills/`.** Ignore that specifically rather than the
> whole of `.agents/`, so any authored `.agents/agents/` and `.agents/prompts/` content stays
> tracked.

### One installer

Skills have exactly one installer: **the skills CLI**. The `.agents` mirror does not copy them.

A package still ships `.agents/skills/<name>/`, but as *metadata* — it is what
`ResolvePurviewSkillSources` reads to map a package to its skills and to the commit it was built
from. It is not content to be copied into the consuming repository.

That split exists because the CLI's universal install location is `.agents/skills/` — exactly where
the mirror used to write. Two writers on the same paths meant the mirror would overwrite a pinned
install on the next build, silently undoing it, while `PRSKL0011` compared a lockfile ref against
content the mirror had already replaced.

| Source | Destination | Who writes it |
| -- | -- | -- |
| package `.agents/agents/**` | `<dest>/agents/**` | the mirror |
| package `.agents/prompts/**` | `<dest>/prompts/**` | the mirror |
| package `.agents/skills/**` | *(not copied)* | — metadata only |
| git `skills/<name>/` | `.agents/skills/`, `.claude/skills/`, … | the skills CLI |

Because the mirror no longer owns them, skills are absent from the generated
`<destination>/.gitignore`; a repository ignores its installed skills itself. `PurviewMirrorPackagedSkills=true`
restores the old copy-everything behaviour for a repository that has not moved to the CLI, so an SDK
upgrade is not a forced migration.

### Detecting the CLI

Installation probes `PATH` rather than assuming the configured runner is present, honouring
`PATHEXT` on Windows where these are `.cmd` shims. The order is:

1. `skills` — used as-is if it is already on `PATH`. No download, works offline.
2. `$(PurviewSkillsRunner)` — the configured preference.
3. `$(PurviewSkillsRunnerCandidates)` — `npx;bunx;bun;pnpm;yarn;deno` by default.

So a machine with bun but no Node still installs, rather than reporting that nothing was found. The
runner that is actually found wins over the configured preference, because it is the one that can
really run. `PATH` is read directly rather than by spawning a version check: it is deterministic,
costs no process, and cannot hang on a cold download.

- Nothing found is `PRSKL0004`, naming the fix rather than surfacing a raw `'npx' is not recognized`.
- `PurviewSkillsCommand` overrides the launcher entirely and skips detection.

`ListPurviewSkills` and `RecommendPurviewSkills` **never** error on a missing runner. Resolving,
evaluating and reporting need no Node tooling at all.

### Always from the repository root

`skills-lock.json` and the agent directories belong to the repository, not to a project, and a
solution has many projects and exactly one of each of those. So every skill target runs the CLI
**from the repository root, always**. The root is resolved by this chain:

1. `PurviewSkillsInstallRoot` — an explicit override
2. `PurviewSkillsRepoRoot` — when the *user* set it
3. the **marker walk** (below)
4. `PurviewSkillsRepoRoot` / `RepoRoot` as the SDK guessed them
5. `GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY` or `CI_PROJECT_DIR`
6. the git source root SourceLink reports

There is deliberately **no fall back to the project directory**: that would scatter a lockfile and an
agent directory through the tree, one per project, which is the thing being prevented. If the root
cannot be resolved, `InstallPurviewSkills` reports `PRSKL0003` and names `PurviewSkillsInstallRoot`
rather than guessing; recommendations simply stay silent, because a build never fails over advice.

#### The marker walk

`PurviewRepositoryRootMarkers` lists what identifies a repository root, most authoritative first:

```text
.git/ ; .github/ ; .gitattributes ; LICENSE.md ; AGENTS.md ; .agents/ ; CLAUDE.md ; .claude/ ; package.json
```

A trailing slash marks a directory — and still matches a *file* of that name, because `.git` is a
directory in a clone but a file in a worktree or submodule. MSBuild's own
`GetDirectoryNameOfFileAbove` matches files only, which is why this is a task rather than an
intrinsic: it finds nothing for `.git/`, `.github/`, `.agents/` or `.claude/`.

Resolution runs in two passes, because neither pure priority nor pure proximity is right alone:

1. **The first marker in the list is definitive** and is searched all the way up. `.git/` *is* the
   repository root by definition, so a project inside a monorepo package resolves to the repository
   rather than to the nearer folder holding a `package.json`.
2. **Otherwise the nearest ancestor** holding any marker wins, with list order breaking ties inside
   that one directory.

Pass 2 is nearest-first on purpose. Searching every remaining marker to the top would let a stray
`.claude/` in a developer's home directory outrank the `package.json` at the real repository root.
For the same reason the home directory and everything above it are **never** candidates — a
repository root is never your home directory. A repository *inside* your home directory is
unaffected, because it sits below it.

| Project at `/repo/sub/src/proj` | Resolves to |
| -- | -- |
| `/repo/.git/` + `/repo/sub/package.json` | `/repo` — the definitive marker wins from a distance |
| `/repo/.github/` + `/repo/sub/package.json` | `/repo/sub` — nearest wins once `.git/` is absent |
| `/repo/LICENSE.md` + `/repo/package.json` | `/repo`, reported as `LICENSE.md` (higher in the list) |
| no marker below the home directory | unresolved — `PRSKL0003`, never `~` |

## One-off use

To consult a skill once without installing it:

```bash
npx skills use purview-dev/build-sdk@tunit-filtering | claude
```

This is a good fit for a one-time operation. It is not a substitute for installing the skills a
project needs throughout development, and the SDK's recommendations do not suggest it in place of
`add`.

## npm distribution

`npx skills experimental_sync` discovers skills shipped inside npm packages, by reading
`node_modules`.

**Purview does not use it, and should not.** Purview libraries ship as NuGet packages; their skills
are already carried inside those packages and mirrored into `.agents/`, and are already installable
from git by `npx skills add`. Publishing a parallel npm package would mean:

- a second release pipeline to version, publish and keep in step with the NuGet one,
- a `package.json` and a `node_modules` restore in .NET repositories that otherwise need neither,
  purely to obtain documentation, and
- two sources of truth for the same skill, with no mechanism tying the npm version to the NuGet
  version — losing exactly the commit-pinned compatibility described above.

The git source the CLI already supports delivers the same skills with none of that. `experimental_sync`
remains useful to a repository that independently consumes npm packages shipping skills; it is simply
not a Purview distribution channel. Revisit only if Purview starts shipping npm packages for their
own sake.

## Supported CLI commands

Verified against the `skills` CLI v1.

| Command | Purpose | Role here |
| -- | -- | -- |
| `npx skills add <source>` | Install skills | The installation workflow; accepts `owner/repo#<ref>` |
| `npx skills list` | List installed skills | Diagnostics; `--json` is machine-readable but omits `ref` |
| `npx skills find [query]` | Search for skills | Discovery fallback where no package mapping exists |
| `npx skills update` | Update to latest | Correct only when latest matches the referenced package |
| `npx skills remove` | Remove skills | Developer-managed |
| `npx skills use <source>@<skill>` | Use without installing | One-off guidance |
| `npx skills experimental_install` | Restore from `skills-lock.json` | Optional bootstrap; experimental |
| `npx skills init [name]` | Scaffold a skill | Authoring |
| `npx skills experimental_sync` | Sync from `node_modules` | Not used by Purview — see above |

## Diagnostics

| Code | Meaning |
| -- | -- |
| `PRSKL0001` | `project.assets.json` is missing; run `dotnet restore` first |
| `PRSKL0002` | A package ships skills but carries no pinnable git identity |
| `PRSKL0003` | The repository root could not be resolved, so there is nowhere correct to install |
| `PRSKL0004` | No package runner was found on `PATH` |
| `PRSKL0005` | An authored skill is malformed |
| `PRSKL0010` | Skills a referenced package ships are not installed |
| `PRSKL0011` | Installed skills are pinned to a different ref than the referenced package |
| `PRSKL0012` | Compatibility cannot be verified |
| `PRSKL0013` | A skill is installed from a repository no longer referenced |
| `PRSKL0014` | `skills-lock.json` exists but no skills have been restored |

## See also

- [Agent Folder](Agent-Folder.md) — how `.agents/**` is mirrored into a consuming repository
- [Configuration Reference](Configuration-Reference.md)
- [Packaging](Packaging.md)
