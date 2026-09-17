namespace Purview.BuildSdk.Harness;

/// <summary>
/// Compatibility shim around <see cref="ProjectHarness"/>.
/// Use <see cref="ProjectHarness"/> directly for new tests.
/// </summary>
sealed class SimpleProjectHarness(string projectDirectory, string projectName, string workDir)
	: ProjectHarness(workDir, projectName)
{
	public string RequestedProjectDirectory { get; } = projectDirectory;
}
