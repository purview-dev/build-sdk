# Getting Started

This guide walks through the minimal setup required to adopt `Purview.BuildSdk` in a
repository. Install it once per repo — every project beneath the repo root inherits everything
automatically.

## 1. Add the SDK to `global.json`

Add `Purview.BuildSdk` to the `msbuild-sdks` section so MSBuild can resolve the SDK. The value is the
pinned SDK version — bump it deliberately, because a released version can change defaults:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Purview.BuildSdk": "2.1.0"
  }
}
```

> [!NOTE]
> The SDK can also bootstrap a `global.json` for you at the repository root when one is missing; see
> [Repository Bootstrap](Repository-Bootstrap.md). The value it writes is the restored package's version,
> else the version it was packed as (`PurviewBuildSdkVersionForGlobalJson`).

## 2. Create `Directory.Build.props` at the repo root

```xml
<Project>
  <PropertyGroup>
    <!-- Required: sets the root namespace prefix for all projects -->
    <NamespacePrefix>YourCompany</NamespacePrefix>
  </PropertyGroup>

  <Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
</Project>
```

`NamespacePrefix` is mandatory — a build error is raised if it is missing
(`ValidateRootNamespacePrefixTarget`), unless `DisableNamespacePrefixCheck=true`.

## 3. Create `Directory.Build.targets` at the repo root

```xml
<Project>
  <Import Sdk="Purview.BuildSdk" Project="Sdk.targets" />
</Project>
```

## 4. Copy `Directory.Packages.props` to the repo root

Copy [`templates/Directory.Packages.props`](https://github.com/purview-dev/build-sdk/blob/main/templates/Directory.Packages.props)
from the repository to your repo root. The templates are not packed into the NuGet package — they are
starting points held in git, so take them from there. Every package names a version. A
`PackageVersion` is a minimum rather than an exact pin — NuGet resolves it unless something in the
graph requires higher — so floating versions are not needed to stay current, and they would make a
build depend on the day it ran.

> **Note:** `ManagePackageVersionsCentrally=true` is set by the SDK. You **must** have a
> `Directory.Packages.props` at your repo root for CPM to work, even if it only contains the packages
> the SDK adds automatically.

## 5. Add `package.json` at the repo root

The product version comes from the `version` field of a repository-root `package.json`:

```json
{
  "version": "1.0.0"
}
```

In a git repository with no root `package.json` the build fails, because version detection found a
repository root but no file in it. Add one, point `RootPackageJson` at another file, or set
`UsePackageJsonVersion=false` before the import. See [Version Detection](configuration/Version-Detection.md).

## Required settings and versions

These four values decide whether the SDK behaves as documented:

| Setting | Where it goes | Notes |
| -- | -- | -- |
| `NamespacePrefix` | `Directory.Build.props`, above the import | Required; the build fails without it unless `DisableNamespacePrefixCheck=true`. |
| SDK version | `global.json` → `msbuild-sdks.Purview.BuildSdk` | Pinned to a released version. |
| Product version | repo-root `package.json` → `version` | Applied to `Version` and `PackageVersion`. `UsePackageJsonVersion=Strict` fails instead of falling back to `0.0.1`. |
| Package versions | `Directory.Packages.props` | A `PackageVersion` is a minimum, not an exact pin. |

## Setting the target framework(s)

A project that declares neither `TargetFramework` nor `TargetFrameworks` gets the SDK's default set,
`net10.0;net11.0`. To change it:

- declare `TargetFramework` (one) or `TargetFrameworks` (many) — an explicit value always wins over
  every set, in the project, a file it imports, or `Directory.Build.props`; or
- select a named set with `<PurviewTargetFrameworkSet>Supported</PurviewTargetFrameworkSet>` in
  `Directory.Build.props` or the `.csproj` (`Current`, `Latest`, `Supported`, `Broad`, `All`).

See [Target framework sets](Configuration-Reference.md#target-framework-sets).

## Next steps

- Read the policy-level guidance in [Engineering Principles](concepts/Engineering-Principles.md).
- Understand how projects are detected and named in [Project Type Detection](concepts/Project-Type-Detection.md)
  and [Project Naming Conventions](concepts/Project-Naming-Conventions.md).
- Browse every configurable property in the [Configuration Reference](Configuration-Reference.md).
- Learn how the package version is sourced in [Version Detection](configuration/Version-Detection.md).
- See how test projects are wired up in [Testing Wiring](testing/Testing-Wiring.md).
- Read how packages are produced in [Packaging](packaging/Packaging.md).
