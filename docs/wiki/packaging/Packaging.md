# Packaging

The SDK configures packable projects (`IsPackable=true`) for clean NuGet output and handles several
packaging workflows automatically: symbol packages, Roslyn analyzer assets, source generators, the
`Sdk/` folder, and the repository README.

## Packable project defaults

For projects where `IsPackable=true`, a packed project ends up with these values **unless the
consuming project supplies its own** — explicit values are always preserved. Most are defaulted for
every project in `Props/Defaults.props`; `DebugType=portable` is the only one set for packable
projects alone:

| Property | Default | Description |
| -- | -- | -- |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. |
| `IncludeSymbols` | `true` | Produces a symbol package (`false` for an analyzer-only Roslyn component, `IsRoslynComponentOnly=true` — its PDB ships inside the `.nupkg` under `analyzers/dotnet/cs/`). |
| `SymbolPackageFormat` | `snupkg` | Symbol package format. Always the modern `.snupkg`; the legacy `.symbols.nupkg` is never produced by default. |
| `PublishRepositoryUrl` | `true` | Publishes the repository URL. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources for SourceLink. |
| `DebugType` | `portable` | Ensures portable PDBs for symbol-package delivery. |
| `IncludeSource` | `false` | Source files are not packed into the symbol package; SourceLink is how sources reach the debugger. |

A SourceLink provider package (`SourceLinkPackageName`) is added to every non-test project, and
`PublishRepositoryUrl=true` writes the repository URL and commit into the `.nuspec`. See
[SourceLink](SourceLink.md).

Portable PDBs are delivered through the `.snupkg`; the normal `.nupkg` does **not** receive PDB files
unless the project explicitly opts in (for example by adding `.pdb` to
`AllowedOutputExtensionsInPackageBuildOutputFolder`).

The SDK never forces organization/package-specific metadata — `Authors`, `Company`,
`PackageLicenseExpression`, `PackageLicenseFile`, `Description`, `PackageTags`, `PackageProjectUrl`,
and repository URLs are left to the repository or individual package. `IsPackable` is not set blindly:
it defaults to `false` and only becomes `true` when a project explicitly opts in.

## Roslyn components (`IsRoslynComponent=true`)

Packable, single-targeted Roslyn components automatically pack the built analyzer/generator assembly
**and its PDB** into `analyzers/dotnet/cs/` (`PurviewPackAnalyzerPdb` defaults to `true` for an
analyzer-only component). The PDB is skipped when `DebugType` is `embedded` or `none`. Set
`PurviewPackAnalyzerPdb=false` only when symbols are delivered another way — NuGet's `.snupkg` cannot
host `analyzers/dotnet/cs` symbols. For an analyzer-only component (`IsRoslynComponentOnly`, `true` by
default for a Roslyn component), `IncludeBuildOutput=false` keeps `lib/` empty and `NU5128` is
suppressed.

Pack-time validation (`ValidateRoslynComponentCompilerSettings`, `BeforeTargets="Pack"`) fails the
pack when a standard compiler default has been removed or turned off: `PRSGD0001` (no `LangVersion`),
`PRSGD0002` (no `Nullable`), `PRSGD0003` (`TreatWarningsAsErrors` not `true`) and `PRSGD0004`
(`EnforceExtendedAnalyzerRules` not `true`). Opt out with
`DisableRoslynCompilerDefaultsValidation=true`.

Roslyn development dependencies (`Microsoft.CodeAnalysis.CSharp`,
`Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.CodeAnalysis.Analyzers`) default to
`PrivateAssets="all"`, and `Microsoft.CodeAnalysis.Analyzers` also gets the analyzer
`IncludeAssets`, so they never leak into the packed nuspec.

## Automatic source generator packaging

`PackProjectReferencedSourceGenerators=true` (default) automatically packs analyzer
`ProjectReference` outputs and their runtime dependencies under `analyzers/dotnet/cs/`. Analyzer
project references use the `GetSourceGeneratorAnalyzerFiles` target (set automatically when a
`ProjectReference` has `OutputItemType=Analyzer` and `ReferenceOutputAssembly=false`); runtime
dependencies are declared as `SourceGeneratorRuntimeDependency` items and copied beside the generator.
Duplicates across analyzer projects are deduplicated by package path so the pack never fails on
colliding `analyzers/dotnet/cs` targets.

Set `PackProjectReferencedSourceGenerators=false` to opt out, or `Pack="false"` on an individual
`ProjectReference` to exclude only that generator.

## Automatic `Sdk/` folder packaging (`PurviewAutoSdkPack`)

For packable projects, `PurviewAutoSdkPack` (default `true`) automatically adds `Sdk/**/*` as `None`
items with `Pack="true"` and `Visible="true"`, mapping each file to the correct location in the
package:

| Source path | Package path |
| -- | -- |
| `Sdk/.github/**` | `.github/**` |
| `Sdk/build/**` | `build/**` |
| `Sdk/buildTransitive/**` | `buildTransitive/**` |
| `Sdk/buildMultiTargeting/**` | `buildMultiTargeting/**` |
| `Sdk/*.md`, `*.png`, `*.jpg`, `*.jpeg`, `*.gif`, `*.svg` | package root |
| everything else under `Sdk/` | `Sdk/` |

There is no special mapping for `Sdk/.agents/**`: like any other folder it lands under `Sdk/`, and the
SDK writes no `.gitignore` files. With `PurviewAutoSdkPack=true` the SDK also defaults
`NoDefaultExcludes=true`, so dotfiles such as `Sdk/.editorconfig` are packed.

MSBuild SDK packages (like `Purview.BuildSdk` itself) set `PurviewAutoSdkPack=false` and
pack their `Sdk/` contents explicitly instead. External files linked beneath `Sdk/` (a `None` item whose `Link` starts with `Sdk/`) are packed by
`IncludeLinkedSdkFiles` by the same table as physical files, so a linked `Sdk/README.md` packs as
`README.md` and a linked `Sdk/Props/X.props` as `Sdk/Props/X.props`. `SdkFolderPackingTests` packs both
kinds side by side to keep them identical.

## Repository README auto-inclusion

For a packable project with no explicit `PackageReadmeFile`, the SDK registers
`PackageReadmeFile=README.md` and packs a README to the package root (`IncludeRepoReadmeInPackage`).
The project's own `README.md`, beside its `.csproj`, wins; otherwise the `README.md` at `RepoRoot` is
used. `RepoRoot` comes from a CI workspace variable (`GITHUB_WORKSPACE`, `BUILD_SOURCESDIRECTORY`,
`BUILD_REPOSITORY_LOCALPATH`, `CI_PROJECT_DIR`), else the nearest `.git` or `package.json` above the
project. The SDK skips the auto-inclusion if a README-named file is already being packed (including a
linked `Sdk/README.md`), so no duplicate readme items are produced. No README is required; if neither
file exists the pack succeeds without readme metadata.

## Pack validation

The shared `Purview.Build` pipeline validates each package after pack, configured by
`.config/purview-build.json` `PackValidation`: for this repository `RequireSymbolPackage=false`,
`RequiredCompilerFlags` (`optimization=release`) and `RequiredContent`, the files each package must
contain. See [Release Flow](../development/Release-Flow.md) for the full flow.
