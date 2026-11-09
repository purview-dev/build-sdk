# Agent-skill recipes, imported by the Justfile.
#
# Every skill target runs once for the whole solution, however many projects it holds, and reads
# every project's restore graph - so the recipes name the solution, not a hand-picked project. It is
# the Justfile's own solution_file, so the two cannot drift apart.
skills_project := solution_file

# ValidatePurviewSkills is the pack-time check, and runs in each project it is invoked on, so it names
# the one project that packs the skills.
skills_validate_project := "src/src/BuildSdk/BuildSdk.csproj"

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
    dotnet msbuild "{{ skills_project }}" -t:InstallPurviewSkills -p:PurviewSkillsSource={{ source }} {{ args }}

# Validate the authored skills in skills/ against the Agent Skills specification
#
# The same check the pack runs (PRSKL0005). The broader rule set - names, descriptions, optional
# fields, bundled links, body size - runs as RepositorySkillTests in `just test`.
#
# Validate the authored skills in skills/ against the Agent Skills specification
[group('Agent Skills')]
skills-validate *args:
    echo "==> Validating authored skills in {{ BLUE }}skills/{{ NORMAL }}"
    dotnet msbuild "{{ skills_validate_project }}" -t:ValidatePurviewSkills -v:n {{ args }}

# Report which skills the restored packages ship, and whether a runner is available
[group('Agent Skills')]
skills-list *args:
    dotnet msbuild "{{ skills_project }}" -t:ListPurviewSkills -v:n {{ args }}

# Report missing, drifted or unverifiable skills without installing anything
[group('Agent Skills')]
skills-recommend *args:
    dotnet msbuild "{{ skills_project }}" -t:RecommendPurviewSkills -v:n {{ args }}

# Update the agent skills by re-adding every unique source in skills-lock.json, then 'skills update'
#
# Skills a referenced package ships are re-added at that package's commit. When PurviewSkillsAgents
# is set, the agent directories it does not select are cleaned up afterwards (FixPurviewSkills);
# when it is not, the skills CLI auto-detects agents and the cleanup does nothing.
#
#   just skills-update -p:PurviewSkillsUpdateMode=Always -p:PurviewSkillsDryRun=true
#   just skills-update -p:PurviewSkillsRunner=npx -p:PurviewSkillsAgents=universal
#
# Update the agent skills by re-adding every unique source in skills-lock.json
[group('Agent Skills')]
skills-update *args:
    dotnet msbuild "{{ skills_project }}" "-t:UpdatePurviewSkills;FixPurviewSkills" -v:n {{ args }}

# Check that only the skill directories of the agents in PurviewSkillsAgents exist
[group('Agent Skills')]
skills-check *args:
    dotnet msbuild "{{ skills_project }}" -t:CheckPurviewSkills -v:n {{ args }}

# Remove the skills CLI's links from agent directories PurviewSkillsAgents does not select
[group('Agent Skills')]
skills-fix *args:
    dotnet msbuild "{{ skills_project }}" -t:FixPurviewSkills -v:n {{ args }}
