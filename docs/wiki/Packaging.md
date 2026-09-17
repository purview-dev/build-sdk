# Packaging

The SDK configures packable projects (`IsPackable=true`) for clean NuGet output and handles several
packaging workflows automatically: symbol packages, Roslyn analyzer assets, source generators, the
`Sdk/` folder, and the repository README.

## Packable project defaults

For projects where `IsPackable=true`, the SDK provides these defaults **only when the consuming
project has not supplied a value** — explicit values are always preserved:

| Property | Default | Description |
| -- | -- | -- |
| `GenerateDocumentationFile` | `true` | Emits XML documentation. |
| `IncludeSymbols` | `true` | Produces a symbol package (`false` for Roslyn components — their PDB ships inside the `.nupkg` under `analyzers/dotnet/cs/`). |
| `SymbolPackageFormat` | `snupkg` | Symbol package format. Always the modern `.snupkg`; the legacy `.symbols.nupkg` is never produced by default. |
| `PublishRepositoryUrl` | `true` | Publishes the repository URL. |
| `EmbedUntrackedSources` | `true` | Embeds untracked sources for SourceLink. |
| `DebugType` | `portable` | Ensures portable PDBs for symbol-package delivery. |
| `IncludeSource` | `true` | Includes source files in the package. |

Portable PDBs are delivered through the `.snupkg`; the normal `.nupkg` does **not** receive PDB files
unless the project explicitly opts in (for example by adding `.pdb` to
`AllowedOutputExtensionsInPackageBuildOutputFolder`).

The SDK never forces organization/package-specific metadata — `Authors`, `Company`,
`PackageLicenseExpression`, `PackageLicenseFile`, `Description`, `PackageTags`, `PackageProjectUrl`,
and repository URLs are left to the repository or individual package. `IsPackable` is not set blindly:
it defaults to `false` and only becomes `true` when a project explicitly opts in.

## Roslyn components (`IsRoslynComponent=true`)

Packable Roslyn components automatically pack the built analyzer/generator assembly **and its PDB**
into `analyzers/dotnet/cs/` (`PurviewPackAnalyzerPdb=true`). Set `PurviewPackAnalyzerPdb=false` only
when symbols are delivered another way — NuGet's `.snupkg` cannot host `analyzers/dotnet/cs` symbols.
`IncludeBuildOutput=false` keeps `lib/` empty.

Pack-time validation (`ValidateRoslynComponentCompilerSettings`) fails the pack with `PRSGD0001`–
`PRSGD0004` if the standard compiler defaults (`LangVersion`, `Nullable`, `TreatWarningsAsErrors`,
`EnforceExtendedAnalyzerRules`) are missing. Opt out with `DisableRoslynCompilerDefaultsValidation=true`.

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
| `Sdk/.agents/**` | `.agents/**` |
| `Sdk/.github/**` | `.github/**` |
| `Sdk/build/**` | `build/**` |
| `Sdk/buildTransitive/**` | `buildTransitive/**` |
| `Sdk/buildMultiTargeting/**` | `buildMultiTargeting/**` |
| `Sdk/*.md`, `Sdk/*.png`, `Sdk/*.jpg`, etc. | package root |
| everything else under `Sdk/` | `Sdk/` |

The SDK automatically adds a `.gitignore` file into each second-level folder under `Sdk/.agents` with
the content `# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore`.
This ensures the copied folder structure remains discoverable in consuming repositories while the
content itself is ignored by Git.

MSBuild SDK packages (like `Purview.BuildSdk` itself) set `PurviewAutoSdkPack=false` and
pack their `Sdk/` contents explicitly instead. External files linked beneath `Sdk/` (via `<Link>`)
are packed with the same paths as physical `Sdk/` files.

## Repository README auto-inclusion

When the repo root is discoverable (`.git` marker or CI workspace variable), the repository-root
`README.md` is packed automatically for packable projects and registered via `PackageReadmeFile` —
but only when the file exists and `PackageReadmeFile` has not been configured explicitly. The SDK
skips the auto-inclusion if a README-named file is already being packed, so no duplicate readme items
are produced. No README is required; if the file is absent the pack succeeds without readme metadata.

## Pack validation

The shared `Purview.Build` pipeline runs package validation on pack (`purview-build.json` sets
`PackValidation.RequireSymbolPackage=false` for this SDK repo). See
[Release Flow](Release-Flow.md) for the full flow.