# Repository Bootstrap

The SDK bootstraps repo-level files so external tooling that does not read MSBuild item metadata (for
example CSharpier, IDE formatting tools, and SDK resolution) works consistently out of the box.

## `.editorconfig` bootstrapping

The package ships an `.editorconfig` in `Sdk/.editorconfig`. It is:

1. Registered on `@(EditorConfigFiles)` via `EditorConfigFilePath` for build-time code-style
   enforcement (`EnforceCodeStyleInBuild=true`, `EnableNETAnalyzers=true`, `AnalysisLevel=latest`,
   `AnalysisMode=All`).
2. Written to the repository root (as a physical file) when a `.editorconfig` does not already exist
   there, so tools like CSharpier pick it up.

The write is atomic (a temporary file is renamed into place) and retried quietly, so parallel projects
sharing the repository root cannot corrupt it or fail the build by racing. An existing file is never
overwritten unless you ask for it.

Control it with:

| Property | Default | Description |
| -- | -- | -- |
| `BootstrapEditorConfigToRepoRoot` | `true` | Copies the SDK `.editorconfig` to the repository root when missing. |
| `RepositoryEditorConfigFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `.editorconfig`. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing` (never overwrite), `Always` (overwrite), `WarnOnDrift` (warn when the existing file differs from the SDK-provided one) or `Never` (skip all bootstrapping). |
| `PurviewRepoBootstrapCopyRetries` | `3` | Write attempts before the failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between write attempts. |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a failed bootstrap write is reported as a warning instead of an error. |

## `global.json` bootstrapping

The SDK creates a `global.json` at the repository root when one is missing, registering
`Purview.BuildSdk` in `msbuild-sdks` and setting the `Microsoft.Testing.Platform` test runner:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Purview.BuildSdk": "<version>"
  }
}
```

Like the `.editorconfig` bootstrap it writes atomically, retries quietly, never overwrites an existing
file, and honours `PurviewRepoBootstrapMode`:

| Property | Default | Description |
| -- | -- | -- |
| `BootstrapGlobalJsonToRepoRoot` | `true` | Creates a `global.json` at the repository root when missing. |
| `RepositoryGlobalJsonFilePath` | *(auto-detected)* | Override the destination path for the bootstrapped `global.json`. |
| `PurviewBuildSdkVersionForGlobalJson` | *(auto-detected or `1.0.0` fallback)* | Version written to the `msbuild-sdks.Purview.BuildSdk` entry. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing`, `Always`, `WarnOnDrift` or `Never`. |

## Retry and failure behaviour

Both bootstraps share the same retry contract:

| Property | Default | Description |
| -- | -- | -- |
| `PurviewRepoBootstrapCopyRetries` | `3` | Write attempts before the failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between attempts (a small increment is added per attempt). |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a failure is reported as a warning and the build continues. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes built-in copy task retry notices (`MSB3026`) to messages. Set to `false` to see every retry. |

Retries are therefore silent by design, while a write that never succeeds is still reported - as an
error by default, including the destination path and the underlying OS error.

## Repository root discovery

Both bootstrap targets locate the repository root by:

1. Running `git rev-parse --show-toplevel` from the `Directory.Build.props` directory.
2. Falling back to probing upward for a `.git` marker.
3. Finally falling back to the `Directory.Build.props` directory itself.

Set the `RepositoryEditorConfigFilePath` / `RepositoryGlobalJsonFilePath` properties explicitly to
override discovery.