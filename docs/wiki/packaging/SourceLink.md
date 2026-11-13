# SourceLink

SourceLink lets a debugger step into a NuGet package's source: the PDB carries a mapping from the
package's own paths to the repository's file-content URLs, and the package manifest carries the
repository URL and the exact commit. The SDK wires it up by default, so the one decision a repository
has to make is **which provider package matches its host** — GitHub, Azure DevOps, GitLab, Bitbucket, or
something self-hosted.

It also matters beyond debugging. The `<repository url="…" commit="…" />` element the provider writes
(via `PublishRepositoryUrl`) is what lets other Purview tooling — the agent skills feature in particular
— pin a package to the exact commit it was built from.

## What the SDK does automatically

- Adds `SourceLinkPackageName` (default **`Microsoft.SourceLink.GitHub`**) as a `PackageReference` with
  `PrivateAssets="all"` to **every non-test project**, not only packable ones. Test and shared-testing
  projects are excluded, because their symbols are never published.
- `PublishRepositoryUrl=true` — the repository URL and commit land in the package's `.nuspec`.
- `EmbedUntrackedSources=true` and `DebugType=portable` on packable projects, so the PDB is portable and
  SourceLink has a source for files the compiler generated or that are not tracked.
- `ContinuousIntegrationBuild` is set to `true` only when a real CI environment variable is `true` —
  `CI`, `GITHUB_ACTIONS` or `TF_BUILD`. A local build is deliberately **not** treated as CI, because
  SourceLink's CI-mode dirty-repository checks would otherwise fail a local `TreatWarningsAsErrors`
  build. Azure Pipelines sets `TF_BUILD`; GitHub Actions sets `GITHUB_ACTIONS`.
- Roslyn components get the same treatment (`EmbedUntrackedSources=true`), so shipped analyzer PDBs are
  source-linked too.

## Choosing a provider for your host

Set `SourceLinkPackageName` in `Directory.Build.props`, above the `<Import>`, so every project uses the
same provider. A value in a `.csproj` also works: the SDK reads it only where it adds the package
reference, and MSBuild evaluates that after the project body.

| Repository host | `SourceLinkPackageName` | Extra configuration |
| -- | -- | -- |
| GitHub (github.com, GitHub Enterprise) | `Microsoft.SourceLink.GitHub` *(default)* | none |
| Azure DevOps Services / Azure Repos (`dev.azure.com`, `*.visualstudio.com`) | `Microsoft.SourceLink.AzureRepos.Git` | none |
| Azure DevOps Server / TFS (on-premises) | `Microsoft.SourceLink.AzureDevOpsServer.Git` | a `SourceLinkAzureDevOpsServerGitHost` item — the virtual directory cannot be inferred |
| GitLab (12.0+) | `Microsoft.SourceLink.GitLab` | none; for older GitLab add `SourceLinkGitLabHost` with `Version` |
| Bitbucket Cloud | `Microsoft.SourceLink.Bitbucket.Git` | none; for Bitbucket Server / Data Center older than 4.7 add `SourceLinkBitbucketGitHost` with `Version` |
| anything else / self-hosted git | *(none built in)* | add your own source-control and provider packages, or opt out |

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <NamespacePrefix>Acme</NamespacePrefix>
  <SourceLinkPackageName>Microsoft.SourceLink.AzureRepos.Git</SourceLinkPackageName>
</PropertyGroup>

<Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
```

### Azure DevOps

- **Azure DevOps Services** (`https://dev.azure.com/…` or `https://<org>.visualstudio.com/…`): reference
  `Microsoft.SourceLink.AzureRepos.Git`, and nothing else. The provider reads the `origin` remote and
  derives the content URLs itself.
- **Azure DevOps Server / TFS (on-premises)**: reference `Microsoft.SourceLink.AzureDevOpsServer.Git`
  **and list the host**, because the virtual directory cannot be derived from the repository URL:

  ```xml
  <ItemGroup>
    <SourceLinkAzureDevOpsServerGitHost Include="tfs.example.com" VirtualDirectory="tfs" />
  </ItemGroup>
  ```

  `Include` is the domain, optionally with a port (`tfs.example.com:8080`). A path such as
  `tfs.example.com/tfs` is rejected by the provider's own validation ("not a valid domain name"), so the
  virtual directory goes in its own attribute.
- Azure Pipelines sets `TF_BUILD`, so `ContinuousIntegrationBuild` turns on by itself there.

## Central Package Management

The SDK sets `ManagePackageVersionsCentrally=true`, so a provider package needs a `PackageVersion` entry
in the repository's `Directory.Packages.props`. The shipped
[`templates/Directory.Packages.props`](https://github.com/purview-dev/build-sdk/blob/main/templates/Directory.Packages.props)
already declares the three most common providers:

```xml
<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="$(SourceLinkVersion)" />
<PackageVersion Include="Microsoft.SourceLink.AzureRepos.Git" Version="$(SourceLinkVersion)" />
<PackageVersion Include="Microsoft.SourceLink.GitLab" Version="$(SourceLinkVersion)" />
```

Add `Microsoft.SourceLink.AzureDevOpsServer.Git` (or `Microsoft.SourceLink.Bitbucket.Git`) yourself when
you need one of those. Selecting a provider you have not declared fails the restore with `NU1010`,
because the SDK adds it as an ordinary `PackageReference`.

## Opting out

```xml
<PropertyGroup>
  <DisableSourceLink>true</DisableSourceLink>
</PropertyGroup>
```

`DisableSourceLink=true` stops the SDK from adding the package at all. Set it in `Directory.Build.props`
for the whole repository, or in a single `.csproj` — the SDK reads it only in the condition on the
package reference, which MSBuild evaluates after the project body. Setting
`SourceLinkPackageName` to an empty value does **not** opt out: the SDK fills an empty value with the
GitHub default.

To keep the package's targets but skip the source mapping, set `EnableSourceLink=false` — that is the
provider package's own switch.

## Other and self-hosted hosts

SourceLink is provider-based: a *source-control* package (`Microsoft.Build.Tasks.Git`) plus a *provider*
package that maps the repository URL to content URLs and sets `EnableSourceLink=true`. When your host has
no official provider, either reference a third-party provider package (and point `SourceLinkPackageName`
at it), or bring both packages in yourself with `DisableSourceLink=true`, so the SDK does not add the
GitHub default alongside them.

## See also

- [Packaging](Packaging.md) — symbol packages and portable PDBs
- [Configuration Reference](../Configuration-Reference.md) — the full property list
- [dotnet/sourcelink](https://github.com/dotnet/sourcelink) — upstream documentation and the provider list
