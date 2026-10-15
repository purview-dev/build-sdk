namespace Purview.Skills.Testing.UnitTests;

/// <summary>
/// Tests the managed port of the SDK's <c>FindPurviewRepositoryRoot</c> marker walk.
/// </summary>
/// <remarks>
/// These assertions mirror the MSBuild-level ones in the SDK's own
/// <c>PurviewSkillInstallCommandTests</c>. The two implementations must agree, because the build
/// installs skills into the root one of them resolves and the tests validate the skills in the root
/// the other resolves; a disagreement means validating one folder while installing another.
/// </remarks>
public class SkillRepositoryRootTests
{
	/// <summary>Creates a throwaway directory tree, outside any repository, to walk.</summary>
	/// <param name="segments">The folders to create below the fixture root.</param>
	/// <returns>The deepest directory created.</returns>
	static string CreateTree(params string[] segments)
	{
		var path = Path.Combine([
			Path.GetTempPath(),
			"PurviewSkillRootFixture",
			Guid.NewGuid().ToString("N"),
			.. segments,
		]);

		Directory.CreateDirectory(path);
		return path;
	}

	[Test]
	public async Task Find_GivenADefinitiveMarkerAboveANearerOne_PrefersTheDefinitiveOne(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		// '.git/' *is* the repository root, so a 'package.json' in a monorepo sub-package below it
		// must not win. This is why the first marker is searched all the way up.
		var project = CreateTree("repo", "packages", "web");
		var repository = Directory.GetParent(Directory.GetParent(project)!.FullName)!.FullName;
		Directory.CreateDirectory(Path.Combine(repository, ".git"));
		await File.WriteAllTextAsync(Path.Combine(project, "package.json"), "{}\n", cancellationToken);

		// Act
		var resolved = SkillRepositoryRoot.Find(project, SkillRepositoryRoot.DefaultMarkers);

		// Assert
		await Assert.That(resolved).IsEqualTo(repository);
	}

	[Test]
	public async Task Find_GivenADotGitFile_TreatsItAsADirectoryMarker(CancellationToken cancellationToken)
	{
		// Arrange
		// '.git' is a directory in a clone but a *file* in a worktree or a submodule, and both
		// identify a repository root, so a directory marker matches either.
		var project = CreateTree("repo", "src");
		var repository = Directory.GetParent(project)!.FullName;
		await File.WriteAllTextAsync(
			Path.Combine(repository, ".git"),
			"gitdir: ../.git/worktrees/example\n",
			cancellationToken
		);

		// Act
		var resolved = SkillRepositoryRoot.Find(project, SkillRepositoryRoot.DefaultMarkers);

		// Assert
		await Assert.That(resolved).IsEqualTo(repository);
	}

	[Test]
	public async Task Find_GivenOnlyNonDefinitiveMarkers_PrefersTheNearestOne(CancellationToken cancellationToken)
	{
		// Arrange
		// '.github/' outranks 'package.json' in the list, but only the definitive marker is allowed
		// to win from a distance: a stray marker in an ancestor must not outrank the real root.
		var project = CreateTree("outer", "inner");
		var outer = Directory.GetParent(project)!.FullName;
		Directory.CreateDirectory(Path.Combine(outer, ".github"));
		await File.WriteAllTextAsync(Path.Combine(project, "package.json"), "{}\n", cancellationToken);

		// Act
		var resolved = SkillRepositoryRoot.Find(project, SkillRepositoryRoot.DefaultMarkers);

		// Assert
		await Assert.That(resolved).IsEqualTo(project);
	}

	[Test]
	public async Task FindWithMarker_GivenTwoMarkersInOneDirectory_BreaksTheTieByListOrder(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		var repository = CreateTree("repo");
		await File.WriteAllTextAsync(Path.Combine(repository, "LICENSE.md"), "MIT\n", cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(repository, "AGENTS.md"), "# Repo\n", cancellationToken);

		// Act
		var (root, marker) = SkillRepositoryRoot.FindWithMarker(repository, SkillRepositoryRoot.DefaultMarkers);

		// Assert
		await Assert.That(root).IsEqualTo(repository);
		await Assert.That(marker).IsEqualTo("LICENSE.md").Because("LICENSE.md precedes AGENTS.md in the list");
	}

	[Test]
	public async Task Find_GivenNoMarkerAnywhere_RefusesToGuess()
	{
		// Arrange
		// The project folder is deliberately not an acceptable answer: resolving to it would scatter
		// a lockfile and an agent directory through the tree, one per project.
		var project = CreateTree("nothing", "here");

		// Act
		var resolved = SkillRepositoryRoot.Find(project, ["this-marker-does-not-exist"]);

		// Assert
		await Assert.That(resolved).IsNull();
	}

	[Test]
	public async Task Find_GivenTheHomeDirectory_NeverReturnsIt()
	{
		// Arrange
		// A '.claude/' or a '.github/' commonly sits in a home directory. Without this bound, a
		// project outside any repository resolves to '~' and installs into it.
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		// Act
		var resolved = SkillRepositoryRoot.Find(home, SkillRepositoryRoot.DefaultMarkers);

		// Assert
		await Assert.That(resolved).IsNull();
	}

	[Test]
	public async Task Find_GivenCustomMarkers_UsesThemInstead(CancellationToken cancellationToken)
	{
		// Arrange
		var repository = CreateTree("custom");
		await File.WriteAllTextAsync(Path.Combine(repository, "my-repo-marker"), "marker\n", cancellationToken);

		// Act
		var resolved = SkillRepositoryRoot.Find(repository, ["my-repo-marker"]);

		// Assert
		await Assert.That(resolved).IsEqualTo(repository);
	}

	[Test]
	public async Task DefaultMarkers_MatchTheSdkDefault()
	{
		// Arrange
		// The SDK's PurviewRepositoryRootMarkers default, verbatim. Drifting apart silently is the
		// whole risk: both are defaults nobody sets, so nothing else would notice.
		var expected = (string[])
			[
				".git/",
				".github/",
				".gitattributes",
				"LICENSE.md",
				"AGENTS.md",
				".agents/",
				"CLAUDE.md",
				".claude/",
				"package.json",
			];

		// Act
		var actual = SkillRepositoryRoot.DefaultMarkers;

		// Assert
		await Assert.That(actual).IsEquivalentTo(expected);
	}
}
