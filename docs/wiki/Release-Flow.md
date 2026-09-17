# Release Flow

This repository uses the shared [`Purview.Build`](https://github.com/purview-dev/build) pipeline for
the full PR/release cycle, and plain `dotnet`/`just` commands for focused local work.

## Versioning

- **`package.json` is the single source of truth for the version.** The `version` field is read by
  the SDK's [Version Detection](Version-Detection.md) logic and applied to `Version` and
  `PackageVersion` for every build and pack.
- Current package version: read from `package.json` (`just current_version`).

## Local workflow

```text
dotnet tool restore          # install local tools (csharpier, etc.)
just build                   # dotnet build src/BuildSdk.slnx --configuration Debug
just test                    # dotnet test with a TUnit tree-node filter
just lint-check              # csharpier check
just lint-fix                # csharpier format .
just pack                    # dotnet pack to ./artifacts
just pipeline-pr             # restore, build, lint, tests, pack, package validation
just pipeline-local-release  # restore, build, lint, tests, pack, local NuGet publish
```

## Pipeline workflows

| Recipe | Purpose |
| -- | -- |
| `just pipeline-pr` | PR pipeline — restore, build, lint, tests, pack, package validation. |
| `just pipeline-build` | Build pipeline — restore, build, lint, pack, package validation (no tests). |
| `just pipeline-tests` | Tests pipeline — restore, build, lint, tests (no pack). |
| `just pipeline-release` | Release pipeline — restore, build, lint, tests, pack, publish to NuGet, GitHub release. |
| `just pipeline-local-release` | Release pipeline with a local NuGet publish (`--Release:Mode=LocalNuGet`). |

The pipeline configuration lives in `purview-build.json`:

```json
{
  "Build": {
    "Solution": "src/BuildSdk.slnx",
    "TestRoot": "src/tests",
    "TestPatterns": "*Tests.csproj",
    "TestFilter": "/*/*/*/*[Category=Unit]/"
  },
  "PackValidation": {
    "RequireSymbolPackage": false
  },
  "Release": {
    "Mode": "None"
  }
}
```

## CI workflows

- **PRs** — `.github/workflows/pr.yml` runs the PR pipeline on pull requests.
- **Releases** — `.github/workflows/release.yml` triggers on pushes to `main` and runs the shared
  `purview-release.yml` workflow with `release-mode: NuGet`, which builds, packs, validates, and
  publishes the package to NuGet.
- `continuousIntegrationBuild` is detected automatically from `CI`, `GITHUB_ACTIONS`, or `TF_BUILD`
  environment variables for deterministic SourceLink output.

## Testing

- Tests use **Microsoft.Testing.Platform** (`global.json` sets `"runner": "Microsoft.Testing.Platform"`)
  and **TUnit** conventions.
- CI runs the full suite on `ubuntu-latest`, so all tests and features must work identically on
  Windows, Linux, and macOS — never hardcode platform-specific paths in tests or fixtures.
- The integration harness (`ProjectHarness`) creates throwaway projects under `Path.GetTempPath()`;
  see `src/tests/BuildSdk.IntegrationTests/Harness/ProjectHarness.cs`.
- Linux agent-pack integration tests can be run locally via `just test-linux` (Docker-based).