[CmdletBinding()]
param(
	[string]$Image = "mcr.microsoft.com/dotnet/sdk:10.0.400",
	# The whole integration suite by default, matching CI. Narrow it with a treenode filter, e.g.
	# -Filter '/*/*/BlazorProjectDetectionTests/*'.
	[string]$Filter = "/*/*/*/*"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
	throw "Docker is required to run the Linux integration tests."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$containerRepositoryRoot = "/repo"

# The working copy is copied rather than mounted, so the build cannot write into it, and .git is left
# behind with the build output. An empty repository is initialised in its place, because the SDK finds
# the repository root by walking up for .git/.
docker run --rm `
	--mount "type=bind,source=$repositoryRoot,target=$containerRepositoryRoot,readonly" `
	--workdir /work `
	$Image `
	bash -c "tar --exclude='.git' --exclude='.vs' --exclude='bin' --exclude='obj' --exclude='artifacts' --exclude='TestResults' --exclude='node_modules' -C /repo -cf - . | tar -C /work -xf - && git init -q && dotnet test --project src/tests/BuildSdk.IntegrationTests/BuildSdk.IntegrationTests.csproj -c Release --ignore-exit-code 8 --treenode-filter '$Filter'"

if ($LASTEXITCODE -ne 0) {
	throw "Linux integration tests failed with exit code $LASTEXITCODE."
}
