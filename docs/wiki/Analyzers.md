# SDK-Shipped Analyzers

The package ships `Purview.BuildSdk.Analyzers.dll` (plus a separate code-fix assembly for the
IDE) and adds the analyzer to every C# project as an `<Analyzer>` item, so the rules surface in both
command-line builds and Visual Studio.

| Rule | Category | Severity | Description |
| -- | -- | -- | -- |
| `PDS0001` | (suppressor) | — | Suppresses `CS1591` for `EditorBrowsable(Never)` members |
| `PDS0002` | Naming | Warning | Files under a project-root `Extensions/` folder reset their namespace |
| `PDS0003` | Style | Warning | Prefer an explicit type with target-typed `new()` over `var` |
| `PDS0004` | Naming | Warning | Use correct acronym capitalization (`Api` → `API`) |
| `PDS0005` | (suppressor) | — | Suppresses `IDE0130` for files rooted under `Extensions/` |

## Style policy

The SDK ships the modifier/visibility **and field-naming** policy in `.editorconfig` and enforces it with
`ValidatePurviewStylePolicy` (`PRSGD0006`-`PRSGD0009`), so a repository cannot neuter it:

- `dotnet_style_require_accessibility_modifiers = omit_if_default:warning`: a declared modifier that
  matches the language default (`private` inside a type, `internal` at namespace scope, `public` inside
  an interface) is reported by `IDE0040` and must be removed.
- Private instance fields must be `_camelCase` (`_name`, never `name`): the `_` prefix keeps field
  access unambiguous, so the noisy `this.` qualifier is never needed. Constants and `static readonly`
  fields stay PascalCase, and local constants stay camelCase.
- `CA1515` (public type in an application/test assembly) ships at warning; `CA1852` (seal internal
  types) is not suppressed for types exposed through `InternalsVisibleTo`; `CA1034` is not disabled for
  `Extensions/` files; the API-surface rules `CA1062`/`CA1707` are not pre-suppressed for test projects.
- Test projects are executables, so their test classes must be non-public (`sealed class MyTests`,
  `internal` by default) rather than the rules being hidden through `NoWarn`.

| Code | Reported when |
| -- | -- |
| `PRSGD0006` | `dotnet_style_require_accessibility_modifiers` is set to anything other than `omit_if_default`. |
| `PRSGD0007` | A policy rule is downgraded below warning (`IDE0040`, `CA1515`, `CA1852`, `CA1034`), any policy rule is set to `none`/`silent`, or the `Style` category is disabled in bulk without an explicit `IDE0040` severity. |
| `PRSGD0008` | One of the accessibility rules (`IDE0040`, `CA1515`, `CA1852`, `CA1034`, `CA1012`, `CA1047`, `CA1050`, `CA1051`, `CA1062`, `CA1064`, `CA1707`) is added to `NoWarn`. |
| `PRSGD0009` | `IDE1006` is hidden, the private instance field naming rule is downgraded below warning, or the `_` prefix is removed from the field naming style. |

Opt out with `<DisablePurviewStylePolicyValidation>true</DisablePurviewStylePolicyValidation>`. A stale
bootstrapped `.editorconfig` is reported rather than obeyed — refresh it with
`PurviewRepoBootstrapMode=Always` (or delete it).

## PDS0002 — Extensions namespace rule

When a file is placed under a project-root `Extensions/` folder, the analyzer intentionally treats
that folder as a namespace reset point.

- Scope: only files where the first project-relative segment is exactly `Extensions`
- Expected namespace: derived from subfolders under `Extensions/` (file name is ignored)
- `RootNamespace` is deliberately ignored for these files

Examples:

| Project-relative file path | Expected namespace |
| -- | -- |
| `Extensions/System/StringExtensions.cs` | `System` |
| `Extensions/Microsoft/Extensions/Configuration/ConfigurationExtensions.cs` | `Microsoft.Extensions.Configuration` |
| `Extensions/TopLevel.cs` | *(global namespace)* |

To avoid conflicting guidance, `IDE0130` is suppressed (PDS0005) for files in this root `Extensions/`
scope. The shipped `.editorconfig` also suppresses the namespace-conflict diagnostics this convention
can trigger (`CA1724`, `CS0436`, `CS1591`, `IDE0005`), so extension files never need `#pragma`
suppressions. Outside this scope, normal `IDE0130` behaviour remains unchanged.

## PDS0003 — Prefer explicit type with target-typed `new()`

Flags `var` declarations that use a target-typed object creation initializer, preferring:

```csharp
// ❌ flagged
var service = new Service();
// ✅ preferred
Service service = new();
```

## PDS0004 — Acronym capitalization

`PDS0004` follows .NET naming guidance for well-known framework spellings (`Sql`, `Guid`, `Uuid`,
`Url`, `Dns`, `Http`, `Xml`, `DbContext`, ...) while still enforcing uppercase for acronyms such as
`Api` → `API`, `Ai` → `AI`, `Cpu` → `CPU`, `Gpu` → `GPU`, `Cli` → `CLI`, `Gui` → `GUI`, `Ram` → `RAM`,
and `Ssh` → `SSH`. By default the acronym-like segments `Http`, `Xml`, `Json`, `Id`, `Sdk`, `Sql`,
`Uuid`, `Url`, `Dns`, `Tcp`, `Udp`, `Csv`, `Pdf`, `Html`, `Css`, `Ftp`, `Smtp`, `Imap`, and `Db` are
exempt — `Db` is deliberately exempt so the prevalent EF Core/ADO.NET spellings (`DbContext`,
`DbConnection`, `DbSet`, `CreateDbContext`) are never flagged; a repo that prefers `DB` can re-enable
it via `acronym_map = Db:DB`. All options are customisable per repo, project, or folder via
`.editorconfig` and **merge with the shipped defaults — config entries override them** (so you can opt
into `Sql` → `SQL` or opt out of `Cli` → `CLI` without re-declaring every default):

- `dotnet_analyzer_configuration.pds0004.allowed_words` — semicolon-separated segments that are never flagged.
- `dotnet_analyzer_configuration.pds0004.acronym_map` — semicolon-separated `Key:Value` corrections.
- `dotnet_analyzer_configuration.pds0004.allowed_identifiers` — semicolon-separated whole identifiers
  that are never flagged. Matched in the order listed (first match wins) by exact name or
  word-boundary prefix, so a brand name like `CosmosDb` also covers `CosmosDbServer`/`CosmosDbContext`.

Members whose names are mandated by a contract — interface implementations (implicit or explicit) and
base-class overrides — are never renamed, since doing so would break the contract.

```ini
[*.cs]
# Optional overrides; everything not mentioned keeps its shipped default.
dotnet_analyzer_configuration.pds0004.allowed_words = Cli
dotnet_analyzer_configuration.pds0004.acronym_map = Db:DB
dotnet_analyzer_configuration.pds0004.allowed_identifiers = ICosmosDBService;CosmosDb;GitHub;YouTube
```

The code fix for `PDS0004` renames the identifier and all of its references across the solution; see
[Code Fixes](Code-Fixes.md).