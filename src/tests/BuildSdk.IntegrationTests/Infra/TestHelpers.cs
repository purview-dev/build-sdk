using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk.Infra;

static class TestHelpers
{
	public static string GenerateError(string stdOut, string stdErr)
	{
		var msg = "";
		if (!string.IsNullOrWhiteSpace(stdOut))
			msg += "Standard Output:\n" + stdOut + "\n";
		if (!string.IsNullOrWhiteSpace(stdErr))
			msg += "Standard Error:\n" + stdErr + "\n";

		msg = msg?.Trim();

		if (string.IsNullOrWhiteSpace(msg))
			msg = "No additional information returned";

		return msg;
	}

	/// <summary>
	/// Normalizes a path to use forward slashes for consistent comparison.
	/// </summary>
	public static string NormalizePath(string path) => path.Replace('\\', '/');

	/// <summary>
	/// Helper to create a complete project structure with SDK boilerplate,
	/// then evaluate the project to get items.
	/// </summary>
	public static Task<(ProjectHarness Harness, IReadOnlyList<string> ProjectReferences)> CreateProjectStructureAsync(
		string projectName,
		Action<string>? createSiblings = null,
		Func<string>? additionalProjectContent = null,
		CancellationToken cancellationToken = default
	) =>
		CreateProjectStructureAsync(
			projectName,
			w =>
			{
				createSiblings?.Invoke(w);
				return Task.CompletedTask;
			},
			additionalProjectContent,
			cancellationToken
		);

	/// <summary>
	/// Helper to create a complete project structure with SDK boilerplate,
	/// then evaluate the project to get items.
	/// </summary>
	public static async Task<(
		ProjectHarness Harness,
		IReadOnlyList<string> ProjectReferences
	)> CreateProjectStructureAsync(
		string projectName,
		Func<string, Task>? createSiblings = null,
		Func<string>? additionalProjectContent = null,
		CancellationToken cancellationToken = default
	)
	{
		var workDir = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workDir);

		// Create the main project directory
		var projectDir = Path.Combine(workDir, projectName);
		Directory.CreateDirectory(projectDir);

		// Allow caller to create sibling projects before we set up the SDK
		if (createSiblings != null)
			await createSiblings(workDir);

		var additionalContent = additionalProjectContent?.Invoke() ?? string.Empty;

		// Now create the SDK boilerplate for the main project
		await File.WriteAllTextAsync(
			Path.Combine(projectDir, "Directory.Build.props"),
			$"""
			<Project>
				<PropertyGroup>
					<NamespacePrefix>Test</NamespacePrefix>
				</PropertyGroup>
				<Import Project="{SdkPaths.SdkDirectory}/Sdk.props" />
			</Project>
			""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(projectDir, "Directory.Build.targets"),
			$"""
			<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
				<Import Project="{SdkPaths.SdkDirectory}/Sdk.targets" />
			</Project>
			""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(projectDir, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
			</Project>
			""",
			cancellationToken
		);

		// Create the project file
		await File.WriteAllTextAsync(
			Path.Combine(projectDir, $"{projectName}.csproj"),
			$"""
			<Project Sdk="Microsoft.NET.Sdk">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
				</PropertyGroup>
				{additionalContent}
			</Project>
			""",
			cancellationToken
		);

		// Create harness manually to use our custom setup
		var harness = new ProjectHarness(workDir, projectName);
		var projectReferences = await harness.GetItemIdentitiesAsync("ProjectReference", cancellationToken);

		return (harness, projectReferences);
	}
}
