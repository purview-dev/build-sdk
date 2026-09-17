# Version Detection

The SDK reads the `version` field from a repository-root `package.json` and applies it to both
`Version` and `PackageVersion` automatically. When no version source can be resolved (non-strict mode),
it silently falls back to `0.0.1`.

## Behaviour

When `UsePackageJsonVersion=true` (the default) or `UsePackageJsonVersion=Strict`, the SDK:

1. **Explicit path** — if `RootPackageJson` is set, reads that file directly.
2. **Auto-discovery** — otherwise, locates the repo root from CI workspace variables
   (`GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY`, `BUILD_REPOSITORY_LOCALPATH`, `CI_PROJECT_DIR`),
   then by walking up from the project directory looking for a `.git` marker or a `package.json`, and
   reads `package.json` from there.

The extracted `version` field is applied to both `Version` and `PackageVersion`. A build error is
raised when a `package.json` was resolved but can't be read, or when it contains no `version` field.
With `UsePackageJsonVersion=Strict`, the build also fails when no package.json source can be
discovered at all (for example, no explicit `RootPackageJson` and no discoverable `.git` marker or CI
workspace variable); in non-strict mode that case silently falls back to the `0.0.1` default.

## Caching

Version detection results are cached locally under the user's temporary directory
(`%TEMP%\Purview.BuildSdk\VersionDetection`, platform equivalent elsewhere) so repeated
evaluations don't re-scan the filesystem. Enable or disable with `EnableVersionDetectionCache`
(default `true`).

## Logging

Version detection logging is disabled by default. Set `VersionDetectionLogEnabled=true` to emit a
high-importance message showing the detected package version.

## Important — set before the import

Both `UsePackageJsonVersion` and `RootPackageJson` must be set **before** the
`<Import Sdk="Purview.BuildSdk" Project="Sdk.props" />` line in your `Directory.Build.props`.
The version logic runs during that import and cannot see properties set afterwards (for example in
individual `.csproj` files).

```xml
<Project>
  <PropertyGroup>
    <NamespacePrefix>Acme</NamespacePrefix>
    <!-- Set here, before the import -->
    <RootPackageJson>$(MSBuildThisFileDirectory)package.json</RootPackageJson>
  </PropertyGroup>

  <Import Sdk="Purview.BuildSdk" Project="Sdk.props" />
</Project>
```