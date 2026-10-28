set quiet

solution_file := "src/BuildSdk.slnx"
build_configuration := "Debug"
artifacts_folder := "./artifacts"

pipeline_tool := "dotnet purview-build"

current_version := `bun -p "require('./package.json').version"`

[private]
default:
    just --list

# Restore the repository's .NET local tools (.config/dotnet-tools.json)
[private]
ensure-pipeline-tool:
    dotnet tool restore

# Run the PR pipeline (restore, build, lint, tests)
[group('Pipeline')]
pipeline-pr *args: ensure-pipeline-tool
    echo "Running PR pipeline..."
    {{ pipeline_tool }} {{ args }}

# Run the build pipeline (restore, build, lint)
[group('Pipeline')]
pipeline-build *args: ensure-pipeline-tool
    echo "Running build pipeline..."
    {{ pipeline_tool }} --Build:RunTests=false --Release:Mode=None {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, publish, GitHub release)
[group('Pipeline')]
pipeline-release *args: ensure-pipeline-tool
    echo "Running release pipeline..."
    {{ pipeline_tool }} --Release:Mode=NuGet {{ args }}

# Run the release pipeline (restore, build, lint, tests, pack, local nuget publish)
# Note: `just` runs recipes through the shell, which strips backslashes from unquoted arguments.
# Use the LOCAL_NUGET_FEED_PATH environment variable or forward slashes, e.g.
# just pipeline-local-release --PublishLocalNuGet:LocalFeedPath=p:/_sync-projects/.local-nuget/
[group('Pipeline')]
pipeline-local-release *args: ensure-pipeline-tool
    echo "Running local release pipeline..."
    {{ pipeline_tool }} --Release:Mode=LocalNuGet {{ args }}

# Run the pipeline with tests enabled
[group('Pipeline')]
pipeline-tests *args: ensure-pipeline-tool
    echo "Running tests pipeline..."
    {{ pipeline_tool }} --Build:RunTests=true --Release:Mode=None {{ args }}

# Run the pipeline through pack + validate (restore, build, lint, tests, pack, validate pack contents) without publishing/releasing
[group('Pipeline')]
pipeline-pack-validate *args: ensure-pipeline-tool
    echo "Running pack + validate pipeline..."
    {{ pipeline_tool }} --Build:RunPack=true --Build:ValidatePack=true --Release:Mode=None {{ args }}

# Open the solution in Visual Studio/ Registered application
[group('Utilities')]
vs:
    open {{ solution_file }}

# Build the solution for the specified configuration (default: Release)
[group('Build and Test')]
build *args:
    echo "==> Building {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet build {{ solution_file }} --configuration {{ build_configuration }} {{ args }}

# Cleans the solution for the specified configuration (default: Release)
[group('Build and Test')]
clean *args:
    echo "==> Cleaning {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) with configuration {{ YELLOW }}{{ build_configuration }}{{ NORMAL }}"
    dotnet clean {{ solution_file }} --configuration {{ build_configuration }} {{ args }}

# Restore local .NET tools
[group('Utilities')]
restore-tools:
    dotnet tool restore

# Restore NuGet packages for the solution
[group('Build and Test')]
restore *args:
    dotnet restore {{ solution_file }} {{ args }}

# Displays the current package version from package.json
[group('Build and Test')]
current_version:
    echo "==> Current version: {{ GREEN }}{{ current_version }}{{ NORMAL }} (defined in package.json and automatically included in the build output through the Purview.BuildSdk package)"

# Rewrite the Purview.BuildSdk pins in the docs and starter template to the version in package.json
#
# Run after bumping package.json. Packed files are stamped at pack time and need nothing.
[group('Build and Test')]
version-sync:
    echo "==> Syncing version pins to {{ GREEN }}{{ current_version }}{{ NORMAL }}"
    bun scripts/sync-version.ts

# Run tests for a specific project with a filter (e.g., "/*/*/*/*/", or "/*/*/*/*[Category=Unit]" to run just unit tests) and configuration (e.g., "Release")
[group('Build and Test')]
test filter="/*/*/*/*/" *args:
    echo "==> Testing {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ build_configuration }}{{ NORMAL }}) with filter {{ YELLOW }}{{ filter }}{{ NORMAL }}"
    dotnet test --project {{ solution_file }} --configuration {{ build_configuration }} --treenode-filter "{{ filter }}" --ignore-exit-code 8 {{ args }}

# Run the SDK integration tests in the same Linux SDK environment used by CI
[group('Build and Test')]
test-linux:
    pwsh -NoProfile -File ./scripts/test-linux-docker.ps1

# Pack all packable projects
[group('Build and Test')]
pack artifact_folder=artifacts_folder *args:
    echo "==> Packing {{ BLUE }}{{ solution_file }}{{ NORMAL }} ({{ GREEN }}{{ current_version }}{{ NORMAL }}) to {{ YELLOW }}{{ artifact_folder }}{{ NORMAL }}"
    dotnet pack "{{ solution_file }}" --configuration "{{ build_configuration }}" --output "{{ artifact_folder }}" {{ args }}

# Install skills from another repository into .agents/skills/, through the skills CLI
#
# This repository's *own* skills are not installed here. They are authored at skills/<name>/SKILL.md
# for other repositories to install, and are read from that path - AGENTS.md links to it, and
# `npx skills add purview-dev/build-sdk` is how a consumer gets them. .agents/skills/ is reserved for
# skills installed from other sources, and skills-lock.json is the committed record of those.
#
# This goes through the SDK's own InstallPurviewSkills target rather than calling the CLI directly,
# so the repository exercises the path it ships to consumers.
#
#   just skills-add purview-dev/engineering-playbook
#
# Install skills from another repository into .agents/skills/ (append '#<ref>' to pin)
[group('Agent Skills')]
skills-add source *args:
    echo "==> Installing skills from {{ BLUE }}{{ source }}{{ NORMAL }} into the agent directories"
    dotnet msbuild src/src/BuildSdk/BuildSdk.csproj -t:InstallPurviewSkills -p:PurviewSkillsSource={{ source }} {{ args }}

# Validate the authored skills in skills/ against the Agent Skills specification
#
# The same check the pack runs (PRSKL0005). The broader rule set - names, descriptions, optional
# fields, bundled links, body size - runs as RepositorySkillTests in `just test`.
#
# Validate the authored skills in skills/ against the Agent Skills specification
[group('Agent Skills')]
skills-validate *args:
    echo "==> Validating authored skills in {{ BLUE }}skills/{{ NORMAL }}"
    dotnet msbuild src/src/BuildSdk/BuildSdk.csproj -t:ValidatePurviewSkills -v:n {{ args }}

# Report which skills the restored packages ship, and whether a runner is available
[group('Agent Skills')]
skills-list *args:
    dotnet msbuild src/src/BuildSdk/BuildSdk.csproj -t:ListPurviewSkills -v:n {{ args }}

# Report missing, drifted or unverifiable skills without installing anything
[group('Agent Skills')]
skills-check *args:
    dotnet msbuild src/src/BuildSdk/BuildSdk.csproj -t:RecommendPurviewSkills -v:n {{ args }}

# Format the code with CSharpier
[group('Utilities')]
lint-fix:
    dotnet csharpier format .

# Check formatting with CSharpier
[group('Utilities')]
lint-check:
    dotnet csharpier check .
