# Performance

What this SDK costs at **evaluation** time, and what has been done about it.

Evaluation cost is the number that matters here. `Sdk.props` is imported by every project in every
consuming repository, and it is evaluated on every build, every restore, and every design-time build the
IDE runs — which, for an open solution, is continuously.

## Project-text scans

`Sdk.props` has to inspect the raw project text, because it is imported *before* the project body is
evaluated and so cannot ask MSBuild what the project declares. Six properties are resolved this way:

| Property | Looks for |
| -- | -- |
| `IsRoslynComponent` | `<IsRoslynComponent>true</IsRoslynComponent>` |
| `IsRoslynComponentOnly` | `<IsRoslynComponentOnly>false</IsRoslynComponentOnly>` |
| `IsPackable` | `<IsPackable>true</IsPackable>` |
| `_PurviewProjectDeclaresTargetFramework` | `<TargetFramework>` / `<TargetFrameworks>` |
| `_PurviewProjectDeclaresRootNamespace` | a non-empty `<RootNamespace>` |
| `_PurviewProjectTargetFrameworkSet` | `<PurviewTargetFrameworkSet>` |

Each of these used to re-read the project file and re-strip its XML comments before running its own
regex, so one evaluation paid for **six** file reads and six full-text comment-stripping replacements.
The file is now read once into `_PurviewProjectRawText`, comment-stripped once into
`_PurviewProjectText`, and all six scans share that result. `SdkProjectName` reuses the raw text.

### Measured

Isolating just the read-and-strip work, over `BuildSdk.csproj` (4,756 bytes), 2,000 iterations:

| | Per project |
| -- | -- |
| Before — 6 reads + 6 comment strips | 0.70 ms |
| After — 1 read + 1 comment strip | 0.12 ms |
| **Saving** | **0.59 ms** |

Scaled to a solution-wide evaluation:

| Projects | Saving per evaluation |
| -- | -- |
| 50 | ~29 ms |
| 200 | ~117 ms |

Be clear about the size of this: it is a real saving and it is paid back on every evaluation, but it is
not transformative. On a 200-project solution it is roughly a tenth of a second against an evaluation
measured in seconds. It was worth doing because the cost was pure waste and the fix is behaviour-free —
not because evaluation was slow.

### Why the scans were not shared before

`Sdk.props:184` carries a warning that the project text must never be passed as a **quoted string
literal**, because project text contains commas, parentheses and quotes that break MSBuild's
property-function argument parsing. That warning is correct, and it is a different thing from passing
the text as `$(_PurviewProjectText)`.

Passing a property reference was verified against MSBuild directly before the change: the property is
expanded into the argument without its content being re-parsed as an argument list, and without any
`$(...)` the project text happens to contain being re-expanded. A project containing
`<Marker>$(SomeUndefinedProperty)</Marker>` still matches the literal text through the shared property,
and the property form and the old nested form agree on every probe.

The regression net is `BuildSdk.IntegrationTests`, which asserts evaluated property values directly
through `ProjectHarness.GetPropertyAsync` against real `dotnet msbuild` invocations. All 406 tests pass
unchanged.

## Inline tasks

`Sdk.targets` defines 10 `RoslynCodeTaskFactory` inline C# tasks. MSBuild compiles each on first use,
per node, so they carry a cold-start cost that is paid once per build rather than once per project.
This has not been measured, and is the obvious next thing to look at.

## Not yet measured

- End-to-end evaluation of a synthetic 50- and 200-project solution, with and without the SDK imported.
- Inline-task compilation cost, cold versus warm.
- `VersionDetection` cache hit rate across a solution.
