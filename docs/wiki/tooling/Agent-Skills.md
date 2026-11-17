# Agent Skills

[Agent Skills](https://skills.sh/) are folders of `SKILL.md` guidance that coding agents load on
demand. A repository publishes them in its root `skills/` folder, and the standard
[`skills` CLI](https://skills.sh/) installs them — `bun x skills add owner/repo` — and records each
install in `skills-lock.json`. `Purview.BuildSdk` adds nothing bespoke to that: its `skills-*` recipes
are a thin wrapper that runs the CLI from the repository root and keeps the agent directories tidy.

> Nothing on this page runs as a side effect of a restore, a build, a design-time build or a test run,
> and installing skills does not involve .NET at all. Each recipe runs only when a person runs it.

## Publication

Skills are authored at the **repository root**, in `skills/<name>/SKILL.md`, and published through
the skills CLI, which clones the git repository and discovers that root `skills/` folder. That is the
only route: the SDK packs no skills into a NuGet package, and reads none from one.

```bash
bun x skills add purview-dev/build-sdk                    # every skill
bun x skills add purview-dev/build-sdk --skill purview-buildsdk-testing
bun x skills add purview-dev/build-sdk --list             # see what is there, install nothing
```

Skills live **alongside the library they describe**, in that library's repository, rather than in one
central skills repository, so they change in the same commit as the code.

## Authoring and validation

`bun x skills init <name>` scaffolds a skill; move it under `skills/<name>/` and adapt the frontmatter.
`ValidatePurviewSkills` runs `BeforeTargets="Pack"`, so a malformed skill fails the build of the
repository that owns it rather than reaching the CLI, which would silently skip it. It checks that:

- every folder under the skills root has a `SKILL.md`,
- that file opens with YAML frontmatter carrying `name` and `description`,
- `name` matches the folder name, because the CLI installs by frontmatter name while a human reads
  the folder name, and
- every `references/...` path the body links to exists.

Failures are reported as `PRSKL0005`. The fuller rule set — names, descriptions, optional fields,
bundled links, body size — is `Purview.Skills.Testing`, run as tests (see the
`purview-buildsdk-skill-tests` skill). Prose quality is deliberately not checked.

## The recipes

```bash
just skills-add owner/repo[#ref]
just skills-install
just skills-update
just skills-check
just skills-fix
```

| Recipe | Runs | Changes the repository |
| -- | -- | -- |
| `skills-add <source>` | `skills add "<source>" --skill "*"` — every skill in that repository's root `skills/`; append `#<ref>` to pin | yes |
| `skills-install` | `skills experimental_install` — restore exactly what `skills-lock.json` records | yes |
| `skills-update` | `skills add "<source>" --skill "*"` once per unique lockfile source, at its latest; then `skills-fix` | yes |
| `skills-check` | nothing; fails (`PRSKL0015`) for an agent directory the selected agents do not include | no |
| `skills-fix` | nothing; removes the CLI's links from those directories | yes |

They come from the [`purview-buildsdk-justfile` skill](../../../skills/purview-buildsdk-justfile/SKILL.md):
`skills.just` holds the recipes and `skills.mts` does the work. The script needs no solution, no
restore and no build, so it runs in well under a second and still works when the build is broken —
which is when new or updated skills may be what fixes it. It runs under Bun, then Deno, and falls back
to Node with `skills.mjs`, which is generated from `skills.mts` with the types removed.
`PURVIEW_SKILLS_RUNTIME` picks one explicitly.

`--dry-run` makes install and update print every command and run none, and makes fix name what it
would remove and remove nothing (`just skills-update --dry-run` passes it to both). With no source and no `skills-lock.json` there is nothing to install
or update, which is `PRSKL0007`.

**Update is `skills add`, never `skills update`.** `skills add` reinstalls every skill it selects (the
CLI cleans each skill's folder and copies it again), so one re-add both refreshes the installed skills
and adds any the source has gained; `skills update` only re-fetches what the lockfile already lists.
Every source is attempted even when one fails, and each failure is named as `PRSKL0008`.

**Never from a build or a hook.** Do not make a skills recipe a dependency of `build`, `test` or
`default`, and do not run `skills-add`, `-install`, `-update` or `-fix` from a git hook: they change
the repository. `skills-check` is read-only.

## Settings

Settings live in the `Skills` section of `.config/purview-build-sdk.json` at the repository root:

```json
{
  "Skills": {
    "Agents": ["universal"],
    "Runner": "bun"
  }
}
```

| Setting | Default | Description |
| -- | -- | -- |
| `Agents` | *(empty — the CLI auto-detects)* | Agents to install to; when set, the only agent skill directories `skills-check` allows. |
| `Runner` | `bun` | Package runner used to launch the CLI. |
| `RunnerCandidates` | `bun npx bunx pnpm yarn deno` | Probed in order after `Runner`. |
| `Package` | `skills@1` | Package spec the launcher pins to. |
| `Command` | *(detected)* | Overrides the launcher entirely and skips runner detection. |
| `LockFile` | `skills-lock.json` | Where the CLI's lockfile is read from, relative to the repository root. |
| `DryRun` | `false` | Print the install and update commands without running them, and make fix remove nothing. |
| `AgentDirectories` | *(the CLI's agent table)* | Extra agent skill directories, each mapped to its agents. |

Each has a command-line option (`--agents`, `--runner`, `--package`, `--command`, `--lock-file`,
`--dry-run`) and a `PURVIEW_SKILLS_*` environment variable (`PURVIEW_SKILLS_AGENTS`, and so on).
Highest first: the option, the environment variable, the file, the default. A list may be a JSON array
or one string separated by `,`, `;` or spaces. A setting with the wrong type, or a file that is not
JSON, is `PRSKL0018`; a key that is not a setting is `PRSKL0019`, a warning, because it is usually a
misspelling.

## Choosing agents

`Agents` is **empty by default**: no `--agent` is passed and the CLI auto-detects the agents installed
on the machine. Set it to choose. Each agent becomes its own `--agent` flag, because the CLI does not
split a comma-separated value. A restore (`experimental_install`) is passed no agent flag.

Once agents are named, `skills-check` enforces them: any agent skill directory at the repository root
that belongs to an agent not named fails as `PRSKL0015`. Two directories are always allowed —
`.agents/skills`, the CLI's canonical store that every agent directory links into, and the authored
`skills/` folder, even though openclaw installs into a root `skills/`. With `Agents` empty, every agent
directory is a legitimate destination and both recipes report that there is nothing to enforce.

`skills-fix` removes what is provably the CLI's: links (symlinks, and junctions on Windows) — the link
only, never its target — and then any folder left empty. A real file is kept and reported, because it
cannot be told from hand-written content; for some agent selections the CLI writes copies rather than
links, and those are kept too.

The agent-to-directory map is taken from the CLI's agent table. The CLI adds agents independently of
this SDK, so an agent the map does not know is `PRSKL0016` (a warning), fixed by adding it:

```json
{ "Skills": { "AgentDirectories": { ".new-agent/skills": ["new-agent"] } } }
```

## The lockfile

`skills-lock.json` is the CLI's own record of what is installed and where it came from. The recipes
read it for `skills-update` and introduce no competing lockfile and no competing metadata. Commit it:
a fresh clone then restores the same set with `just skills-install`. Everything the CLI writes into
the agent directories is reproducible from it, so ignore those:

| Path | Written by | Tracked |
| -- | -- | -- |
| `skills/<name>/` | authored by hand | yes |
| `skills-lock.json` | the CLI | yes |
| `.agents/skills/<name>/` | the CLI's "universal" location | no |
| `.claude/skills/`, `agent/skills/`, … | per-agent links into it | no |

Ignore `.agents/skills/*` specifically rather than the whole of `.agents/`, so authored
`.agents/agents/` and `.agents/prompts/` content stays tracked. A repository-local skill — guidance
about working in that repository, never published — can be authored in `.agents/skills/<name>/` and
un-ignored by name.

### One installer

Skills have exactly one installer: **the skills CLI**. The SDK writes nothing into an agent directory,
at all. Earlier versions mirrored a package's whole `.agents/**` folder into the consuming repository;
that is gone, because the CLI's universal install location is `.agents/skills/` — exactly where the
mirror wrote — and two writers on one set of paths meant the mirror silently overwriting a pinned
install on the next build.

### Installing in the repository that authors the skills

A repository that authors skills it also uses can install from its own working tree, so an edit to
`skills/<name>/SKILL.md` reaches its agents without a push:

```bash
just skills-add .
```

The CLI records that portably — `"source": "."`, `"sourceType": "local"`. `Purview.BuildSdk` itself does
not: every skill it authors exists for *other* repositories, so an agent working here reads them at
their authored path.

## Package runners

The CLI is published to npm, and every package runner spells "run this npm package" its own way.
`Runner` picks one; it defaults to **`bun`** (`bun x skills`).

| `Runner` | Executed |
| -- | -- |
| `bun` *(default)* | `bun x skills@1` |
| `npx` | `npx -y skills@1` |
| `bunx` | `bunx skills@1` |
| `pnpm` | `pnpm dlx skills@1` |
| `yarn` | `yarn dlx skills@1` |
| `deno` | `deno run -A npm:skills@1` |
| `skills` | `skills` |

A runner this script has never heard of still works: it falls through to `<runner> skills@1`.
`yarn dlx` needs Yarn 2+. The launcher pins the CLI to a major so a breaking release cannot change
what a recipe does; `Package` (default `skills@1`) overrides the pin.

### Detecting the CLI

The script probes `PATH` rather than assuming the configured runner is present, honouring `PATHEXT`
on Windows where these are `.cmd` shims, in this order:

1. `skills` — used as-is if it is already on `PATH`. No download, works offline.
2. `Runner` — the configured preference.
3. `RunnerCandidates` — `bun npx bunx pnpm yarn deno` by default.

The runner actually found wins over the configured preference, because it is the one that can really
run. `PATH` is read directly rather than by spawning a version check: deterministic, no process, and
it cannot hang on a cold download. Nothing found is `PRSKL0004`, naming the fix; `Command` overrides
the launcher entirely and skips detection. A dry run with nothing on `PATH` prints the configured
runner's command instead of failing.

## Always from the repository root

`skills-lock.json` and the agent directories belong to the repository, not to a project, so every
command runs the CLI **from the repository root**. The recipes pass it as `--root`: the directory
holding the root `Justfile`. Run by hand, the script uses the nearest folder holding `.git`, else the
current one.

### Where validation looks

`ValidatePurviewSkills` is MSBuild, so it resolves the root itself, through this chain:

1. `PurviewSkillsInstallRoot` — an explicit override
2. `PurviewSkillsRepoRoot` — when the *user* set it
3. the **marker walk** (below)
4. `PurviewSkillsRepoRoot` / `RepoRoot` as the SDK guessed them
5. `GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY` or `CI_PROJECT_DIR`
6. the git source root SourceLink reports

There is deliberately **no fall back to the project directory**: a project's own folder is not where a
repository's skills live.

### The marker walk

`PurviewRepositoryRootMarkers` lists what identifies a repository root, most authoritative first:

```text
.git/ ; .github/ ; .gitattributes ; LICENSE.md ; AGENTS.md ; .agents/ ; CLAUDE.md ; .claude/ ; package.json
```

A trailing slash marks a directory — and still matches a *file* of that name, because `.git` is a
directory in a clone but a file in a worktree or submodule. MSBuild's own `GetDirectoryNameOfFileAbove`
matches files only, which is why this is a task rather than an intrinsic: it finds nothing for `.git/`,
`.github/`, `.agents/` or `.claude/`.

Resolution runs in two passes, because neither pure priority nor pure proximity is right alone:

1. **The first marker in the list is definitive** and is searched all the way up. `.git/` *is* the
   repository root by definition, so a project inside a monorepo package resolves to the repository
   rather than to the nearer folder holding a `package.json`.
2. **Otherwise the nearest ancestor** holding any marker wins, with list order breaking ties inside that
   one directory.

Pass 2 is nearest-first on purpose. Searching every remaining marker to the top would let a stray
`.claude/` in a developer's home directory outrank the `package.json` at the real repository root. For
the same reason the home directory and everything above it are **never** candidates. A repository
*inside* your home directory is unaffected, because it sits below it.

| Project at `/repo/sub/src/proj` | Resolves to |
| -- | -- |
| `/repo/.git/` + `/repo/sub/package.json` | `/repo` — the definitive marker wins from a distance |
| `/repo/.github/` + `/repo/sub/package.json` | `/repo/sub` — nearest wins once `.git/` is absent |
| `/repo/LICENSE.md` + `/repo/package.json` | `/repo`, reported as `LICENSE.md` (higher in the list) |
| no marker below the home directory | unresolved, never `~` |

## Configuration

| Property | Default | Description |
| -- | -- | -- |
| `PurviewSkillsInstallRoot` | *(resolved)* | Overrides the repository root whose `skills/` folder `ValidatePurviewSkills` checks. |
| `PurviewRepositoryRootMarkers` | `.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json` | Ordered markers that identify a repository root; the first is definitive. |

## Diagnostics

| Code | Meaning |
| -- | -- |
| `PRSKL0004` | No package runner was found on `PATH` |
| `PRSKL0005` | An authored skill is malformed |
| `PRSKL0007` | Nothing to install or update: no source, and no `skills-lock.json` |
| `PRSKL0008` | Adding or re-adding a source failed |
| `PRSKL0015` | An agent skill directory exists for an agent `Agents` does not name |
| `PRSKL0016` | `Agents` names an agent with no known skills directory |
| `PRSKL0018` | `.config/purview-build-sdk.json` is not valid JSON, or a setting has the wrong type |
| `PRSKL0019` | The `Skills` section has a key that is not a setting |

Retired: `PRSKL0003` and `PRSKL0009`, with the `InstallPurviewSkills`, `UpdatePurviewSkills`,
`CheckPurviewSkills` and `FixPurviewSkills` targets and their `PurviewSkills*` properties, which the
recipes replaced; `PRSKL0001`, `PRSKL0002`, `PRSKL0010`–`PRSKL0014` and `PRSKL0017`, with the
package-to-skill mapping and the `ListPurviewSkills`, `RecommendPurviewSkills` and `VerifyPurviewSkills`
targets they belonged to; and `PRSKL0006`, with `PurviewSkillsUpdateMode`.

## Supported CLI commands

The skills CLI v1 commands, and the role each plays here:

| Command | Purpose | Role here |
| -- | -- | -- |
| `bun x skills add <source>` | Install skills | What `skills-add` and `skills-update` run; accepts `owner/repo#<ref>` |
| `bun x skills experimental_install` | Restore from `skills-lock.json` | What `skills-install` runs; experimental in the CLI |
| `bun x skills list` | List installed skills | Diagnostics, run directly |
| `bun x skills find [query]` | Search for skills | Discovery, run directly |
| `bun x skills update` | Update to latest | Not used — an update is a re-add, which also picks up new skills |
| `bun x skills remove` | Remove skills | Run directly |
| `bun x skills use <source>@<skill>` | Use without installing | One-off guidance |
| `bun x skills init [name]` | Scaffold a skill | Authoring |

## See also

- [Configuration Reference](../Configuration-Reference.md)
- [Justfile](Justfile.md)
