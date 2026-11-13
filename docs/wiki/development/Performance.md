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

A comment in `Sdk.props`, beside the shared project-text properties, carries a warning that the project text must never be passed as a **quoted string
literal**, because project text contains commas, parentheses and quotes that break MSBuild's
property-function argument parsing. That warning is correct, and it is a different thing from passing
the text as `$(_PurviewProjectText)`.

Passing a property reference was verified against MSBuild directly before the change: the property is
expanded into the argument without its content being re-parsed as an argument list, and without any
`$(...)` the project text happens to contain being re-expanded. A project containing
`<Marker>$(SomeUndefinedProperty)</Marker>` still matches the literal text through the shared property,
and the property form and the old nested form agree on every probe.

The regression net is `BuildSdk.IntegrationTests`, which asserts evaluated property values directly
through `ProjectHarness.GetPropertyAsync` against real `dotnet msbuild` invocations. All 406 tests at the
time passed unchanged.

## Measured: a real 39-project solution

Measured on `changeops` (`src/ChangeOps.slnx`, 39 projects, `net11.0`, `AnalysisMode=All`,
`TreatPurviewWarningsAsErrors`-style strictness, consuming `Purview.BuildSdk` from `global.json`
`msbuild-sdks`), with `dotnet build -clp:PerformanceSummary`. That flag prints per-target and
per-task cumulative time, so none of this needs a binlog viewer.

### The headline: incremental builds did not work

| | Cold build | Warm rebuild, no source changes |
| -- | -- | -- |
| Wall clock | 154.4 s | **67.7 s** |
| `Csc` invocations | 22 | **38** |
| `Csc` time | 97.9 s | **111.6 s** |

Every one of the 38 projects recompiled on a build where nothing had changed. The cause was
`GenerateAssemblyInfoClassTarget` and `GenerateAssemblyIdentifierClassTarget` writing their generated
files with `Overwrite="true"` and no `WriteOnlyWhenDifferent`: both files are `@(Compile)` inputs, so
`CoreCompile` never had an up-to-date output. That single attribute was worth more than everything
else in this document combined, and it also silently defeated the `AccelerateBuildsInVisualStudio=true`
default that exists to make up-to-date checks fast.

### Where the rest of the time went

Task-level numbers from the cold build (`Target Performance Summary` is unreliable on a solution this
wide, because `Build` and `ResolveProjectReferences` nest and each project instance reports its own
copy of the same wait):

| Task | Time | Calls |
| -- | -- | -- |
| `Csc` | 97,857 ms | 22 |
| `Microsoft.Build.Tasks.Git.LocateRepository` + `GetUntrackedFiles` (SourceLink) | 4,095 ms | 76 |
| `ResolvePurviewSkillSources` | 1,473 ms | 38 |
| `EvaluatePurviewSkillState` | 357 ms | 38 |
| `FindPurviewRepositoryRoot` | 270 ms | 38 |
| `ValidatePurviewStylePolicy` | 195 ms | 38 |
| `ReadBuildTransitiveCompilerVisibleProperties` | 98 ms | 36 |

`ResolvePurviewSkillSources` alone was 1.47 s, and it could only ever answer "no restored package
ships agent skills" for that repository — the restored `Purview.BuildSdk` package carries no
`.agents/` folder. It read and regex-scanned 38 separate ≈500 KB `project.assets.json` files to reach
an answer that is identical for all 38 projects.

### What changed as a result

| Change | Effect |
| -- | -- |
| `WriteOnlyWhenDifferent="true"` on both generated-file writers | A no-change rebuild compiles nothing instead of everything |
| The `AfterTargets="Build"` skill hook removed; `VerifyPurviewSkills` added | 2.1 s and three inline-task compilations removed from every build |
| `ValidatePurviewStylePolicy` reads each `.editorconfig` once | 95 KB × 2 → 95 KB per project per compile |
| Both repo-file bootstraps skip `DesignTimeBuild` | No probe ladder and no 2 × 95 KB read per project, per IDE edit |

### After: the same solution on 1.3.0

The 1.2.0 figures above are the "before". Re-measured on `changeops` after the fixes, same machine,
same solution, no source changes between runs:

| | 1.2.0 | 1.3.0 |
| -- | -- | -- |
| Cold build | 154.4 s | 103.2 s |
| Warm rebuild, no source changes | 67.7 s | **7.20 s** |
| `Csc` invocations on the warm rebuild | **38** | **0** |
| `Csc` time on the warm rebuild | 111.6 s | 0 |

Two details worth having on record:

- **The first warm build after upgrading to 1.3.0 took 27.3 s, not 7.2 s**, because the SDK upgrade
  changes enough (analyzers, `.editorconfig`, editorconfig content) to invalidate a lot at once. Only
  the second no-change build reaches steady state. Quoting the 27.3 s figure as the steady state would
  overstate what is left by a factor of four.
- Every Purview target in the steady-state build sums to roughly **0.9 s of the 7.2 s** — about 12%,
  with no single one above 400 ms (`PurviewValidateBrowserSafeReferences` 362 ms,
  `EnsureRepositoryEditorConfigTarget` 268 ms, `ValidatePurviewStylePolicy` 62 ms). The SDK is no
  longer the story; framework and ecosystem targets are.

### `.editorconfig` work, sized honestly

Measured rather than guessed, because the numbers change the decision:

| Work | Cost |
| -- | -- |
| Two full reads of the 95 KB `.editorconfig` + two first-line regexes | **0.83 ms per project**, ≈ 32 ms per 39-project build |
| `ValidatePurviewStylePolicy` task (after reading each file once) | 170 ms per build, 38 projects |
| `EnsureRepositoryEditorConfigTarget` target | 381 ms per build, 38 projects |

So the redundant read is small in absolute terms. What made it worth changing was not the size but
*when* it ran: neither bootstrap target was guarded against design-time builds, and an IDE runs one
of those for every edit, on every project, for the whole time a solution is open. The fix is a
`DesignTimeBuild` condition, not a cleverer read.

The second read is **kept deliberately**. It looks removable — the marker line is stamped as
`markerPrefix + version + " - managed file, do not edit"`, so the SDK could compute the line it
expects instead of reading the source. It cannot: in an unpacked source tree no version is
available (`Props/PackedVersion.props` only exists in a packed SDK, and the package folder is not
versioned), so the computed line would never match, `_PurviewEditorConfigNeedsSync` would be true on
every build, and `SyncPurviewRepositoryFiles` — an inline task with a measured ~680 ms first-use
compile — would be invoked every time. Reading the source is what makes the check robust; 0.83 ms is
a fair price for not having a second place that knows the marker format.

### Verified on this repository

This repository dogfoods the SDK from source (`src/Directory.Build.props`), so a build of
`src/BuildSdk.slnx` exercises the changes directly. Three consecutive `Release` builds, each after
the previous one had absorbed an edit:

| Build | Wall clock | `Csc` invocations |
| -- | -- | -- |
| Absorbs edits | 10.8 s | 6 |
| No changes | 6.4 s | 1 |
| No changes, after the last generated-file writer was fixed | **0.85 s** | **0** |

The intermediate 6.4 s / 1-compile case is worth keeping: it was
`src/tests/BuildSdk.IntegrationTests/BuildSdk.IntegrationTests.csproj` doing the same thing by hand
— its `WriteSdkSourcePaths` target wrote `Generated/SdkPaths.cs` with `Overwrite="true"` and no
`WriteOnlyWhenDifferent`. The same defect, independently authored, in a file the SDK does not own.
`IncrementalBuildTests` now pins both SDK-generated files so neither can regress.

## Deliberately not changed

Two findings were measured, judged, and left alone. Both are recorded so they are not re-litigated:

- **`ResolvePurviewSkillSources` regex-scanned the whole assets file.** It was taken off the build path
  then, and later removed altogether with the package-to-skill mapping it served: the skill targets now
  only run the skills CLI, and read no restore graph at all.
- **`ImportProjectReferencedBuildTransitiveAssets` is not worth changing either**, and this is worth
  recording because it looked like the obvious next target. Its target-level timing (≈2 s across 38
  projects) is inflated by nesting, and on a transitional build it appeared to cost 19 s. Toggling it
  off on the steady-state build gave 7.99 s against 7.20 s with it on — i.e. nothing. The recursive
  `Directory.GetFiles` is not a real cost, so the safer `TopDirectoryOnly`/memoisation work has no
  measured justification and was left undone.

The largest remaining per-target cost in that solution is not this SDK at all:
`MsCoverageExtensionReferencedPathMaps`, 13,187 ms over 16 calls (≈824 ms per test project) on a build
that compiled nothing. It comes from the Microsoft coverage extension, and it is the next thing worth
investigating in that repository.

`AnalysisMode=All` (`Props/Defaults.props`) is deliberate policy rather than an accident, so its
default does not change. `-p:ReportAnalyzer=true` measures what it costs in a given repository.
