# Code Fixes

The package ships `Purview.BuildSdk.CodeFixers.dll` for the IDE. The code-fix assembly is
added as an analyzer reference only when building inside Visual Studio (Roslyn's `CodeFixService` keys
on `Project.AnalyzerReferences`, and the command-line compiler cannot resolve
`Microsoft.CodeAnalysis.Workspaces`).

## PDS0004 — Rename to correct acronym capitalization

The code fix for `PDS0004` renames the identifier to its correct-English capitalization (for example
`ApiClient` → `APIClient`) and updates all of its references across the solution. See
[Analyzers](Analyzers.md) for the full rules and configuration options.

## PDS0002 — Sync namespace to Extensions folder structure

The code fix for `PDS0002` rewrites the file's namespace to the one its folders under `Extensions/`
imply (see [Analyzers](Analyzers.md#pds0002--extensions-namespace-rule)).

## Move extensions class

A refactoring, not tied to a diagnostic. When a static extensions class targets a single receiver type
but is not at its conventional location, **Move extensions class to conventional location** — or, when it
is already there but not conventionally named, **Rename extensions class to '…'**:

- Re-paths the file to `Extensions/<receiver namespace>/<Type>Extensions.cs`.
- Renames the class to the receiver-derived name (for example an `IServiceCollection` extension
  becomes `ServiceCollectionExtensions` and moves to `Microsoft.Extensions.DependencyInjection`).
- When a `<Type>Extensions` class already exists in the receiver's namespace it is merged into that
  class instead of creating a duplicate — every member from both classes (constants, private helpers,
  XML docs) is preserved, only members with a matching signature are skipped.
- The namespace is fixed, a `using` for the previous namespace is added to the moved file, and a
  `using` for the new namespace plus the renamed type name are applied to every other document that
  references the type, so the move compiles everywhere.

## Split extensions class

When a static extensions class targets multiple receiver types, the **Split extensions class into one
class per receiver type** refactoring:

- Splits the class into one `<ReceiverType>Extensions` class per receiver — for a generic
  `this TBuilder where TBuilder : IHostApplicationBuilder` receiver that becomes
  `HostApplicationBuilderExtensions`.
- Places each new file under `Extensions/<receiver namespace>/` so the namespace convention stays
  satisfied.
- When a `<ReceiverType>Extensions` class already exists in the receiver's namespace, the receiver's
  methods are merged into it instead of creating a duplicate file.

## Target-typed `new()`

The code fix for `PDS0003` (**Use explicit type and target-typed new**) rewrites a `var` declaration
initialised with an object creation to an explicit type with a target-typed `new()`:

```csharp
// before
var service = new Service();
// after
Service service = new();
```