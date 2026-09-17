# project-harness-test (generic prompt spec)

Create or update an integration test in `src/tests/BuildSdk.IntegrationTests/` using `ProjectHarness`.

## Required behaviour

1. Keep scope to one behaviour (single focused scenario).
2. Use `ProjectHarness` (`Harness/ProjectHarness.cs`) to construct a throwaway project for the scenario.
3. Prefer evaluation helpers before build-log parsing:
   - `GetPropertyAsync` / `GetPropertiesAsync`
   - `GetItemIdentitiesAsync` / `GetProjectReferencesAsync`
   - `GetPreprocessProjectAsync` when import/evaluation order matters
4. Use `BuildAsync(restore: true)` only when the scenario requires restore/build behaviour.
5. Follow TUnit conventions used in this repo:
   - `[Test]` method attributes
   - awaited assertions (e.g., `await Assert.That(actual).IsEqualTo(expected);`)
6. Keep setup deterministic and minimal; avoid unrelated package/config changes.

## Suggested output structure

- Add/modify one test file under `src/tests/BuildSdk.IntegrationTests/`.
- Include a short test comment describing **Given / When / Then** intent.
- Validate by running targeted tests first, then broader tests if needed.

If multiple test ideas are possible, pick the one with the smallest diff that still validates the intended SDK behaviour.
