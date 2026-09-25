# Agent Folder

`Purview.BuildSdk` ships bundled agent content (skills, prompts, agents) under `.agents/**`
in the NuGet package. During build, the SDK copies it into the consuming repository's `.agents/`
folder by default so compatible coding agents can discover repository-aware guidance automatically.

## How the copy works

- `EnableAgentFolderInPackage=true` (default) syncs the bundled `.agents/**` folder from the SDK
  NuGet package into `$(AgentPackDestinationFolder)/` (default `.agents`) in the consuming repository
  **before build**.
- The source folder defaults to the package-level `.agents` folder beside `Sdk/` and can be pointed
  elsewhere with `PurviewAgentFolderSourcePath`.
- The destination root is resolved from `RepoRoot` (auto-discovered repo root), an `AGENTS.md` walk-up,
  or SourceLink source roots.
- The sync is also performed for bundled `.agents` folders shipped by **any** restored NuGet package
  that used `PurviewAutoSdkPack` (read from `project.assets.json`), not just this SDK.
- Unchanged content is skipped using a repository manifest (`.purview/agent-sync.cache` by default), so
  repeat builds do not touch any file. The manifest records a per-file size/timestamp fingerprint plus a
  content hash, which is what makes an in-place package republish (same version, new content) re-sync.
  Deleting a mirrored file also re-triggers its copy.
- Every write is staged into a temporary file in the destination folder and then renamed into place, so a
  reader never observes a partially written file and concurrent writers cannot interleave.
- Several projects share the same destination, so simultaneous writes are expected. Those retries are
  logged at low importance only (MSB3026 copy retry notices are demoted to messages) and a copy that
  still fails after every retry is reported as an **error** by default.

## Failure and retry behaviour

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAgentFolderSourcePath` | *(package-level `.agents`)* | Folder that provides the bundled `.agents` content mirrored into the repository. |
| `PurviewAgentFolderCopyRetries` | `3` | Copy attempts per file before the failure is reported. |
| `PurviewAgentFolderCopyRetryDelayMilliseconds` | `500` | Base delay between attempts (a small increment is added per attempt). |
| `PurviewAgentFolderCopyFailureAsError` | `true` | When `false`, a copy that still fails after every retry is reported as a warning and the build continues. |
| `PurviewAgentSyncManifestPath` | `<repo root>/.purview/agent-sync.cache` | Overrides the change-detection manifest location. |
| `PurviewSuppressCopyRetryWarnings` | `true` | Demotes MSB3026 copy retry notices to messages. Set to `false` to see every retry attempt. |

## Opting out

To disable bundled agent folder copying in a consuming repo, set the opt-out property before
importing the SDK:

```xml
<PropertyGroup>
  <EnableAgentFolderInPackage>false</EnableAgentFolderInPackage>
</PropertyGroup>
```

## Packaging the folder

For packable projects, the SDK packs the `Sdk/.agents/**` folder into the package at `.agents/**` and
injects a `.gitignore` file into each second-level folder under `Sdk/.agents` with the content
`# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore`.
This ensures the copied folder structure remains discoverable in consuming repositories while the
content itself is ignored by Git.

| Property | Default | Description |
| -- | -- | -- |
| `PurviewAutoSdkPack` | `true` | When `true`, automatically packs the `Sdk/` folder contents into the NuGet package with the correct root-level paths. Disable this for MSBuild SDK projects. |
| `EnableAgentFolderInPackage` | `true` | Copies the bundled `.agents/**` folder from the SDK NuGet package into the consuming repository's `.agents/` folder (or `$(AgentPackDestinationFolder)/`) before build. |
| `AgentPackDestinationFolder` | `.agents` | Repo-relative destination folder that receives the copied agent folder contents when `EnableAgentFolderInPackage` is `true`. |

See [Packaging](Packaging.md) for the full `Sdk/` folder packaging rules.