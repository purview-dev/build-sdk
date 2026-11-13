# Release Flow

This repository uses the shared [`Purview.Build`](https://github.com/purview-dev/build) pipeline for
the full PR/release cycle, and plain `dotnet`/`just` commands for focused local work.

## Versioning

- **`package.json` is the single source of truth for the version.** The `version` field is read by
  the SDK's [Version Detection](../configuration/Version-Detection.md) logic and applied to `Version` and
  `PackageVersion` for every build and pack.
- Current package version: read from `package.json` (`just version`; `just bump major|minor|patch` changes it).

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
| `just pipeline-tests` | Tests pipeline — restore, build, lint, tests, pack, package validation, with release explicitly off. |
| `just pipeline-release` | Release pipeline — restore, build, lint, tests, pack, publish to NuGet, GitHub release. |
| `just pipeline-local-release` | Release pipeline with a local NuGet publish (`--Release:Mode=LocalNuGet`). |
| `just pipeline-pack-validate` | Through pack and package validation, publishing nothing. |

The recipes come from the `purview-build.just` and `purview-build-nuget.just` modules in
`.build/just/` — see the [`purview-buildsdk-justfile` skill](../../../skills/purview-buildsdk-justfile/SKILL.md).
Package validation checks each package against `PackValidation.RequiredContent`: a file a package
ships that the list does not name fails the run as *undeclared content*, so a new file under
`Sdk/` needs an entry there too.

The pipeline configuration lives in `.config/purview-build.json`. Every `Build` stage — tests, lint, pack
and pack validation — runs unless the file or a `--Build:<Setting>=false` argument turns it off:

```json
{
	"Build": {
		"Solution": "src/BuildSdk.slnx",
		"TestRoot": "src/tests",
		"TestPatterns": "*Tests.csproj",
		"TestFilter": "/*/*/*/*/"
	},
	"PackValidation": {
		"RequiredCompilerFlags": [
			"optimization=release"
		],
		"RequireSymbolPackage": false,
		"RequiredContent": {
			"purview.buildsdk": [
				"analyzers/dotnet/cs/Purview.BuildSdk.Analyzers.dll",
				"analyzers/dotnet/cs/Purview.BuildSdk.CodeFixers.dll",
				"purview-logo-light.png",
				"README.md",
				"Sdk/.editorconfig",
				"Sdk/Props/Defaults.props",
				"Sdk/Props/PackedVersion.props",
				"Sdk/Props/VersionDetection.props",
				"Sdk/Sdk.props",
				"Sdk/Sdk.props.buildschema.json",
				"Sdk/Sdk.targets",
				"Sdk/Targets/Blazor.targets",
				"Sdk/Targets/Modern.targets",
				"Sdk/Targets/PropertySchema.targets",
				"Sdk/Targets/ServerShared.targets",
				"Sdk/Targets/TargetFrameworkOwnership.targets",
				"Sdk/Targets/VersionDetection.targets"
			],
			"purview.skills.testing": [
				"README.md",
				"lib/$(TFM)/Purview.Skills.Testing.dll",
				"lib/$(TFM)/Purview.Skills.Testing.xml"
			]
		}
	},
	"Release": {
		"Mode": "None"
	}
}
```

## CI workflows

- **PRs** — `.github/workflows/pr.yml` calls the shared `purview-build.yml` workflow twice: one job
  builds, lints and packs without tests, and a matrix job runs each test project separately (the
  matrix lists them by name, so a new test project must be added to it).
- **Releases** — `.github/workflows/release.yml` triggers on pushes to `main` and runs the shared
  `purview-release` composite action with `release-mode: NuGet` and nuget.org Trusted Publishing,
  optionally mirroring to GitHub Packages.
- `ContinuousIntegrationBuild` is set when the `CI`, `GITHUB_ACTIONS` or `TF_BUILD` environment variable
  is `true`, for deterministic SourceLink output.

## Testing

- Tests use **Microsoft.Testing.Platform** (`global.json` sets `"runner": "Microsoft.Testing.Platform"`)
  and **TUnit** conventions.
- CI runs the full suite on `ubuntu-latest`, so all tests and features must work identically on
  Windows, Linux, and macOS — never hardcode platform-specific paths in tests or fixtures.
- The integration harness (`ProjectHarness`) creates throwaway projects under `Path.GetTempPath()`;
  see `src/tests/BuildSdk.IntegrationTests/Harness/ProjectHarness.cs`.
- The SDK integration suite can be run on Linux locally via `just test-linux` (Docker-based).