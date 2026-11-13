# Version Detection

The SDK reads the `version` field from a repository-root `package.json` and applies it to both
`Version` and `PackageVersion` automatically. When no repository root can be discovered at all
(non-strict mode), it silently falls back to `0.0.1`.

## Behaviour

When `UsePackageJsonVersion=true` (the default) or `UsePackageJsonVersion=Strict` (compared
case-insensitively), the SDK resolves the file during the `Sdk.props` import:

1. **Explicit path** — if `RootPackageJson` is set, reads that file directly. A relative path resolves
   from the project directory, whatever directory the build runs from — so a value in
   `Directory.Build.props` resolves per project; use `$(MSBuildThisFileDirectory)package.json` there.
2. **Auto-discovery** — otherwise it finds the repository root (`RepoRoot`), and reads `package.json`
   from there. The first of these that gives an answer wins:
   1. a `RepoRoot` you set before the import;
   2. the repository root recorded in the [cache](#caching) for this project;
   3. the CI workspace variables `GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY`,
      `BUILD_REPOSITORY_LOCALPATH` and `CI_PROJECT_DIR`, in that order, when the folder exists;
   4. the nearest folder above the project holding a `.git` file or folder;
   5. the nearest folder above the project holding a `package.json`.

The value of the first `"version": "..."` pair in the file is applied to both `Version` and
`PackageVersion`. It is a text match, not a JSON parse, so keep the top-level `version` field ahead of
any nested object that also has one.

`ValidatePackageJsonVersion` runs before `BeforeBuild`, `GenerateNuspec` and `Pack`, and fails the build
when:

- `RootPackageJson` was set explicitly and that file does not exist;
- a repository root was discovered but there is no `package.json` in it — so a git repository without a
  root `package.json` fails the build until you add one, set `RootPackageJson`, or set
  `UsePackageJsonVersion=false`;
- a `package.json` was found but no `version` could be read from it;
- `UsePackageJsonVersion=Strict` and no source could be discovered at all (no explicit
  `RootPackageJson` and no repository root). In non-strict mode that case silently keeps the `0.0.1`
  default.

`UsePackageJsonVersion=false` turns all of this off; `Version` keeps whatever was set elsewhere, or
`0.0.1`.

## Caching

Auto-discovery results are cached under the user's temporary directory, in
`Purview.BuildSdk/VersionDetection/`, one file per project directory, so later evaluations can skip the
root search. `VersionDetectionCacheFile` overrides the file. The cache records the repository root, the
`package.json` path and the version, and is written by `WriteVersionDetectionCache` before
`CollectPackageReferences`, `PrepareForBuild`, `GenerateNuspec` and `Pack`. It is only used when
`RootPackageJson` is not set explicitly, and the version itself is always re-read from the cached
`package.json`, so editing the file is picked up straight away. Enable or disable it with
`EnableVersionDetectionCache` (default `true`).

## Logging

Version detection logging is disabled by default. Set `VersionDetectionLogEnabled=true` to emit a
high-importance message, from `ValidatePackageJsonVersion`, showing the detected package version and
the file it came from. It can be set anywhere, because only the target reads it.

## Important — set before the import

`UsePackageJsonVersion`, `RootPackageJson`, `RepoRoot`, `EnableVersionDetectionCache` and
`VersionDetectionCacheFile` must be set **before** the
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
