[CmdletBinding()]
param(
	[string]$Image = "mcr.microsoft.com/dotnet/sdk:10.0.400"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
	throw "Docker is required to run the Linux integration tests."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$containerRepositoryRoot = "/repo"

docker run --rm `
	--mount "type=bind,source=$repositoryRoot,target=$containerRepositoryRoot,readonly" `
	--workdir /work `
	$Image `
	bash -c "tar --exclude='.git' --exclude='.vs' --exclude='bin' --exclude='obj' --exclude='artifacts' --exclude='TestResults' --exclude='node_modules' -C /repo -cf - . | tar -C /work -xf - && dotnet test src/tests/BuildSdk.IntegrationTests/BuildSdk.IntegrationTests.csproj -c Release -- --treenode-filter '/*/*/AgentPackFolderTests/*'"

if ($LASTEXITCODE -ne 0) {
	throw "Linux integration tests failed with exit code $LASTEXITCODE."
}
