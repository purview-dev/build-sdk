# Getting Started

This guide walks through the minimal setup required to adopt `Purview.BuildSdk` in a
repository. Install it once per repo — every project beneath the repo root inherits everything
automatically.

## 1. Add the SDK to `global.json`

Add `Purview.BuildSdk` to the `msbuild-sdks` section so MSBuild can resolve the SDK:

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Purview.BuildSdk": "1.0.0"
  }
}
```

> [!NOTE]
> The SDK can also bootstrap a `global.json` for you at the repository root when one is missing; see
> [Repository Bootstrap](Repository-Bootstrap.md).

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

Copy `templates/Directory.Packages.props` from this package to your repo root. All package versions
default to `*` (latest at restore). Pin any package by replacing `*` with a specific version.

> **Note:** `ManagePackageVersionsCentrally=true` is set by the SDK. You **must** have a
> `Directory.Packages.props` at your repo root for CPM to work, even if it only contains the packages
> the SDK adds automatically.

## Next steps

- Understand how projects are detected and named in [Project Type Detection](Project-Type-Detection.md)
  and [Project Naming Conventions](Project-Naming-Conventions.md).
- Browse every configurable property in the [Configuration Reference](Configuration-Reference.md).
- Learn how the package version is sourced in [Version Detection](Version-Detection.md).
- See how test projects are wired up in [Testing Wiring](Testing-Wiring.md).
- Read how packages are produced in [Packaging](Packaging.md).