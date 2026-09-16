# Assembly Name Generation

By default (`EnableAssemblyNameGeneration=true`), the SDK treats `RootNamespace` as the canonical
public name: `AssemblyName` and `PackageId` both default to the fully evaluated `RootNamespace` — or,
when suffix-stripping removed a segment of the logical project name, to the full logical project name
so assemblies stay distinct. The defaults are applied during `Sdk.props` evaluation — before the
Microsoft SDK computes `TargetName` and before the project body — so compilation, output paths,
project references, restore, and packing all agree on the same identities. Set
`EnableAssemblyNameGeneration=false` **before the SDK import** to opt out and fall back to standard
.NET behaviour (`$(MSBuildProjectName)`).

## Resolved identities

| Project name | `NamespacePrefix` | `RootNamespace` | Resolved `AssemblyName` / `PackageId` |
| -- | -- | -- | -- |
| `Api` | `Acme` | `Acme.Api` | `Acme.Api` |
| `Acme.Api` | `Acme` | `Acme.Api` | `Acme.Api` (no double-prefix) |
| `Core.Infrastructure` | `Acme` | `Acme.Infrastructure` | `Acme.Core.Infrastructure` (`.Core` stripped from the namespace only) |
| `Shared` | `Acme` | `Acme` | `Acme.Shared` (full logical name, so it stays distinct) |
| `ServiceDefaults` | `Acme` | `Acme` | `Acme.ServiceDefaults` (full logical name, so it stays distinct) |
| `Acme` | `Acme` | `Acme` | `Acme` |

Explicitly setting `<RootNamespace>` before the SDK import always wins: `AssemblyName`/`PackageId`
follow that override instead of re-appending a stripped suffix.

Test projects keep their detected suffix: `Api.UnitTests` → `AssemblyName`/`PackageId` =
`Acme.Api.UnitTests`, while `RootNamespace` remains `Acme.Api`.

Explicit `<AssemblyName>` or `<PackageId>` in a `.csproj` (or `Directory.Build.props`) always takes
precedence. Because the defaults run before the project body, project-authored values set in the body
are evaluated later and win.

> **Note:** set `EnableAssemblyNameGeneration=false` **before** the SDK import (for example in
> `Directory.Build.props`) — it is consumed during `Sdk.props` evaluation.

## Namespace stripping

Certain suffixes are automatically stripped from `RootNamespace` to avoid awkward namespace names like
`Acme.MyProject.Core.Something`.

Stripped suffixes: `Core`, `EF`, `Shared`, `ClientShared`, `ServiceDefaults`, and all shared and
shared-testing project names.

See [Project Naming Conventions](Project-Naming-Conventions.md) and
[InternalsVisibleTo](InternalsVisibleTo.md) for related naming behaviour.