# Agent Folder

`Purview.DotNetProjectSdk` ships bundled agent content (skills, prompts, agents) under `.agents/**`
in the NuGet package. During build, the SDK copies it into the consuming repository's `.agents/`
folder by default so compatible coding agents can discover repository-aware guidance automatically.

## How the copy works

- `EnableAgentFolderInPackage=true` (default) copies the bundled `.agents/**` folder from the SDK
  NuGet package into `$(AgentPackDestinationFolder)/` (default `.agents`) in the consuming repository
  **before build**.
- The destination root is resolved from `RepoRoot` (auto-discovered repo root), an `AGENTS.md` walk-up,
  or SourceLink source roots.
- The copy is also performed for bundled `.agents` folders shipped by **any** restored NuGet package
  that used `PurviewAutoSdkPack` (read from `project.assets.json`), not just this SDK.
- `SkipUnchangedFiles=true` keeps repeated builds cheap.

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