# The repository-root marker walk

`ValidatePurviewSkills`, the pack-time check on a repository's authored `skills/`, finds the
repository root with `ResolvePurviewSkillsRepositoryRoot`:

1. `PurviewSkillsInstallRoot` — an explicit override
2. `PurviewSkillsRepoRoot` — only when the **user** set it
3. the `PurviewRepositoryRootMarkers` walk
4. `PurviewSkillsRepoRoot` / `RepoRoot` as the SDK guessed them
5. CI workspace variables
6. SourceLink's git `SourceRoot`

The `skills-*` recipes do not use any of this: they pass the directory holding the root `Justfile`.

`FindPurviewRepositoryRoot` is an inline MSBuild task driven by `PurviewRepositoryRootMarkers`:

```text
.git/;.github/;.gitattributes;LICENSE.md;AGENTS.md;.agents/;CLAUDE.md;.claude/;package.json
```

A trailing slash marks a directory. A directory marker still matches a **file** of that name,
because `.git` is a directory in a clone but a file in a worktree or a submodule, and both identify
a repository root.

## Three things that are load-bearing

### It cannot be an MSBuild intrinsic

`GetDirectoryNameOfFileAbove` matches **files only** and returns nothing for `.git/`, `.github/`,
`.agents/` or `.claude/` — four of the nine markers. Verified, not assumed.

### Two passes

- **Pass 1** — the first marker in the list is *definitive* and is searched all the way up. `.git/`
  *is* the repository root, so a monorepo sub-package with its own `package.json` does not win.
- **Pass 2** — every other marker resolves nearest-ancestor-first, with list order breaking ties
  inside a single directory. Searching them all to the top would let a stray `.claude/` in a
  developer's home directory outrank the real root.

### The home directory is never a candidate

The home directory and everything above it are excluded. Without that bound, a project outside any
repository resolves to `~` — and a `.claude/` or a `.github/` very commonly sits there. A repository
*below* the home directory is unaffected, because it is below it.

## Why the marker walk outranks the evaluation-time guess

`PurviewSkillsRepoRoot` is computed during evaluation from `RepoRoot` and an `AGENTS.md` walk-up.
An `AGENTS.md` can legitimately sit in a subdirectory, and resolving to it would validate a folder
that is not the repository's `skills/`.

`_PurviewSkillsRepoRootWasSpecified` is captured in `Sdk.props` *before* defaulting, so a value the
**user** set still outranks the walk while a value the SDK guessed does not.

## A target's Condition is evaluated before its DependsOnTargets

A property a dependency computes is still empty in the depending target's `Condition`. That is why
`ValidatePurviewSkills` tests that the folder exists on its task rather than in its own `Condition`.

## The managed port

`Purview.Skills.Testing` ships `SkillRepositoryRoot`, a managed port of the same walk with the same
default markers, so a test that validates a repository's skills resolves the same root the pack-time
check validates. In the Purview.BuildSdk repository the two are kept in step twice over:
`SkillRepositoryRootParityTests` runs the real MSBuild target and the managed port over the same
folders and compares the root and the matched marker, and `SkillRepositoryRootTests` pins the default
marker list itself — both are defaults nobody sets, so nothing else would notice them drifting apart.
