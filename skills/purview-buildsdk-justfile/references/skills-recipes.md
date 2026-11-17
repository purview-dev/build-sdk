# The agent-skill recipes

`skills.just` gives each skill command a recipe, and `skills.mts` beside it does the work: a thin
wrapper over the standard skills CLI (`bun x skills` by default), which finds a repository's skills in
its root `skills/` folder and records what it installed in `skills-lock.json`. Their behaviour is
covered by `purview-buildsdk-skill-installation`; this page is about the recipes and the files.

## Three files, copied together

| File | What it is |
| -- | -- |
| `skills.just` | the recipes |
| `skills.mts` | the script, in TypeScript; Bun and Deno run it directly |
| `skills.mjs` | the same script with the types removed, for a machine with Node but neither Bun nor Deno |

`skills.mjs` is **generated** from `skills.mts` and must not be edited by hand. Copy all three, or the
recipes fail on the machine whose runtime needs the missing one.

None of them involves .NET. There is no solution to evaluate, no restore and no build, so the recipes
take well under a second and work in a repository whose build is broken.

## The rule that matters most

**Every skill recipe is run by a person, on purpose.** `skills-add`, `-install`, `-update` and `-fix`
change the repository. Keep the `Justfile` consistent with that:

- never make a skills recipe a dependency of `build`, `test`, `default` or any other recipe;
- never call `skills-add`, `skills-install`, `skills-update` or `skills-fix` from a git hook
  (`lefthook.yml`, `.husky/`) or from a CI job that commits;
- `skills-check` is read-only and safe anywhere.

## The recipes

| Recipe | Command | Runs | Changes the repository |
| -- | -- | -- | -- |
| `skills-add <owner/repo[#ref]>` | `add` | `skills add <source> --skill "*"` | yes |
| `skills-install` | `install` | `skills experimental_install` — restore `skills-lock.json` | yes |
| `skills-update` | `update`, then `fix` | `skills add <source> --skill "*"` once per lockfile source | yes |
| `skills-check` | `check` | nothing | no |
| `skills-fix` | `fix` | nothing; removes links itself | yes |

Each passes `--root` with the directory holding the root `Justfile`, and `*args` last, so a caller can
add options: `just skills-update --dry-run --agents claude-code,codex`.

The runtime is the first of Bun (`skills.mts`), Deno (`skills.mts`) and Node (`skills.mjs`) on `PATH`.
`PURVIEW_SKILLS_RUNTIME` overrides it — `node`, `bun`, or `deno run -A`. `just`'s own `which()` is not
used, because it needs an unstable setting; the choice is a small `sh` function instead.

A repository that *authors* skills usually adds `skills-validate` to its root `Justfile`. It runs
`ValidatePurviewSkills` — the pack-time check, which is still MSBuild — against the one project that
packs, because that target runs once per project it is invoked on:

```just
[group('Agent Skills')]
skills-validate *args:
    dotnet msbuild "src/src/Acme/Acme.csproj" -t:ValidatePurviewSkills -v:n {{ args }}
```

## Settings belong in `.config/purview-build-sdk.json`, not in the recipe

Set repository-wide choices once, not as flags repeated in each recipe, so running the script by hand
behaves the same as the recipe:

```json
{
  "Skills": {
    "Runner": "bun",
    "Agents": ["universal"]
  }
}
```

`Agents` is optional; setting it makes `skills-check`/`skills-fix` enforce that only those agents'
skill directories exist. One-off overrides go after the recipe name or in a `PURVIEW_SKILLS_*`
environment variable. The full table is in `purview-buildsdk-skill-installation`.

| Option | Effect |
| -- | -- |
| `--dry-run` | Print every CLI command; run none |
| `--runner npx` | Launch the CLI through another runner (`bunx`, `pnpm`, `yarn`, `deno`) |
| `--agents a,b` | Install to, and enforce, these agents |

## Details that break a recipe if missed

- **Keep `*args` last** so a caller can pass options.
- `fix` after `update` does nothing unless agents are selected, so it is safe in the recipe either way.
- `skills-install` with no lockfile fails with `PRSKL0007`: add a source first with `skills-add`.
- `skills.mts` must stay erasable TypeScript — no `enum`, `namespace` or parameter properties — or
  Node's type stripper cannot produce `skills.mjs` from it.

## Moving from the MSBuild targets

The recipes used to run `dotnet msbuild <solution> -t:InstallPurviewSkills` and friends, which
evaluated every project in the solution each time and failed whenever one did not evaluate. Those
targets and their `PurviewSkills*` properties are gone. Copy the three files above, and move any
`PurviewSkills*` property from `Directory.Build.props` into `.config/purview-build-sdk.json` — the
mapping is in `purview-buildsdk-skill-installation`. Delete a `skills_project` override from the root
`Justfile`; nothing reads it.

## Replacing hand-written skill scripts

| Hand-written | Replace with |
| -- | -- |
| `scripts/update-agent-skills.ts`, `"skills:update"` | `skills-update` |
| `--update` / `--no-update` | Nothing: the re-add refreshes installed skills and adds new ones, so there is no separate `skills update` step to switch |
| `--dry-run` | `--dry-run` |
| `scripts/ensure-universal-skills.ts check`, `"skills:check"` | `skills-check` with `Agents` set to `universal` |
| `scripts/ensure-universal-skills.ts fix`, `"skills:fix"` | `skills-fix` |
| a hard-coded `-a universal` | `Agents` — or leave it empty to auto-detect |

Update is `skills add`, not `skills update`, so it also installs skills a source has **gained**: each
source is re-added with `--skill "*"`. Mention that when migrating.

Before deleting the scripts, run `just skills-update --dry-run` and compare the printed commands with
what the old script ran. Then delete the scripts and their `package.json` entries — but keep
`package.json` itself, which Purview.BuildSdk reads the version from.

An existing `skills-recommend`, `skills-verify` or `skills-list` recipe has nothing behind it any
more: delete it, and its callers.
