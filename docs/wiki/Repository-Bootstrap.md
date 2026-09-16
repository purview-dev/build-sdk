# Repository Bootstrap

The SDK bootstraps repo-level files so external tooling that does not read MSBuild item metadata (for
example CSharpier, IDE formatting tools, and SDK resolution) works consistently out of the box.

## `.editorconfig` bootstrapping

The package ships an `.editorconfig` in `Sdk/.editorconfig`. It is:

1. Registered on `@(EditorConfigFiles)` via `EditorConfigFilePath` for build-time code-style
   enforcement (`EnforceCodeStyleInBuild=true`, `EnableNETAnalyzers=true`, `AnalysisLevel=latest`,
   `AnalysisMode=All`).
2. Copied to the repository root (as a physical file) when a `.editorconfig` does not already exist
   there, so tools like CSharpier pick it up.

Control it with:

| Property | Default | Description |
| -- | -- | -- |
| `BootstrapEditorConfigToRepoRoot` | `true` | Copies the SDK `.editorconfig` to the repository root when missing. |
| `RepositoryEditorConfigFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `.editorconfig`. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |

## `global.json` bootstrapping

The SDK creates a `global.json` at the repository root when one is missing, registering
`Purview.DotNetProjectSdk` in `msbuild-sdks` and setting the `Microsoft.Testing.Platform` test runner:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Purview.DotNetProjectSdk": "<version>"
  }
}
```

Control it with:

| Property | Default | Description |
| -- | -- | -- |
| `BootstrapGlobalJsonToRepoRoot` | `true` | Creates a `global.json` at the repository root when missing. |
| `RepositoryGlobalJsonFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `global.json`. |
| `PurviewDotNetProjectSdkVersionForGlobalJson` | *(auto-detected or `1.0.0` fallback)* | Version written to the `msbuild-sdks.Purview.DotNetProjectSdk` entry. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |

## Repository root discovery

Both bootstrap targets locate the repository root by:

1. Running `git rev-parse --show-toplevel` from the `Directory.Build.props` directory.
2. Falling back to probing upward for a `.git` marker.
3. Finally falling back to the `Directory.Build.props` directory itself.

Set the `RepositoryEditorConfigFilePath` / `RepositoryGlobalJsonFilePath` properties explicitly to
override discovery.