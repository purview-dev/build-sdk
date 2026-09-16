# Code Fixes

The package ships `Purview.DotNetProjectSdk.CodeFixers.dll` for the IDE. The code-fix assembly is
added as an analyzer reference only when building inside Visual Studio (Roslyn's `CodeFixService` keys
on `Project.AnalyzerReferences`, and the command-line compiler cannot resolve
`Microsoft.CodeAnalysis.Workspaces`).

## PDS0004 — Rename to correct acronym capitalization

The code fix for `PDS0004` renames the identifier to its correct-English capitalization (for example
`ApiClient` → `APIClient`) and updates all of its references across the solution. See
[Analyzers](Analyzers.md) for the full rules and configuration options.

## PDS0002 — Fix extensions namespace

When a static extensions class is not at its conventional location (or is at that location but not
conventionally named), the **Move extensions class to conventional location** refactoring:

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

The code fix for `PDS0003` rewrites a `var` declaration with a target-typed object creation
initializer to use an explicit type with `new()`:

```csharp
// before
var service = new Service();
// after
Service service = new();
```