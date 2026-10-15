# Purview.Skills.Testing

Validates a repository's authored agent skills against the
[Agent Skills specification](https://agentskills.io/specification), as TUnit tests.

A malformed `SKILL.md` does not fail loudly — the CLI simply skips it, and the skill is never
installed. This turns that silence into a test failure.

## Install

```bash
dotnet add package Purview.Skills.Testing
```

## Adopt

Author skills at `skills/<name>/SKILL.md`, then add one derived test class:

```csharp
[InheritsTests]
public sealed class SkillTests : PurviewSkillTests;
```

> **`[InheritsTests]` is required.** TUnit does not walk base types during discovery, so without it
> the class compiles, the run is green and nothing is validated. TUnit's analyzer reports
> `TUnit0030` — treat it as an error.

## The rules

| Rule | Checks |
| -- | -- |
| `skills-discovery` | The folder, when present, holds at least one skill |
| `skill-structure` | Every folder has a `SKILL.md` whose frontmatter parses |
| `skill-name` | Spec-legal, unique, and equal to the folder name |
| `skill-description` | Present, non-empty, within 1024 characters |
| `skill-optional-fields` | Only spec-defined fields, each within its limit |
| `skill-bundled-resources` | Every linked file exists, inside the skill folder |
| `skill-body-size` | `SKILL.md` stays under the recommended 500 lines |

Two of those are worth the reasoning:

- A repository with **no** `skills/` folder passes everything — authoring skills is optional. An
  **empty** folder fails `skills-discovery`, because every other rule is vacuous against an empty
  catalogue.
- `skill-bundled-resources` rejects a link resolving *outside* the skill folder, not only a missing
  one. Only the skill's own folder is installed, so such a link works in the authoring repository
  and nowhere else.

Prose quality, and whether an agent obeys a skill, are deliberately not checked. A test can only
check that a skill is well formed — treat a green run as "installable", not "good".

## Overrides

```csharp
[InheritsTests]
public sealed class SkillTests : PurviewSkillTests
{
    protected override string SkillsFolderName => "agent-skills";
    protected override bool SkipRule(string rule) => rule == SkillRules.BodySize;
}
```

`RepositoryRoot` defaults to a marker walk (`.git/`, `.github/`, `AGENTS.md`, …) that stops below
the home directory, so it never resolves to `~`.

## Design

Three layers, deliberately separated: `SkillManifestReader`/`SkillCatalogue` read the files with no
MSBuild or test-framework types; `SkillPolicyEvaluator` holds the rules and takes a `SkillCatalogue`,
so a rule is testable against a fixture folder; `PurviewSkillTests` is the TUnit wrapper with one
`[Test]` per rule.

The frontmatter parser covers the subset of YAML the specification defines rather than taking a YAML
dependency — this assembly is referenced by your test project, and a transitive YAML parser is a
dependency conflict waiting to happen for a format whose whole surface is six fields. Anything it
cannot parse is reported as a structural error rather than silently dropped.

---

Part of [Purview.BuildSdk](https://github.com/purview-dev/build-sdk) · [purview.dev](https://purview.dev/projects/build-sdk/)
