# Justfile

A repository that uses Purview.BuildSdk keeps its shared `just` recipes in modules under `.build/just/`,
imported through one barrel, so its own `Justfile` holds only what is specific to it. The modules are
the [`purview-buildsdk-justfile`](../../../skills/purview-buildsdk-justfile/SKILL.md) skill's
`assets/just/`, copied verbatim, and that skill is how an agent adds or migrates them.

## Layout

```text
Justfile                    settings, one import, the repository's variables and own recipes
.build/just/
  index.just                the barrel; optional modules are imported with `import?`
  env.just                  exported telemetry opt-outs and DOTNET_CLI_ENABLEAOT
  version.just              version, bump
  bump-version.ts           what `just bump` runs (Bun)
  dotnet.just               ensure-tools, restore, build, clean, test, pack
  utilities.just            lint-check, lint-fix, vs, scrub
  purview-build.just        pipeline-pr, pipeline-build, pipeline-tests
  skills.just               skills-add, -install, -update, -check, -fix
  purview-build-nuget.just  optional: pipeline-pack-validate, pipeline-local-release, pipeline-release
  docs.just                 optional: docs, docs-install, docs-check (a pm-docs-site project site)
```

The root `Justfile` sets `allow-duplicate-variables` and `allow-duplicate-recipes` and imports
`.build/just/index.just` — nothing else. Every module gives its variables a default, and a definition
in the root `Justfile` replaces it: `solution_file := "src/Acme.slnx"`. An optional module is switched
on by copying its file into `.build/just/` and off by deleting it; the barrel never changes.

## Variables

| Variable | Default | Used by |
| -- | -- | -- |
| `solution_file` | `.` | every .NET, utility and skill recipe |
| `build_configuration` | `Debug` | `build`, `clean`, `test`, `pack` |
| `artifacts_folder` | `./artifacts` | `pack`, `scrub` |
| `test_filter` | `/*/*/*/*/` | `test` with no argument |
| `version_file` | `package.json` | `version`, `bump` |
| `csharpier` | `dotnet csharpier` | `lint-check`, `lint-fix` |
| `pipeline_tool` | `dotnet purview-build` | the `pipeline-*` recipes |
| `skills_project` | `solution_file` | the `skills-*` recipes |
| `docs_site` | `src/docs-site` | the `docs*` recipes |

## Version

`just version` prints the version in `package.json`, read by `just` itself. `just bump major`,
`minor` or `patch` increments that part and resets the parts after it (`2.0.1` → `3.0.0`, `2.1.0`,
`2.0.2`), drops any pre-release suffix, and rewrites only the `"version"` value. `--dry-run` writes
nothing, and any other part is rejected. Afterwards it runs the `_after-bump` hook, which does nothing
unless the root `Justfile` overrides it — this repository's runs `version-sync`.

## Environment

`env.just` exports, to every process a recipe starts, the telemetry opt-outs for the .NET CLI,
MSBuild, Aspire, the Azure SDKs, PowerShell, Git Credential Manager, Next.js, Astro, Yarn and Claude
Code, plus `DO_NOT_TRACK`, `DOTNET_NOLOGO` and `DOTNET_CLI_ENABLEAOT`. Each is a default: a value
already in the environment wins.

The MSBuild server (`DOTNET_CLI_USE_MSBUILD_SERVER`) is deliberately left to the SDK's default. A
shared server keeps the NuGet settings it first read, so a later build under a different `nuget.config`
resolves SDKs from the wrong sources and fails with `MSB4242`. This repository's package-consumption
tests do exactly that. Multithreaded MSBuild (`-mt`) comes from the `Directory.Build.rsp` the SDK
bootstraps, not from the environment.

## This repository

The root `Justfile` here keeps `version-sync`, `version-sync-check`, `properties-check`, `test-linux`
and `skills-validate`, and overrides `_after-bump`. `JustModuleParityTests` fails if `.build/just/`
differs from the skill's assets, holds a file the skill does not ship, or if the root `Justfile`
imports anything but the barrel or redefines a module's recipe.
