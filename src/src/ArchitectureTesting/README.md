# Purview.Architecture.Testing

Enforces a repository's project dependency and naming policy as TUnit tests, so the rules are
defined once rather than copied into every repository's test project.

## Install

```bash
dotnet add package Purview.Architecture.Testing
```

## Adopt

Two things. A policy at `.config/architecture-policy.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/purview-dev/build-sdk/main/architecture-policy.schema.json",
  "version": 1,
  "sourceRoots": ["src"],
  "responsibilities": [
    {
      "name": "Core",
      "aliases": ["Domain"],
      "category": "domain",
      "purpose": "The domain model, its invariants and its rules.",
      "mayDependOn": [],
      "mustNotDependOn": ["Application", "Persistence"]
    }
  ]
}
```

And one derived test class:

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests;
```

> **`[InheritsTests]` is required.** TUnit does not walk base types during discovery, so without it
> the class compiles, the run is green and nothing is enforced. TUnit's analyzer reports `TUnit0030`
> — treat it as an error.

## The rules

| Rule | Catches |
| -- | -- |
| `graphDiscovery` | An empty graph, which satisfies every other rule |
| `naming` | A project name matching no responsibility or alias |
| `discouragedName` | A project named after a category, so nothing can be kept out of it |
| `dependencyMatrix` | A reference the matrix forbids or does not permit |
| `siblingIndependence` | A qualified implementation referencing a qualified sibling |
| `compositionBoundary` | A non-host project referencing Composition |
| `forbiddenPackage` | A package matching a forbidden prefix for that responsibility |
| `cycle` | A dependency cycle, reported readably |
| `testReference` | Production code referencing a test project |
| `waiverIntegrity` | A waiver that is stale, unjustified or malformed |

The policy is validated against its schema first, because a policy that does not match its schema
may be silently missing a rule.

## Waivers over skips

```json
"waivers": [
  {
    "project": "Legacy.Utilities",
    "mapsTo": "Shared",
    "rules": ["naming"],
    "reason": "Predates the catalogue; renaming breaks a published package id.",
    "reviewedOn": "2026-01-14"
  }
]
```

`SkipRule` removes a rule for the whole repository with no record. A waiver is per project, carries
a reason and a review date, keeps every other rule in force, and is itself checked. `mapsTo` maps an
established project onto the responsibility it actually has, so only the *name* is excused.

## Design

Three layers, deliberately separated: `ArchitecturePolicy`/`ProjectGraph` read the policy and the
`.csproj` graph with no MSBuild or test-framework types; `ArchitecturePolicyEvaluator` holds the
rules and takes an in-memory graph, so a rule is testable as a three-project fixture;
`PurviewArchitectureTests` is the TUnit wrapper with one `[Test]` per rule.

Project files are read rather than compiled assemblies, because a declared `ProjectReference` is
what a reviewer adds and what the policy governs.

---

Part of [Purview.BuildSdk](https://github.com/purview-dev/build-sdk) · [purview.dev](https://purview.dev/projects/build-sdk/)
