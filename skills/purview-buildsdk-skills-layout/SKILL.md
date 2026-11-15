---
name: purview-buildsdk-skills-layout
description: Keep a repository's skills discoverable and its installs local - the arrangement every repository in this organisation uses, and the one `npx skills add <owner>/<repo>` depends on. Use when a repository tracks `.agents/skills/`, when a skill someone installed is being found by consumers, when adding or moving a `SKILL.md`, when a `.gitignore` needs the `.agents/skills/` and `.claude/` rules, when `skills-lock.json` and an installed folder disagree, or when auditing why `skills add` sees skills a repository does not publish.
license: MIT
metadata:
  package: Purview.BuildSdk
  repository: purview-dev/build-sdk
---

# Where a repository's skills live

Two folders, two purposes:

| Folder | What it is | Tracked |
| --- | --- | --- |
| `skills/<name>/SKILL.md` | the skills this repository **authors and publishes** - the only ones `npx skills add purview-dev/<repo>` should find | yes: committed and reviewed |
| `.agents/skills/<name>/SKILL.md` | the skills CLI's **local install**, written by `npx skills add` / `just skills-install` | no: gitignored, never committed |

`skills-lock.json` is the record on the second row: it *is* committed, so a fresh clone reproduces
the install with `just skills-install` (or `npx -y skills@1 experimental_install`).

## Why the arrangement matters

`npx skills add purview-dev/<repo>` reads the *repository*, so a committed `.agents/skills/`
publishes someone else's skills as if this repository had written them: a consumer asking for this
project's guidance is handed another project's. It also puts a second writer on a path the CLI owns,
so the CLI's next update fights the committed copy, and `git` shows a diff nobody meant to review.

`.claude/` and every other per-agent folder are the same problem one level down: a local copy that
drifts from the lockfile the moment the CLI updates it.

## Making a repository comply

```bash
# Untrack the install (the CLI wrote it; it stays on disk until you delete it)
git rm -r --cached -- .agents/skills
rm -rf .agents/skills

# A skill the repository's own agents need belongs with the published set
git mv .agents/skills/<name> skills/<name>
```

Then make sure `.gitignore` carries both rules, and never un-ignore a path under `.agents/skills/`:

```gitignore
.agents/skills/
.claude/
```

Commit `skills-lock.json` with the change; it is the only part of the install that belongs in git.

## Verify

```bash
git ls-files .agents/skills                        # must print nothing
git check-ignore -v .agents/skills/x .claude/x     # must match both rules
npx -y skills@1 experimental_install               # restores the install from the lockfile
```

A repository should hold the line with a test rather than a convention: this repository's
`RepositorySkillsSelfTests` asserts that nothing under `.agents/skills/` is tracked, that both ignore
rules are present, that no authored skill is self-installed, and that every `AGENTS.md` link into a
skill resolves to the authored copy under `skills/`.

Related skills: `purview-buildsdk-skill-installation` (the CLI and the MSBuild targets),
`purview-buildsdk-skill-tests` (authoring a `SKILL.md` that conforms), `purview-buildsdk-skill-...`
as published by this repository.
