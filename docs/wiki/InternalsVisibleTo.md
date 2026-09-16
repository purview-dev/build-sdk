# InternalsVisibleTo

The SDK automatically generates `[assembly: InternalsVisibleTo("…")]` attributes for every non-test
C# project. The friend assembly name is derived from the source project's resolved `$(AssemblyName)`,
so all naming modes are handled correctly:

- **Explicit `<AssemblyName>`** — if a project sets `<AssemblyName>Custom.Assembly</AssemblyName>`, the
  generated attributes use `Custom.Assembly.UnitTests`, `Custom.Assembly.IntegrationTests`, etc.
- **Default** — `AssemblyName` is `RootNamespace`-derived, so fully-qualified names are used (for
  example `Acme.MyProject.UnitTests`).
- **`EnableAssemblyNameGeneration=false`** — standard .NET behaviour: `$(MSBuildProjectName)` (for
  example `MyProject.UnitTests`).

## What is generated

Two categories of friend assemblies are generated:

1. **TestType variants** — for each defined `TestType` (`Unit`, `Integration`, `Architecture`,
   `Contract`, `Functional`, …) the SDK emits the `$(AssemblyName).{TestType}Tests` alias plus the
   `$(TargetProjectName).{TestType}Tests` and `$(MSBuildProjectName).{TestType}Tests` equivalents, so
   the attribute resolves regardless of whether naming comes from an explicit `AssemblyName`, the SDK
   default, or the raw project name.
2. **SharedTesting projects** — one per known shared testing project name (`SharedTestingFramework`,
   `SharedTestingInfrastructure`, etc.). By default (`EnableAssemblyNameGeneration=true`) with a
   `NamespacePrefix` set, these are prefixed (for example `Acme.SharedTestingFramework`); with
   `EnableAssemblyNameGeneration=false` the raw name is used.

`InternalsVisibleTo` is also generated for `DynamicProxyGenAssembly2` so Moq/NSubstitute dynamic
proxies can access internals.

## Disabling automatic InternalsVisibleTo

To disable automatic InternalsVisibleTo generation, set `DisableAutoInternalsVisibleTo=true` in your
project or `Directory.Build.props`:

```xml
<PropertyGroup>
  <DisableAutoInternalsVisibleTo>true</DisableAutoInternalsVisibleTo>
</PropertyGroup>
```