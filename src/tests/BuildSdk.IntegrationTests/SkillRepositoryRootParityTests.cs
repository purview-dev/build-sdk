using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;
using Purview.Skills.Testing;

namespace Purview.BuildSdk;

/// <summary>
/// Asserts that the managed repository-root walk in <c>Purview.Skills.Testing</c> agrees with the
/// SDK's <c>FindPurviewRepositoryRoot</c> inline task.
/// </summary>
/// <remarks>
/// <para>
/// There are two implementations of this walk, and they have to agree. The build installs skills
/// into the root MSBuild resolves; the tests validate the skills in the root the managed port
/// resolves. A disagreement means a repository validates one folder while installing another, and
/// neither side would report anything - both would simply be looking somewhere.
/// </para>
/// <para>
/// Each case drives the real MSBuild target through the harness and the real managed resolver over
/// the same folder, then compares. That is the only assertion that can catch a divergence, because
/// each implementation is independently tested against its own expectations and both would stay
/// green while drifting apart.
/// </para>
/// </remarks>
public class SkillRepositoryRootParityTests
{
	/// <summary>Asks MSBuild for the root the skill targets would run from.</summary>
	/// <param name="harness">The throwaway project.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The resolved root.</returns>
	static async Task<string> ResolveWithMSBuildAsync(ProjectHarness harness, CancellationToken cancellationToken)
	{
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRoot",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		return stdOut.Trim();
	}

	/// <summary>Asserts both implementations resolve the same directory.</summary>
	/// <param name="harness">The throwaway project.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	static async Task AssertAgreementAsync(ProjectHarness harness, CancellationToken cancellationToken)
	{
		var fromMSBuild = await ResolveWithMSBuildAsync(harness, cancellationToken);
		var fromManagedPort = SkillRepositoryRoot.Find(harness.ProjectDirectory, SkillRepositoryRoot.DefaultMarkers);

		await Assert.That(fromManagedPort).IsNotNull();
		await Assert
			.That(Path.GetFullPath(fromManagedPort!))
			.IsEqualTo(Path.GetFullPath(fromMSBuild))
			.Because("SkillRepositoryRoot is a port of FindPurviewRepositoryRoot and must resolve the same root");
	}

	[Test]
	[Arguments(".git", true)]
	[Arguments(".github", true)]
	[Arguments(".gitattributes", false)]
	[Arguments("LICENSE.md", false)]
	[Arguments("AGENTS.md", false)]
	[Arguments(".agents", true)]
	[Arguments("CLAUDE.md", false)]
	[Arguments(".claude", true)]
	[Arguments("package.json", false)]
	public async Task BothImplementations_AgreeOnEveryDefaultMarker(
		string marker,
		bool isDirectory,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var harness = await ProjectHarness.CreateAsync("SkillRootParity", cancellationToken: cancellationToken);
		var markerPath = Path.Combine(harness.SolutionDirectory, marker);

		if (isDirectory)
			Directory.CreateDirectory(markerPath);
		else
			await File.WriteAllTextAsync(markerPath, "marker\n", cancellationToken);

		// Act & Assert
		await AssertAgreementAsync(harness, cancellationToken);
	}

	[Test]
	public async Task BothImplementations_AgreeThatTheDefinitiveMarkerWinsFromADistance(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		// '.git/' at the solution, 'package.json' nearer in the project folder. A monorepo
		// sub-package is not the repository, and both implementations have to know that.
		using var harness = await ProjectHarness.CreateAsync("SkillRootParity", cancellationToken: cancellationToken);
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".git"));
		await File.WriteAllTextAsync(Path.Combine(harness.ProjectDirectory, "package.json"), "{}\n", cancellationToken);

		// Act
		var fromMSBuild = await ResolveWithMSBuildAsync(harness, cancellationToken);

		// Assert
		await Assert.That(Path.GetFullPath(fromMSBuild)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
		await AssertAgreementAsync(harness, cancellationToken);
	}

	[Test]
	public async Task BothImplementations_AgreeThatANearerMarkerBeatsADistantNonDefinitiveOne(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		// '.github/' outranks 'package.json' in the list, but only the definitive marker may win
		// from a distance - otherwise a stray marker in an ancestor outranks the real root.
		using var harness = await ProjectHarness.CreateAsync("SkillRootParity", cancellationToken: cancellationToken);
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".github"));
		await File.WriteAllTextAsync(Path.Combine(harness.ProjectDirectory, "package.json"), "{}\n", cancellationToken);

		// Act
		var fromMSBuild = await ResolveWithMSBuildAsync(harness, cancellationToken);

		// Assert
		await Assert.That(Path.GetFullPath(fromMSBuild)).IsEqualTo(Path.GetFullPath(harness.ProjectDirectory));
		await AssertAgreementAsync(harness, cancellationToken);
	}

	[Test]
	public async Task BothImplementations_AgreeOnTheMarkerThatMatched(CancellationToken cancellationToken)
	{
		// Arrange
		// Two markers in one directory: list order decides, and the reported marker is what a
		// diagnostic shows a developer. Agreeing on the root but not on why is still a divergence.
		using var harness = await ProjectHarness.CreateAsync("SkillRootParity", cancellationToken: cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(harness.SolutionDirectory, "LICENSE.md"), "MIT\n", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRootMarker",
			cancellationToken
		);

		// Act
		var (_, fromManagedPort) = SkillRepositoryRoot.FindWithMarker(
			harness.ProjectDirectory,
			SkillRepositoryRoot.DefaultMarkers
		);

		// Assert
		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(fromManagedPort).IsEqualTo(stdOut.Trim());
	}
}
