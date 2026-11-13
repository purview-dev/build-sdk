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

The shipped file also defines the style policy
(`dotnet_style_require_accessibility_modifiers = omit_if_default`, private instance fields named
`_camelCase`, with `IDE0040`, `CA1515`, `CA1852` and `CA1034` at warning or above). Because a repository
owner can edit a bootstrapped copy, the `ValidatePurviewStylePolicy` target re-checks the *effective*
configuration on every compile and fails the build (`PRSGD0006`-`PRSGD0009`) when that policy has been
overridden, hidden, or silenced through `NoWarn`. A stale copy that predates the current policy (for
example one that still hides `IDE1006`) is therefore reported rather than obeyed — refresh it with
`PurviewRepoBootstrapMode=Always`. Opt out with `DisablePurviewStylePolicyValidation=true`; see
[Style policy](analyzers/Analyzers.md#style-policy).

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
| `PurviewBuildSdkVersionForGlobalJson` | *(restored package version, else the version it was packed as)* | Version written to the `msbuild-sdks.Purview.BuildSdk` entry. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |
| `PurviewRepoBootstrapMode` | `IfMissing` | `IfMissing`, `Always`, `WarnOnDrift` or `Never`. |

## `Directory.Build.rsp` bootstrapping

`-mt` is an MSBuild **host** switch, so no project property can enable it: the host reads its switches
before evaluation. A `Directory.Build.rsp` in any ancestor of a project is the one file the CLI reads
that can, so the SDK writes one at the repository root:

```text
# Purview.BuildSdk - managed file
# Managed by Purview.BuildSdk. Delete the marker line to take ownership of this file.
-mt
-nr:false
```

`-nr:false` is deliberate. A node left by a non-`-mt` build can hold a cached `RoslynCodeTaskFactory`
task with no assembly path, and the first `-mt` build that reuses it fails with `MSB0001`
([dotnet/msbuild#15213](https://github.com/dotnet/msbuild/issues/15213)) — which is exactly the
transition a repository makes when this file first appears. A short-lived server still provides the
Server GC `-mt` expects. `-mt` requires .NET 11; the bootstrap is skipped on an older SDK.

The file is SDK-managed: a destination carrying the marker line is refreshed, one without it belongs
to the developer and is never touched. `PurviewMultiThreadedBuild=false` rewrites it to `-mt:false`
rather than deleting it, so opting out cannot leave a stale copy enabling the mode.

Like the other bootstraps it writes atomically, retries quietly, and honours `PurviewRepoBootstrapMode`:

| Property | Default | Description |
| -- | -- | -- |
| `BootstrapMultiThreadedBuildRspToRepoRoot` | `true` | Creates the `Directory.Build.rsp` when missing. |
| `RepositoryDirectoryBuildRspFilePath` | *(auto-detected)* | Override the destination path. |
| `PurviewMultiThreadedBuild` | `true` | `false` writes `-mt:false` instead of `-mt`. |
| `DisableAutoCopySdkFiles` | `false` | Master switch that disables repo-level SDK file bootstrapping. |

## Retry and failure behaviour

All bootstraps share the same retry contract:

| Property | Default | Description |
| -- | -- | -- |
| `PurviewRepoBootstrapCopyRetries` | `3` | Write attempts before the failure is reported. |
| `PurviewRepoBootstrapCopyRetryDelayMilliseconds` | `500` | Base delay between attempts (a small increment is added per attempt). |
| `PurviewRepoBootstrapCopyFailureAsError` | `true` | When `false`, a failure is reported as a warning and the build continues. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes built-in copy task retry notices (`MSB3026`) to messages. Set to `false` to see every retry. |

Retries are therefore silent by design, while a write that never succeeds is still reported - as an
error by default, including the destination path and the underlying OS error.

## Repository root discovery

All bootstrap targets locate the repository root the same way, chosen by `PurviewRepositoryRootStrategy`:

1. With `PurviewRepositoryRootStrategy=Git` only, `git rev-parse --show-toplevel` from the
   `Directory.Build.props` directory. The default, `Markers`, starts no process.
2. The `Directory.Build.props` directory, then up to four of its parents, in that order: the first that
   holds a `.git` folder or file is the root.
3. Otherwise, the `Directory.Build.props` directory itself.

`Git` reports the worktree root, so for a submodule or a linked worktree it can answer differently from
the nearest `.git`.

Set the `RepositoryEditorConfigFilePath`, `RepositoryGlobalJsonFilePath` or
`RepositoryDirectoryBuildRspFilePath` property explicitly to override discovery.