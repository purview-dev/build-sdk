using System.Diagnostics.CodeAnalysis;

namespace Purview.Architecture.Testing;

/// <summary>
/// The standard architecture rule set, as TUnit tests.
/// </summary>
/// <remarks>
/// A consuming repository adds a policy file and one derived class:
/// <code>
/// [InheritsTests]
/// public sealed class ArchitectureTests : PurviewArchitectureTests;
/// </code>
/// <para>
/// <b>The <c>[InheritsTests]</c> attribute is required.</b> TUnit does not walk base types when it
/// discovers tests, so without it the class compiles, the run is green, and nothing is enforced -
/// the worst failure this package could have. TUnit's own analyzer reports TUnit0030 when it is
/// missing; treat that as an error rather than a suggestion.
/// </para>
/// Each rule is a separate test so a failure names the rule, and the whole set does not collapse
/// into one assertion. Override <see cref="RepositoryRoot"/> when the policy does not sit above the
/// test binary, and <see cref="SkipRule"/> to opt out of a rule the repository has not adopted yet -
/// preferring a recorded waiver, which is narrower and stays visible.
/// </remarks>
[SuppressMessage(
	"Naming",
	"CA1707:Identifiers should not contain underscores",
	Justification = "These are TUnit tests that run in the consuming repository, so they follow the "
		+ "Subject_GivenCondition_Outcome test-naming convention rather than library member naming. The "
		+ "type ships in a package only because the rule set is shared; it is not an API anyone calls."
)]
public abstract class PurviewArchitectureTests
{
	ArchitecturePolicyEvaluator? _evaluator;

	/// <summary>The repository root. Defaults to probing upward for the policy file.</summary>
	protected virtual string? RepositoryRoot => null;

	/// <summary>The evaluator, created once per test class instance.</summary>
	protected ArchitecturePolicyEvaluator Evaluator =>
		_evaluator ??= ArchitecturePolicyEvaluator.ForRepository(RepositoryRoot);

	/// <summary>Reports whether a rule should be skipped for this repository.</summary>
	/// <param name="rule">The rule identifier, from <see cref="ArchitectureRules"/>.</param>
	/// <returns><see langword="true"/> to skip the rule.</returns>
	/// <remarks>
	/// Prefer a waiver in the policy: it is per project rather than per repository, carries a reason,
	/// and is itself checked. Skipping a rule wholesale removes it with no record.
	/// </remarks>
	protected virtual bool SkipRule(string rule) => false;

	[Test]
	public async Task Policy_GivenTheSchema_Validates()
	{
		// A policy that does not match its schema may be silently missing a rule, so this runs first.
		var failures = PolicySchemaValidator.Validate(
			Path.Combine(RepositoryRoot ?? ArchitecturePolicy.RepositoryRoot(), ArchitecturePolicy.RelativePath)
		);

		await Assert
			.That(failures)
			.IsEmpty()
			.Because(
				Environment.NewLine + string.Join(Environment.NewLine, failures.Select(failure => $"  {failure}"))
			);
	}

	[Test]
	public Task Repository_GivenItsSourceRoots_YieldsProjectsToCheck() =>
		// Runs before the rules it protects: an empty graph satisfies all of them.
		AssertRule(ArchitectureRules.GraphDiscovery, Evaluator.GraphDiscovery());

	[Test]
	public Task ProductionProjects_GivenTheirNames_MapToAResponsibility() =>
		AssertRule(ArchitectureRules.Naming, Evaluator.Naming());

	[Test]
	public Task ProductionProjects_GivenTheirNames_AvoidCatchAllNames() =>
		AssertRule(ArchitectureRules.DiscouragedName, Evaluator.DiscouragedNames());

	[Test]
	public Task ProductionProjects_GivenTheirReferences_RespectTheDependencyMatrix() =>
		AssertRule(ArchitectureRules.DependencyMatrix, Evaluator.DependencyMatrix());

	[Test]
	public Task SiblingImplementations_GivenTheirReferences_DoNotDependOnEachOther() =>
		AssertRule(ArchitectureRules.SiblingIndependence, Evaluator.SiblingIndependence());

	[Test]
	public Task Implementations_GivenTheirReferences_DoNotDependOnComposition() =>
		AssertRule(ArchitectureRules.CompositionBoundary, Evaluator.CompositionBoundary());

	[Test]
	public Task ProductionProjects_GivenTheirPackages_AvoidForbiddenFrameworkDependencies() =>
		AssertRule(ArchitectureRules.ForbiddenPackage, Evaluator.ForbiddenPackages());

	[Test]
	public Task ProjectGraph_GivenItsReferences_HasNoCycles() =>
		AssertRule(ArchitectureRules.Cycle, Evaluator.Cycles());

	[Test]
	public Task ProductionProjects_GivenTheirReferences_DoNotReferenceATestProject() =>
		AssertRule(ArchitectureRules.TestReference, Evaluator.TestReferences());

	[Test]
	public Task PolicyWaivers_GivenTheirDefinitions_AreJustifiedAndStillApply() =>
		AssertRule(ArchitectureRules.WaiverIntegrity, Evaluator.WaiverIntegrity());

	/// <summary>Asserts a rule produced no violations, unless the repository skipped it.</summary>
	/// <param name="rule">The rule identifier.</param>
	/// <param name="violations">The violations the rule produced.</param>
	/// <returns>The assertion.</returns>
	async Task AssertRule(string rule, IReadOnlyList<ArchitectureViolation> violations)
	{
		if (SkipRule(rule))
			return;

		await Assert.That(violations).IsEmpty().Because(ArchitecturePolicyEvaluator.Report(violations));
	}
}
