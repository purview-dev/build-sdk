# Property Schema and IntelliSense

Purview.BuildSdk describes every property it defines in an
[MSBuild Editor](https://marketplace.visualstudio.com/items?itemName=mhutch.msbuildeditor) sidecar
schema, `Sdk.props.buildschema.json`, packed beside `Sdk.props`. With the extension installed, a
`.csproj` or `Directory.Build.props` that imports the SDK gets completion for the SDK's properties,
their descriptions, their defaults, and — for a property such as `TestingFramework` — the allowed
values.

## Getting IntelliSense

1. Install **MSBuild Editor** in Visual Studio from the Marketplace link above.
2. Restore the repository once, so the SDK package is in the NuGet cache.
3. Open a project file and start typing a property name — `Purview`, `Testing`, `Namespace`.

Nothing else is needed. The editor loads `<imported file>.buildschema.json` for every file a project
imports, SDKs resolved through `global.json` `msbuild-sdks` included. Without the extension the file
is inert: it changes nothing about a build.

## What the schema says, and how it stays right

The schema is the **canonical** description of a property. Each entry has:

- a **description**, which says when a property must be set before the `Sdk.props` import and, when a
  property has no default, why (`No default: detected from the project`);
- a **type** — `bool`, `int`, a path, a TFM list, or an enumeration of the values the SDK acts on;
- a **default**, when the property has one.

Every one of those claims is checked by this repository's test suite (`PropertySchemaTests`), which
fails when:

- the SDK defines a public property the schema does not describe, or the schema describes one that
  nothing defines or reads;
- a `CompilerVisibleProperty` has no entry, or an SDK analyzer reads a `build_property.*` that is not
  exported;
- a stated default differs from what a plain class library evaluates to;
- a description says "set it before the import" and `Sdk.props` does not read the property — or the
  other way round;
- the [Configuration Reference](../Configuration-Reference.md) or the README omits a property, or states a
  different default;
- the file fails validation against the MSBuild Editor meta-schema (draft-04);
- the packed `.nupkg` lacks `Sdk/Sdk.props.buildschema.json`.

`just properties-check` runs exactly those checks.

## For package authors: `ValidatePurviewPropertySchema`

A package that ships its own `.props`/`.targets` — under `Sdk/build/` or `Sdk/buildTransitive/`, which
`PurviewAutoSdkPack` packs to `build/` and `buildTransitive/` — can ship its own sidecar and have the
SDK check it:

```xml
<PropertyGroup>
  <EnablePurviewPropertySchemaValidation>true</EnablePurviewPropertySchemaValidation>
  <!-- exports the package does not own -->
  <PurviewPropertySchemaIgnoredProperties>RootNamespace;Version</PurviewPropertySchemaIgnoredProperties>
</PropertyGroup>
```

The check then runs before `Pack`, and on demand:

```bash
dotnet msbuild src/Acme.Kit/Acme.Kit.csproj -t:ValidatePurviewPropertySchema
```

| Id | Meaning |
| -- | -- |
| `PRPSC0001` | A `CompilerVisibleProperty` that no sidecar describes. |
| `PRPSC0002` | A sidecar entry no `.props`/`.targets` file defines, reads or exports. |
| `PRPSC0003` | A sidecar that is not valid JSON, or has no `"properties"` object. |
| `PRPSC0004` | A sidecar with no `.props`/`.targets` beside it, so MSBuild Editor never loads it. |

It is off by default and never part of an ordinary build or design-time build.

### Where the sidecar goes

The name is the **full** name of the file the consumer imports plus `.buildschema.json`, in the same
folder: `build/Acme.Kit.props` → `build/Acme.Kit.props.buildschema.json`. `Acme.Kit.buildschema.json`
is never loaded (`PRPSC0004`). One sidecar per package is enough — the editor applies every loaded
sidecar project-wide — but a package that ships the same file under both `build/` and
`buildTransitive/` needs it in both.

A sidecar is a new file in the package. A repository built with Purview.Build validates every package's
contents against `PackValidation.RequiredContent` in `.config/purview-build.json`, and a packed file that
list does not name fails the pipeline as *undeclared content*. Add each sidecar to its package's entry,
beside the file it describes.

The `purview-buildsdk-msbuild-properties` agent skill walks through adding, renaming, deprecating and
removing a property, and the schema format.
