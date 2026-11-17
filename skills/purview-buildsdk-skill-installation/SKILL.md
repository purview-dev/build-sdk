---
name: purview-buildsdk-skill-installation
description: Install, update, check and tidy agent skills in a repository that uses Purview.BuildSdk, through the standard skills CLI (bun x skills). Use for the just skills-add, skills-install, skills-update, skills-check and skills-fix recipes and the skills.mts script behind them, for .config/purview-build-sdk.json and its Skills section, for installing skills from a repository's root skills/ folder (owner/repo#ref), for restoring skills-lock.json in a fresh clone, for choosing agents, for any PRSKL diagnostic, for choosing a package runner (bun x, npx, pnpm dlx, bunx, yarn dlx, deno), for the removed InstallPurviewSkills/UpdatePurviewSkills MSBuild targets, or when an agent cannot see a skill that should be installed.
license: MIT
metadata:
  package: Purview.BuildSdk
  repository: purview-dev/build-sdk
---

# Installing agent skills

For *authoring* skills, use `purview-buildsdk-skill-tests`; for the `just` modules themselves,
`purview-buildsdk-justfile`.

## The one thing to understand first

**Skill installation is a thin wrapper over the standard skills CLI, and it does not involve .NET.** A
repository publishes skills in its root `skills/` folder; the skills CLI installs them with
`bun x skills add owner/repo` and records each install in `skills-lock.json`. The `skills-*` recipes
run that CLI from the repository root and keep the agent directories tidy. There is no package-to-skill
mapping: a NuGet package carries .NET artifacts, and skills come from git.

The recipes run `skills.mts`, a script the `purview-buildsdk-justfile` skill ships beside
`skills.just` in `.build/just/`. It needs no solution, no restore and no build, so it takes well under a
second and works in a repository whose build is broken — exactly when new or updated skills may be
what fixes it. It runs under Bun, then Deno, and falls back to Node with the generated `skills.mjs`.

```bash
just skills-add purview-dev/build-sdk     # add every skill a source has (append '#<ref>' to pin it)
just skills-install                       # restore skills-lock.json (a fresh clone)
just skills-update                        # re-add every lockfile source at its latest, then fix
just skills-check                         # only the selected agents' directories exist
just skills-fix                           # remove the CLI's links from any other agent directory
```

They run **only when a person runs them**. Do not make one a dependency of `build`, `test` or
`default`, and do not call `skills-add`, `-install`, `-update` or `-fix` from a git hook: they change
the repository. `skills-check` is read-only.

The SDK writes nothing into an agent directory itself. The CLI is the single writer, because its
universal install location is `.agents/skills/` and a second writer on the same paths would overwrite
a pinned install.

Without `just`, run the script directly from the repository root:

```bash
bun .build/just/skills.mts update --dry-run
node .build/just/skills.mjs check --agents claude-code
```

## Settings

Settings live in the `Skills` section of `.config/purview-build-sdk.json` at the repository root:

```json
{
  "Skills": {
    "Agents": ["claude-code", "codex"],
    "Runner": "bun"
  }
}
```

| Setting | Option | Environment | Default |
| -- | -- | -- | -- |
| `Agents` | `--agents` | `PURVIEW_SKILLS_AGENTS` | empty: the CLI auto-detects |
| `Runner` | `--runner` | `PURVIEW_SKILLS_RUNNER` | `bun` |
| `RunnerCandidates` | — | `PURVIEW_SKILLS_RUNNER_CANDIDATES` | `bun npx bunx pnpm yarn deno` |
| `Package` | `--package` | `PURVIEW_SKILLS_PACKAGE` | `skills@1` |
| `Command` | `--command` | `PURVIEW_SKILLS_COMMAND` | empty: detected from `PATH` |
| `LockFile` | `--lock-file` | `PURVIEW_SKILLS_LOCK_FILE` | `skills-lock.json` at the root |
| `DryRun` | `--dry-run` | `PURVIEW_SKILLS_DRY_RUN` | `false` |
| `AgentDirectories` | — | — | the CLI's agent table, extended by this |

Highest first: a command-line option, then the environment variable, then the file, then the default.
A list may be an array or one string separated by `,`, `;` or spaces. `--config` or
`PURVIEW_SKILLS_CONFIG` names a different file. Pass an option through a recipe's arguments:
`just skills-update --dry-run --agents claude-code,codex`.

`PURVIEW_SKILLS_RUNTIME` chooses the runtime the recipes start the script with — `node`, `bun` or
`deno run -A` — instead of the first of Bun, Deno and Node on `PATH`.

## Installing

| Recipe | Runs |
| -- | -- |
| `just skills-add owner/repo` | `skills add "owner/repo" --skill "*"` — every skill in that repository's root `skills/` |
| `just skills-add owner/repo#<ref>` | the same, pinned to a commit, tag or branch |
| `just skills-install` | `skills experimental_install` — restore exactly what `skills-lock.json` records |

The caller owns the ref: append `#<ref>` to pin it. With no source and no `skills-lock.json` there is
nothing to install, which is `PRSKL0007`. `bun x skills add` works just as well run directly; the
recipes exist so a repository uses one runner, one root and one agent selection everywhere.

## Updating

```bash
just skills-update --dry-run   # see the commands first; the fix step removes nothing either
just skills-update
```

`update` re-adds every unique source recorded in `skills-lock.json` with
`skills add "<source>" --skill "*"`, at its latest. That is the whole update: `skills add` reinstalls
every skill it selects, so one re-add both refreshes the installed skills and adds any the source has
gained, whereas `skills update` would only re-fetch what the lockfile already lists, and is never run.
Every source is attempted even when one fails, and each failure is named (`PRSKL0008`). No lockfile is
`PRSKL0007`. The recipe then runs `fix`.

## Choosing agents

`Agents` is **empty by default**, which passes no `--agent` and lets the CLI auto-detect the agents on
the machine. Set it to choose. Each agent becomes its own `--agent` flag; the CLI does not split a
comma-separated value. A restore (`experimental_install`) is passed no agent flag.

When `Agents` is set, `check` fails (`PRSKL0015`) for any agent skill directory at the repository root
that belongs to an agent it does not name. `.agents/skills` is always allowed — it is the CLI's
canonical store — and the authored `skills/` folder is never touched. `fix` removes links (symlinks,
junctions) from those directories, never their targets, and then any folder left empty; a real file is
kept and reported, because it is not provably the CLI's. The agent-to-directory map is taken from the
CLI's own agent table; an agent it does not know is `PRSKL0016`, fixed by adding it:

```json
{ "Skills": { "AgentDirectories": { ".new-agent/skills": ["new-agent"] } } }
```

With `Agents` empty, both commands say so and do nothing: every agent directory is then a legitimate
destination.

## Diagnostics

| Code | Meaning | Fix |
| -- | -- | -- |
| `PRSKL0004` | No skills CLI and no package runner found | Install Bun or Node, or set `Command` |
| `PRSKL0005` | An authored skill is malformed | See `purview-buildsdk-skill-tests` |
| `PRSKL0007` | Nothing to install or update: no source, and no `skills-lock.json` | Install from a source first |
| `PRSKL0008` | Adding or re-adding a source failed | Read the CLI output above it |
| `PRSKL0015` | An agent skill directory exists for an agent `Agents` does not name | `just skills-fix`, then remove anything it kept by hand |
| `PRSKL0016` | `Agents` names an agent with no known skills directory | Warning. Add it to `AgentDirectories` |
| `PRSKL0018` | `.config/purview-build-sdk.json` is not valid JSON, or a setting has the wrong type | Fix the file |
| `PRSKL0019` | The `Skills` section has a key that is not a setting | Warning. Usually a misspelling |

`PRSKL0003` and `PRSKL0009` went with the MSBuild skill targets. `PRSKL0001`, `PRSKL0002`,
`PRSKL0006`, `PRSKL0010`–`PRSKL0014` and `PRSKL0017` belonged to the earlier package-to-skill mapping.
All are retired.

## Package runners

The CLI is published to npm, and every runner spells "run this npm package" differently. `Runner`
selects one, defaulting to `bun` (`bun x skills`).

| Runner | Invocation |
| -- | -- |
| `bun` *(default)* | `bun x skills@1` |
| `npx` | `npx -y skills@1` |
| `bunx` | `bunx skills@1` |
| `pnpm` | `pnpm dlx skills@1` |
| `yarn` | `yarn dlx skills@1` — Yarn 2+ only |
| `deno` | `deno run -A npm:skills@1` |
| `skills` | `skills` — already on `PATH`, no download |

An unknown runner falls through to `<runner> <Package>`, so it still works. The launcher pins the CLI
to a major (`Package`, default `skills@1`).

The script probes `PATH` for `skills` first, then `Runner`, then `RunnerCandidates`, honouring
`PATHEXT` on Windows where these are `.cmd` shims. `PATH` is read directly rather than by spawning a
version check: deterministic, no process, and it cannot hang on a cold download. **The runner actually
found wins over the configured preference.** `Command` overrides the launcher entirely and skips
detection. A dry run with no runner on `PATH` prints the configured runner's command rather than
failing.

## Always the repository root

`skills-lock.json` and the agent directories belong to the repository, not to a project, so every
command runs from the repository root. The recipes pass it as `--root` (the directory holding the root
`Justfile`); run by hand, the script uses the nearest folder holding `.git`, else the current one.

`ValidatePurviewSkills`, the pack-time check on authored skills, is still MSBuild and resolves the
root its own way; see [references/repository-root.md](references/repository-root.md).

## Moving from the MSBuild targets

`InstallPurviewSkills`, `UpdatePurviewSkills`, `CheckPurviewSkills` and `FixPurviewSkills` were
removed, with the `PurviewSkillsAgents`, `PurviewSkillsDryRun`, `PurviewSkillsCommand`,
`PurviewSkillsSource`, `PurviewSkillsLockFile`, `PurviewSkillsRunner`, `PurviewSkillsRunnerCandidates`
and `PurviewSkillsPackage` properties and the `PurviewSkillsAgentDirectory` items. They evaluated every
project in the solution on each run, so they were slow and failed outright whenever a project did not
evaluate. Copy `skills.just`, `skills.mts` and `skills.mjs` from the `purview-buildsdk-justfile` skill
into `.build/just/`, and move any of those properties into `.config/purview-build-sdk.json`:

| Removed property | Setting |
| -- | -- |
| `PurviewSkillsAgents` | `Agents` |
| `PurviewSkillsRunner` | `Runner` |
| `PurviewSkillsRunnerCandidates` | `RunnerCandidates` |
| `PurviewSkillsPackage` | `Package` |
| `PurviewSkillsCommand` | `Command` |
| `PurviewSkillsLockFile` | `LockFile` |
| `PurviewSkillsDryRun` | `--dry-run` |
| `PurviewSkillsSource` | the `skills-add` argument |
| `PurviewSkillsAgentDirectory` items | `AgentDirectories` |

## Why the CLI rather than a reimplementation

The CLI owns the agent-specific destinations and their discovery conventions, which differ per agent
and change independently of this SDK. It also owns `skills-lock.json`, which is what lets a fresh
clone restore the same set with `just skills-install` (`skills experimental_install`). There is
deliberately no competing lockfile and no competing metadata.
