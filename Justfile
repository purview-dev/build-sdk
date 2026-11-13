# Recipes for working on Purview.BuildSdk. Run `just` for the list.
#
# The shared recipes - .NET, version, pipeline, agent skills and utilities - come from .build/just/,
# imported through its barrel. Those files are the purview-buildsdk-justfile skill's assets, copied
# verbatim: change a recipe in skills/purview-buildsdk-justfile/assets/just/ and copy it back
# (JustModuleParityTests fails while the two differ). This file holds only what belongs to this
# repository - the values the modules read, and the recipes nothing else needs.

set quiet

# Let a definition here override a module's default variable or no-op hook.
set allow-duplicate-variables
set allow-duplicate-recipes

import ".build/just/index.just"

solution_file := "src/BuildSdk.slnx"

# ValidatePurviewSkills is the pack-time check and runs in each project it is invoked on, so it names
# the one project that packs - rather than the solution, which would validate the same folder once
# per project.
skills_validate_project := "src/src/BuildSdk/BuildSdk.csproj"

[private]
default:
    just --list

# A bump changes package.json, so the copy-paste pins must follow it.
_after-bump: version-sync

# Rewrite the Purview.BuildSdk pins in the docs, starter template and .editorconfig marker
#
# `just bump` runs this itself. Packed files are stamped at pack time and need nothing.
[doc('Rewrite the Purview.BuildSdk pins in the docs, starter template and .editorconfig marker')]
[group('Version')]
version-sync:
    echo "==> Syncing version pins to {{ GREEN }}{{ current_version }}{{ NORMAL }}"
    bun scripts/sync-version.ts

# Check the version pins without writing anything; non-zero when a pin is stale
#
# What the pre-commit hook runs, so a stale .editorconfig marker or quick-start snippet cannot be
# committed in the first place.
[doc('Check the version pins without writing anything; non-zero when a pin is stale')]
[group('Version')]
version-sync-check:
    echo "==> Checking version pins against {{ GREEN }}{{ current_version }}{{ NORMAL }}"
    bun scripts/sync-version.ts --check

# Run the SDK integration tests in the same Linux SDK environment used by CI
[group('Build and Test')]
test-linux:
    pwsh -NoProfile -File ./scripts/test-linux-docker.ps1

# Check every SDK property against Sdk.props.buildschema.json
#
# Definitions, defaults, the compiler-visible list, the README and wiki tables, the meta-schema and
# the PRPSC target. No --ignore-exit-code 8: a filter that matches nothing must fail, not pass.
[doc('Check every SDK property against Sdk.props.buildschema.json')]
[group('Build and Test')]
properties-check *args:
    echo "==> Checking SDK properties against {{ GREEN }}Sdk.props.buildschema.json{{ NORMAL }}"
    dotnet test --project src/tests/BuildSdk.IntegrationTests/BuildSdk.IntegrationTests.csproj --configuration {{ build_configuration }} --treenode-filter "/*/*/(PropertySchemaTests)|(PropertySchemaValidationTargetTests)/*" {{ args }}

# Validate the authored skills in skills/ against the Agent Skills specification
#
# The same check the pack runs (PRSKL0005). The broader rule set - names, descriptions, optional
# fields, bundled links, body size - runs as RepositorySkillTests in `just test`.
[doc('Validate the authored skills in skills/ against the Agent Skills specification')]
[group('Agent Skills')]
skills-validate *args:
    echo "==> Validating authored skills in {{ BLUE }}skills/{{ NORMAL }}"
    dotnet msbuild "{{ skills_validate_project }}" -t:ValidatePurviewSkills -v:n {{ args }}
