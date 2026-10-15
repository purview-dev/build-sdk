namespace Purview.Architecture.Testing.UnitTests;

/// <summary>
/// Exercises the rule engine directly, against graphs built in memory.
/// </summary>
/// <remarks>
/// The evaluator is deliberately free of MSBuild and test-framework types, which is what lets these
/// tests state a rule as a three-project graph rather than a repository on disk. A rule tested only
/// through a real solution is a rule you can only break by breaking the solution.
/// </remarks>
public sealed class ArchitecturePolicyEvaluatorTests
{
	static ProjectNode Project(
		string name,
		IReadOnlyList<string>? references = null,
		IReadOnlyList<string>? packages = null,
		bool isTest = false
	) => new(name, $"/repo/src/{name}/{name}.csproj", references ?? [], packages ?? [], isTest);

	static ArchitecturePolicyEvaluator Evaluator(ArchitecturePolicy policy, params ProjectNode[] projects) =>
		new(policy, projects.ToDictionary(project => project.Name, StringComparer.OrdinalIgnoreCase));

	static ArchitecturePolicy Policy(params Responsibility[] responsibilities) =>
		new()
		{
			Version = 1,
			Responsibilities = responsibilities,
			DiscouragedProjectNames = new DiscouragedNames
			{
				Names = ["Infrastructure", "Services"],
				Reason = "These name a category rather than a responsibility, so nothing can be excluded from them.",
			},
		};

	static Responsibility Core =>
		new()
		{
			Name = "Core",
			Aliases = ["Domain"],
			Category = "domain",
			Purpose = "The fundamental domain model and its rules.",
			MayDependOn = ["Shared"],
			MustNotDependOn = ["Application", "Persistence", "Composition"],
			ForbiddenPackagePrefixes = ["Microsoft.EntityFrameworkCore"],
		};

	static Responsibility Application =>
		new()
		{
			Name = "Application",
			Category = "application",
			Purpose = "Coordinates use cases and workflows.",
			MayDependOn = ["Core"],
			MustNotDependOn = ["Persistence", "Composition"],
		};

	static Responsibility Persistence =>
		new()
		{
			Name = "Persistence",
			Category = "infrastructure",
			Purpose = "Durable data access.",
			MayDependOn = ["Core", "Application"],
			MustNotDependOn = ["Composition"],
			SiblingIndependence = true,
		};

	static Responsibility Shared =>
		new()
		{
			Name = "Shared",
			Category = "shared",
			Purpose = "Small, broadly reusable, domain-independent utilities.",
			MayDependOn = [],
			MustNotDependOn = [],
		};

	static Responsibility Composition =>
		new()
		{
			Name = "Composition",
			Category = "composition",
			Purpose = "Dependency injection registration and implementation selection.",
			MayDependOn = ["Core", "Application", "Persistence"],
			MustNotDependOn = [],
		};

	[Test]
	public async Task DependencyMatrix_GivenAForbiddenReference_ReportsItWithTheCorrection()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core, Application, Persistence),
			Project("Core", ["Persistence"]),
			Project("Persistence")
		);

		// Act
		var violations = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Rule).IsEqualTo(ArchitectureRules.DependencyMatrix);
		await Assert.That(violations[0].Project).IsEqualTo("Core");
		// The message has to carry the correction, not just the fact.
		await Assert.That(violations[0].Message).Contains("must not depend on Persistence");
		await Assert.That(violations[0].Message).Contains("Invert it");
	}

	[Test]
	public async Task DependencyMatrix_GivenAPermittedReference_ReportsNothing()
	{
		// Arrange
		var evaluator = Evaluator(Policy(Core, Application), Project("Application", ["Core"]), Project("Core"));

		// Act
		var violations = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task DependencyMatrix_GivenAnImplementationQualifiedName_ClassifiesByTheFirstSegment()
	{
		// Arrange
		// Persistence.SqlServer is Persistence; qualification is a naming choice, never a different rule.
		var evaluator = Evaluator(
			Policy(Core, Application, Persistence),
			Project("Persistence.SqlServer", ["Core"]),
			Project("Core")
		);

		// Act
		var violations = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task DependencyMatrix_GivenAnAlias_ClassifiesIt()
	{
		// Arrange
		// 'Domain' is a recognised alias for Core, so Core's rules apply to it.
		var evaluator = Evaluator(
			Policy(Core, Application, Persistence),
			Project("Domain", ["Persistence"]),
			Project("Persistence")
		);

		// Act
		var violations = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Project).IsEqualTo("Domain");
	}

	[Test]
	public async Task DependencyMatrix_GivenAReferenceThatIsNeitherPermittedNorForbidden_ReportsIt()
	{
		// Arrange
		// Silence on a dependency is not permission; an unlisted edge is a gap in the policy.
		// Shared appears in neither Application's mayDependOn nor its mustNotDependOn.
		var evaluator = Evaluator(
			Policy(Core, Application, Shared),
			Project("Application", ["Shared"]),
			Project("Shared")
		);

		// Act
		var violations = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Message).Contains("neither permits nor forbids");
	}

	[Test]
	public async Task SiblingIndependence_GivenAQualifiedSiblingReference_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core, Persistence),
			Project("Persistence.SqlServer", ["Persistence.PostgreSql"]),
			Project("Persistence.PostgreSql")
		);

		// Act
		var violations = evaluator.SiblingIndependence();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Message).Contains("must stay independent");
	}

	[Test]
	public async Task SiblingIndependence_GivenAReferenceToTheUnqualifiedProject_AllowsIt()
	{
		// Arrange
		// The unqualified project is shared ground, which is the whole reason for having one.
		var evaluator = Evaluator(
			Policy(Core, Persistence),
			Project("Persistence.SqlServer", ["Persistence"]),
			Project("Persistence")
		);

		// Act
		var violations = evaluator.SiblingIndependence();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task CompositionBoundary_GivenAnImplementationReferencingComposition_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core, Persistence, Composition),
			Project("Persistence", ["Composition"]),
			Project("Composition")
		);

		// Act
		var violations = evaluator.CompositionBoundary();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Message).Contains("circular");
	}

	[Test]
	public async Task ForbiddenPackages_GivenAMatchingPrefix_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(Policy(Core), Project("Core", packages: ["Microsoft.EntityFrameworkCore.SqlServer"]));

		// Act
		var violations = evaluator.ForbiddenPackages();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Rule).IsEqualTo(ArchitectureRules.ForbiddenPackage);
	}

	[Test]
	public async Task ForbiddenPackages_GivenAnUnrelatedPackage_AllowsIt()
	{
		// Arrange
		var evaluator = Evaluator(Policy(Core), Project("Core", packages: ["System.Text.Json"]));

		// Act
		var violations = evaluator.ForbiddenPackages();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task Cycles_GivenACircularGraph_ReportsThePath()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core, Application),
			Project("Core", ["Application"]),
			Project("Application", ["Core"])
		);

		// Act
		var violations = evaluator.Cycles();

		// Assert
		await Assert.That(violations).IsNotEmpty();
		await Assert.That(violations[0].Message).Contains("->");
	}

	[Test]
	public async Task TestReferences_GivenProductionReferencingATestProject_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core),
			Project("Core", ["Core.UnitTests"]),
			Project("Core.UnitTests", isTest: true)
		);

		// Act
		var violations = evaluator.TestReferences();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Message).Contains("must not ship");
	}

	[Test]
	public async Task Naming_GivenAnUnknownProjectName_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(Policy(Core), Project("Widgets"));

		// Act
		var violations = evaluator.Naming();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Message).Contains("Known responsibilities");
	}

	[Test]
	public async Task DiscouragedNames_GivenACatchAllName_ReportsIt()
	{
		// Arrange
		var evaluator = Evaluator(Policy(Core), Project("Infrastructure"));

		// Act
		var violations = evaluator.DiscouragedNames();

		// Assert
		await Assert.That(violations).HasSingleItem();
		await Assert.That(violations[0].Rule).IsEqualTo(ArchitectureRules.DiscouragedName);
	}

	[Test]
	public async Task Waiver_GivenAMappedProject_ExcusesTheNameButKeepsTheDependencyRules()
	{
		// Arrange
		// The point of a waiver: the name is excused, the direction is not.
		var policy = Policy(Core, Persistence, Composition);
		policy = new ArchitecturePolicy
		{
			Version = policy.Version,
			Responsibilities = policy.Responsibilities,
			DiscouragedProjectNames = policy.DiscouragedProjectNames,
			Waivers =
			[
				new PolicyWaiver
				{
					Project = "Infrastructure",
					MapsTo = "Persistence",
					Rules = [ArchitectureRules.Naming, ArchitectureRules.DiscouragedName],
					Reason = "Holds one adapter and predates the catalogue; renaming churns the docs for no gain.",
				},
			],
		};

		var evaluator = Evaluator(policy, Project("Infrastructure", ["Composition"]), Project("Composition"));

		// Act
		var naming = evaluator.Naming();
		var discouraged = evaluator.DiscouragedNames();
		var matrix = evaluator.DependencyMatrix();

		// Assert
		await Assert.That(naming).IsEmpty();
		await Assert.That(discouraged).IsEmpty();
		// Still classified as Persistence, so Persistence's rules bite.
		await Assert.That(matrix).HasSingleItem();
		await Assert.That(matrix[0].Message).Contains("must not depend on Composition");
	}

	[Test]
	public async Task WaiverIntegrity_GivenAWaiverForAMissingProject_ReportsIt()
	{
		// Arrange
		var policy = new ArchitecturePolicy
		{
			Version = 1,
			Responsibilities = [Core],
			Waivers =
			[
				new PolicyWaiver
				{
					Project = "Deleted",
					Rules = [ArchitectureRules.Naming],
					Reason = "A reason long enough to pass the schema's minimum length requirement.",
				},
			],
		};

		var evaluator = Evaluator(policy, Project("Core"));

		// Act
		var violations = evaluator.WaiverIntegrity();

		// Assert
		await Assert.That(violations).IsNotEmpty();
		await Assert.That(violations[0].Message).Contains("hides the next one");
	}

	[Test]
	public async Task EvaluateAll_GivenACleanSolution_ReportsNothing()
	{
		// Arrange
		var evaluator = Evaluator(
			Policy(Core, Application, Persistence, Composition),
			Project("Core"),
			Project("Application", ["Core"]),
			Project("Persistence", ["Core", "Application"]),
			Project("Composition", ["Core", "Application", "Persistence"])
		);

		// Act
		var violations = evaluator.EvaluateAll();

		// Assert
		await Assert.That(violations).IsEmpty().Because(ArchitecturePolicyEvaluator.Report(violations));
	}

	[Test]
	public async Task TestProjects_GivenTheirReferences_AreNotGovernedByTheMatrix()
	{
		// Arrange
		// A test project references everything by design; applying the matrix to it would make the
		// rule set unusable.
		var evaluator = Evaluator(
			Policy(Core, Persistence),
			Project("Core.UnitTests", ["Core", "Persistence"], isTest: true),
			Project("Core"),
			Project("Persistence")
		);

		// Act
		var violations = evaluator.EvaluateAll();

		// Assert
		await Assert.That(violations).IsEmpty().Because(ArchitecturePolicyEvaluator.Report(violations));
	}
}
