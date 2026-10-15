---
name: purview-buildsdk-architecture-tests
description: Adopt or debug Purview.Architecture.Testing, the package that enforces a repository's project dependency and naming policy as TUnit tests. Use when adding architecture tests to a repository, writing or changing .config/architecture-policy.json, recording a waiver, interpreting a dependency-matrix, cycle, sibling-independence or naming failure, or when the architecture tests pass but enforce nothing.
license: MIT
metadata:
  package: Purview.Architecture.Testing
  repository: purview-dev/build-sdk
---

# Architecture tests with Purview.Architecture.Testing

The package defines the rules once so a policy is not copied into every repository's test project.

## Adoption is two things

**1. A policy file** at `.config/architecture-policy.json`:

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

**2. One derived test class:**

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests;
```

## `[InheritsTests]` is load-bearing

TUnit does **not** walk base types during discovery. Without the attribute the class compiles, the
run is green, and nothing is enforced — the worst failure this package can have.

TUnit's analyzer reports `TUnit0030` when it is missing. Treat it as an error, not a suggestion.

If the architecture tests "pass" but you cannot name which rule ran, check this first.

## The rules

| Rule id | Test | What it catches |
| -- | -- | -- |
| `graphDiscovery` | `Repository_GivenItsSourceRoots_YieldsProjectsToCheck` | An empty graph — which satisfies every other rule |
| `naming` | `ProductionProjects_GivenTheirNames_MapToAResponsibility` | A project name matching no responsibility or alias |
| `discouragedName` | `ProductionProjects_GivenTheirNames_AvoidCatchAllNames` | A project named after a category, so nothing can be kept out of it |
| `dependencyMatrix` | `ProductionProjects_GivenTheirReferences_RespectTheDependencyMatrix` | A reference the matrix forbids or does not permit |
| `siblingIndependence` | `SiblingImplementations_GivenTheirReferences_DoNotDependOnEachOther` | A qualified implementation referencing a qualified sibling |
| `compositionBoundary` | `Implementations_GivenTheirReferences_DoNotDependOnComposition` | A non-host project referencing Composition |
| `forbiddenPackage` | `ProductionProjects_GivenTheirPackages_AvoidForbiddenFrameworkDependencies` | A package matching a forbidden prefix for that responsibility |
| `cycle` | `ProjectGraph_GivenItsReferences_HasNoCycles` | A dependency cycle, reported readably |
| `testReference` | `ProductionProjects_GivenTheirReferences_DoNotReferenceATestProject` | Production code referencing a test project |
| `waiverIntegrity` | `PolicyWaivers_GivenTheirDefinitions_AreJustifiedAndStillApply` | A waiver that is stale, unjustified or malformed |

A separate test validates the policy against the schema first, because a policy that does not match
its schema may be silently missing a rule.

`graphDiscovery` runs before the rules it protects. If `sourceRoots` is wrong, discovery finds
nothing and every other rule passes vacuously — that is what this rule exists to stop.

## Prefer a waiver to a skip

```csharp
protected override bool SkipRule(string rule) => rule == ArchitectureRules.DiscouragedName;
```

`SkipRule` removes a rule for the whole repository, with no record. A waiver is better: it is per
project, carries a reason and a review date, keeps every other rule in force for that project, and
is itself checked by `waiverIntegrity`.

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

`mapsTo` is the useful part: it maps an established project onto the responsibility it actually has,
so the dependency rules still apply in full and only the *name* is excused.

## Reading a failure

Each rule is its own test, so a failure names the rule rather than collapsing the set into one
assertion. The `.Because(...)` text lists every violation the rule found.

- `naming` — a project name is `<Responsibility>[.<Implementation>]`, and only the **first segment**
  is classified. `Persistence.SqlServer` and `Persistence` are both Persistence. Add an alias, or a
  waiver with `mapsTo`.
- `dependencyMatrix` — fix the reference, or move the type. Widening `mayDependOn` is the last
  resort, and it is a policy change that needs review.
- `graphDiscovery` — almost always a wrong `sourceRoots`. An empty array scans the repository root,
  which suits a flat layout.

## The three layers

Useful when extending the package rather than consuming it:

- `ArchitecturePolicy` / `ProjectGraph` — read the policy and the `.csproj` graph. No MSBuild and no
  test-framework types.
- `ArchitecturePolicyEvaluator` — the rules, returning typed `ArchitectureViolation` lists. It takes
  an **in-memory graph**, so a rule is tested as a three-project fixture rather than a repository on
  disk.
- `PurviewArchitectureTests` — the TUnit wrapper, one `[Test]` per rule.

Project files are read rather than compiled assemblies, because a declared `ProjectReference` is
what a reviewer adds and what the policy governs.

## Overrides

```csharp
[InheritsTests]
public sealed class ArchitectureTests : PurviewArchitectureTests
{
    protected override string? RepositoryRoot => "/path/to/repo";
}
```

`RepositoryRoot` defaults to probing upward from the test binary for the policy file. The search
**stops at the repository boundary** (`.git`) rather than continuing into parent folders — walking
past it would happily find a sibling checkout's policy and validate this repository against someone
else's rules.

## Related

The equivalent for agent skills is `purview-buildsdk-skill-tests`, which follows the same shape:
read-only layers, a typed violation list, and a TUnit base class adopted with `[InheritsTests]`.
