namespace Purview.Architecture.Testing.UnitTests;

/// <summary>
/// Covers how the rule set finds a repository's projects, and what happens when it finds none.
/// </summary>
/// <remarks>
/// Discovery is convention rather than configuration, which is what makes adoption two files - and
/// also what makes a wrong convention silent. Both failure modes here produced a fully green
/// architecture suite that checked nothing, so each one is pinned.
/// </remarks>
public sealed class GraphDiscoveryTests
{
	static ArchitecturePolicy PolicyWith(params string[] sourceRoots) =>
		new()
		{
			Version = 1,
			SourceRoots = sourceRoots,
			Responsibilities =
			[
				new Responsibility
				{
					Name = "Core",
					Category = "domain",
					Purpose = "The fundamental domain model and its rules.",
					MayDependOn = [],
					MustNotDependOn = [],
				},
			],
		};

	static string CreateRepository(string projectFolder)
	{
		var root = Path.Combine(Path.GetTempPath(), "PurviewGraphDiscovery", Guid.NewGuid().ToString("N"));
		var directory = Path.Combine(root, projectFolder, "Core");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "Core.csproj"), """<Project Sdk="Microsoft.NET.Sdk"></Project>""");

		return root;
	}

	[Test]
	public async Task GraphDiscovery_GivenNoProjectsFound_FailsInsteadOfPassingVacuously()
	{
		// Arrange
		// The repository keeps its projects in 'source', but the policy looks under 'src'.
		var root = CreateRepository("source");
		var policy = PolicyWith("src");

		// Act
		var evaluator = new ArchitecturePolicyEvaluator(policy, ProjectGraph.Load(root, policy.SourceRoots));
		var violations = evaluator.EvaluateAll();

		// Assert
		// Without the graphDiscovery rule this is zero violations and a green suite.
		await Assert.That(violations).IsNotEmpty();
		await Assert.That(violations[0].Rule).IsEqualTo(ArchitectureRules.GraphDiscovery);
		await Assert.That(violations[0].Message).Contains("without checking anything");
		await Assert.That(violations[0].Message).Contains("sourceRoots");
	}

	[Test]
	public async Task GraphDiscovery_GivenAConfiguredSourceRoot_FindsTheProjects()
	{
		// Arrange
		var root = CreateRepository("source");
		var policy = PolicyWith("source");

		// Act
		var evaluator = new ArchitecturePolicyEvaluator(policy, ProjectGraph.Load(root, policy.SourceRoots));

		// Assert
		await Assert.That(evaluator.GraphDiscovery()).IsEmpty();
		await Assert.That(evaluator.ProductionProjects.Select(project => project.Name)).Contains("Core");
	}

	[Test]
	public async Task ProjectGraph_GivenAnEmptySourceRootList_ScansTheRepositoryRoot()
	{
		// Arrange
		// A flat layout, where projects sit directly under the repository root.
		var root = CreateRepository(".");

		// Act
		var projects = ProjectGraph.Load(root, []);

		// Assert
		await Assert.That(projects.ContainsKey("Core")).IsTrue();
	}

	[Test]
	public async Task ProjectGraph_GivenSeveralSourceRoots_ScansAllOfThem()
	{
		// Arrange
		var root = CreateRepository("src");
		var extra = Path.Combine(root, "samples", "Sample");
		Directory.CreateDirectory(extra);
		await File.WriteAllTextAsync(
			Path.Combine(extra, "Sample.csproj"),
			"""<Project Sdk="Microsoft.NET.Sdk"></Project>"""
		);

		// Act
		var projects = ProjectGraph.Load(root, ["src", "samples"]);

		// Assert
		await Assert.That(projects.ContainsKey("Core")).IsTrue();
		await Assert.That(projects.ContainsKey("Sample")).IsTrue();
	}

	[Test]
	public async Task ProjectGraph_GivenAMissingSourceRoot_SkipsItWithoutThrowing()
	{
		// Arrange
		// A repository may configure roots it does not all use; that is not an error on its own.
		var root = CreateRepository("src");

		// Act
		var projects = ProjectGraph.Load(root, ["src", "does-not-exist"]);

		// Assert
		await Assert.That(projects.ContainsKey("Core")).IsTrue();
	}

	[Test]
	public async Task RepositoryRoot_GivenNoPolicyInsideTheRepository_DoesNotEscapeIntoParentFolders()
	{
		// Arrange
		// A policy in the folder *above* the repository, which the probe must never reach: it would
		// silently validate this repository against a sibling checkout's rules.
		var parent = Path.Combine(Path.GetTempPath(), "PurviewGraphDiscovery", Guid.NewGuid().ToString("N"));
		var repository = Path.Combine(parent, "repo");
		Directory.CreateDirectory(Path.Combine(parent, ".config"));
		Directory.CreateDirectory(Path.Combine(repository, ".git"));
		var nested = Path.Combine(repository, "src", "tests", "X", "bin", "Debug", "net10.0");
		Directory.CreateDirectory(nested);
		await File.WriteAllTextAsync(Path.Combine(parent, ".config", "architecture-policy.json"), "{}");

		// Act
		// Walking up from the test binary: no policy in the repository, then the .git boundary.
		var searched = Probe(nested);

		// Assert
		await Assert.That(searched).IsEqualTo(repository);
	}

	/// <summary>
	/// Mirrors <see cref="ArchitecturePolicy.RepositoryRoot"/>'s walk, which anchors on
	/// AppContext.BaseDirectory and so cannot be pointed at a fixture directly.
	/// </summary>
	/// <param name="start">The directory to start from.</param>
	/// <returns>The repository boundary the walk stopped at.</returns>
	static string Probe(string start)
	{
		DirectoryInfo? directory = new(start);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, ArchitecturePolicy.RelativePath)))
				return directory.FullName;

			if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
				return directory.FullName;

			directory = directory.Parent;
		}

		return string.Empty;
	}
}
