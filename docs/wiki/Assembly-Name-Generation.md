# Assembly Name Generation

This page explains the identity-generation rules referenced by
[Engineering Principles](Engineering-Principles.md).

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

The built-in stripping set is broader than the common examples and includes:
`ServiceDefaults`, `Infrastructure`, `Abstractions`, `ClientShared`, `DataAccess`,
`Extensions`, `Contracts`, `Framework`, `Utilities`, `Helpers`, `Library`, `Common`,
`Shared`, `Infra`, `Utils`, `Core`, `Host`, `Util`, `Lib`, `EF`, plus the known shared and
shared-testing project names (`Shared*`, `SharedTesting*`).

`Persistence`, `AppHost` and `Data` are deliberately absent. They read as layer noise but are
routinely the whole point of the project — an `Acme.Billing.Data` assembly collapsed onto
`Acme.Billing` loses the distinction its sources declare. Add one with
`<NamespaceRemoveSuffix Include="Data" />` if a repository wants it collapsed.

**Roslyn component names are deliberately absent.** `SourceGenerator`, `SourceGenerators`,
`SourceGeneration`, `Generators`, `Analyzers`, `CodeFixers` and `CodeFixes` are part of a component's
identity rather than noise: a generator, its analyzers and its code fixes are separate assemblies that
each need their own namespace. Stripping them collapses that distinction and collides with the namespace
the sources actually declare, which produced `IDE0130` across every Roslyn-component repository
consuming this SDK. Add one back per project with `<NamespaceRemoveSuffix Include="Analyzers" />` if a
repository really does want it collapsed.

### Keeping suffixes

The strip list is authored as the `NamespaceRemoveSuffix` item type. To keep a suffix the SDK would
otherwise strip, remove the matching entry — this **must come after the `Sdk.props` import**, because
the items are declared there and only exist to be removed once the import has evaluated:

```xml
<Import Sdk="Purview.BuildSdk" Project="Sdk.props" />

<ItemGroup>
  <!-- Keep the full Abstractions and Contracts namespaces. -->
  <NamespaceRemoveSuffix Remove="Abstractions" />
  <NamespaceRemoveSuffix Remove="Contracts" />
</ItemGroup>
```

This is useful where the layer suffix is genuinely part of the public shape — a published
`Acme.Payments.Abstractions` package whose consumers expect to `using Acme.Payments.Abstractions;`,
rather than finding its interfaces in `Acme.Payments`.

**Only suffixes present in the built-in list need removing**; a suffix the SDK does not ship is already
kept as-is. That is why an analyser or source-generator repository that names projects after the role —
`Purview.ZodSharp.SourceGenerators`, `Purview.ZodSharp.CodeFixers`, `Purview.ZodSharp.CodeFixes` — needs
no `Remove` at all. Those roles survive in the namespace by default, and a `Remove` for them is a
harmless no-op rather than the thing keeping them.

## Assembly identifier class

Architecture tests need a handle on each assembly under test. Matching one by name
(`Assembly.Load("Acme.Billing")`) is a string that no compiler checks, so a rename silently empties
the test instead of failing it.

`GenerateAssemblyIdentifierClassTarget` writes an empty marker class into the intermediate output for
every production C# project, so the reference becomes a `typeof`:

```csharp
// <auto-generated />

/// <summary>
/// Identifies the <c>Acme.Billing</c> assembly to architecture tests.
/// </summary>
public static partial class BillingAssembly
{
}
```

```csharp
var assembly = typeof(BillingAssembly).Assembly;
```

The class name comes from `$(MSBuildProjectName)` with the `NamespacePrefix` and every remaining dot
removed, suffixed with `Assembly` — `Acme.Billing.Core.Abstractions` and `Core.Abstractions` both
produce `CoreAbstractionsAssembly`. Set `AssemblyIdentifierClassName` to choose a different name, and
`AutoGeneratedAssemblyIdentifierFile` to change the generated file name.

It is emitted in the global namespace deliberately: a test project that references several assemblies
should not need a `using` per marker. Being `partial`, a project can add members to it.

Test and shared-testing projects do not get one — the test assembly is the one doing the asserting, so
it has nothing to identify. Set `DisableGenerateAssemblyIdentifierClass=true` to opt a production
project out.

See [Project Naming Conventions](Project-Naming-Conventions.md) and
[InternalsVisibleTo](InternalsVisibleTo.md) for related naming behaviour.
