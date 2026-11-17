using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>ResolvePurviewSkillsRepositoryRoot</c>, the repository root whose <c>skills/</c> folder
/// <c>ValidatePurviewSkills</c> checks at pack time.
/// </summary>
/// <remarks>
/// The throwaway solution is not a git repository and has no <c>AGENTS.md</c>, so each test places the
/// marker it is about.
/// </remarks>
sealed class PurviewSkillsRepositoryRootTests
{
	/// <summary>
	/// Resolves the repository root skill validation uses, by asking
	/// MSBuild for the property the shared resolution target computes.
	/// </summary>
	static async Task<string> ResolveRepositoryRootAsync(
		ProjectHarness harness,
		string extraArguments,
		CancellationToken cancellationToken
	)
	{
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRoot " + extraArguments,
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		return stdOut.Trim();
	}

	/// <summary>
	/// Every marker in the default list, on its own, identifies a repository root. Four of them are
	/// directories, which MSBuild's own <c>GetDirectoryNameOfFileAbove</c> cannot find at all - it
	/// matches files only - so each one is pinned here rather than assumed to work.
	/// </summary>
	[Test]
	[Arguments(".git", true)]
	[Arguments(".github", true)]
	[Arguments(".agents", true)]
	[Arguments(".claude", true)]
	[Arguments(".gitattributes", false)]
	[Arguments("LICENSE.md", false)]
	[Arguments("AGENTS.md", false)]
	[Arguments("CLAUDE.md", false)]
	[Arguments("package.json", false)]
	public async Task RepositoryRoot_IsFoundFromAnyDefaultMarker(
		string marker,
		bool isDirectory,
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		var markerPath = Path.Combine(harness.SolutionDirectory, marker);
		if (isDirectory)
			Directory.CreateDirectory(markerPath);
		else
			await File.WriteAllTextAsync(markerPath, "marker\n", cancellationToken);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert
			.That(Path.GetFullPath(resolved))
			.IsEqualTo(Path.GetFullPath(harness.SolutionDirectory))
			.Because($"'{marker}' should identify the repository root");
	}

	[Test]
	public async Task RepositoryRoot_MatchesAFileNamedDotGit(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.git' is a directory in a clone but a *file* in a worktree or a submodule, and both
		// identify a repository root. A directory marker therefore matches either.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, ".git"),
			"gitdir: ../../.git/worktrees/x\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRoot_LetsTheDefinitiveMarkerBeatANearerOne(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.git/' sits at the solution, a 'package.json' sits nearer in the project folder. '.git/'
		// is the repository root by definition, so a package inside a monorepo must not win.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".git"));
		await File.WriteAllTextAsync(
			Path.Combine(harness.ProjectDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"nested\" }\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
		await Assert.That(Path.GetFullPath(resolved)).IsNotEqualTo(Path.GetFullPath(harness.ProjectDirectory));
	}

	[Test]
	public async Task RepositoryRoot_PrefersANearerMarkerOverADistantNonDefinitiveOne(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.github/' outranks 'package.json' in the list, but only '.git/' is allowed to win from a
		// distance. Were that not so, a stray '.claude/' or '.github/' in a developer's home
		// directory would outrank the real repository root and scatter a lockfile outside it.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".github"));
		await File.WriteAllTextAsync(
			Path.Combine(harness.ProjectDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"nested\" }\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert
			.That(Path.GetFullPath(resolved))
			.IsEqualTo(Path.GetFullPath(harness.ProjectDirectory))
			.Because("the nearest marker wins once the definitive one is absent");
	}

	[Test]
	public async Task RepositoryRoot_BreaksTiesWithinADirectoryByListOrder(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// Both markers sit in the same directory, so the list order decides which one is reported.
		await File.WriteAllTextAsync(Path.Combine(harness.SolutionDirectory, "LICENSE.md"), "MIT\n", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"root\" }\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRootMarker",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut.Trim()).IsEqualTo("LICENSE.md");
	}

	[Test]
	public async Task RepositoryRoot_FallsThroughWhenTheLeadingMarkersAreAbsent(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// Nothing above '.claude/' exists, so the walk has to keep going down the list rather than
		// stopping at the first marker it cannot find.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".claude"));

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRootMarkers_CanBeReplaced(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "my-repo-marker"),
			"marker\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(
			harness,
			"-p:PurviewRepositoryRootMarkers=my-repo-marker",
			cancellationToken
		);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRoot_IsFoundFromAnAgentsMarkerAboveTheProject(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// The marker sits beside the solution, a level above the project, which is exactly the shape
		// the walk-up exists for: skills belong to the repository, not to whichever project built.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(resolved).IsNotEmpty();
		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
		// Never the project folder - that is the scattering this prevents.
		await Assert.That(Path.GetFullPath(resolved)).IsNotEqualTo(Path.GetFullPath(harness.ProjectDirectory));
	}

	[Test]
	public async Task RepositoryRoot_PrefersAnExplicitInstallRoot(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		var elsewhere = Path.Combine(harness.SolutionDirectory, "elsewhere");
		Directory.CreateDirectory(elsewhere);

		var resolved = await ResolveRepositoryRootAsync(
			harness,
			$"-p:PurviewSkillsInstallRoot=\"{elsewhere}\"",
			cancellationToken
		);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(elsewhere));
	}
}
