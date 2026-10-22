using System.Diagnostics.CodeAnalysis;

namespace Purview.Skills.Testing;

/// <summary>
/// The Agent Skills conformance rule set, as TUnit tests.
/// </summary>
/// <remarks>
/// A consuming repository authors its skills at <c>skills/&lt;name&gt;/SKILL.md</c> and adds one
/// derived class:
/// <code>
/// [InheritsTests]
/// public sealed class SkillTests : PurviewSkillTests;
/// </code>
/// <para>
/// <b>The <c>[InheritsTests]</c> attribute is required.</b> TUnit does not walk base types when it
/// discovers tests, so without it the class compiles, the run is green, and nothing is validated -
/// the worst failure this package could have. TUnit's own analyzer reports TUnit0030 when it is
/// missing; treat that as an error rather than a suggestion.
/// </para>
/// <para>
/// Each rule is a separate test so a failure names the rule. Override <see cref="RepositoryRoot"/>
/// when the marker walk cannot find the root (a repository that is not a clone, for instance),
/// <see cref="SkillsFolderName"/> when the skills do not live at <c>skills/</c>, and
/// <see cref="SkipRule"/> to opt out of a rule the repository has not adopted.
/// </para>
/// <para>
/// A repository with no skills folder passes every rule: authoring skills is optional. An
/// <i>empty</i> skills folder fails <see cref="SkillRules.Discovery"/>, because every other rule is
/// vacuous against an empty catalogue.
/// </para>
/// </remarks>
[SuppressMessage(
	"Naming",
	"CA1707:Identifiers should not contain underscores",
	Justification = "These are TUnit tests that run in the consuming repository, so they follow the "
		+ "Subject_GivenCondition_Outcome test-naming convention rather than library member naming. The "
		+ "type ships in a package only because the rule set is shared; it is not an API anyone calls."
)]
public abstract class PurviewSkillTests
{
	/// <summary>The repository root. Defaults to the SDK's repository-root marker walk.</summary>
	protected virtual string? RepositoryRoot => null;

	/// <summary>The skills folder, relative to the repository root.</summary>
	/// <remarks>
	/// Defaults to <c>skills</c> because that is the only location the skills CLI discovers. Change
	/// it only for a repository that is not installed through the CLI.
	/// </remarks>
	protected virtual string SkillsFolderName => SkillCatalogue.DefaultFolderName;

	/// <summary>The evaluator, created once per test class instance.</summary>
	protected SkillPolicyEvaluator Evaluator =>
		field ??= SkillPolicyEvaluator.ForRepository(RepositoryRoot, SkillsFolderName);

	/// <summary>Reports whether a rule should be skipped for this repository.</summary>
	/// <param name="rule">The rule identifier, from <see cref="SkillRules"/>.</param>
	/// <returns><see langword="true"/> to skip the rule.</returns>
	protected virtual bool SkipRule(string rule) => false;

	[Test]
	public Task SkillsFolder_GivenItExists_YieldsSkillsToCheck() =>
		// Runs before the rules it protects: an empty catalogue satisfies all of them.
		AssertRule(SkillRules.Discovery, Evaluator.Discovery());

	[Test]
	public Task Skills_GivenTheirFolders_HaveParseableSkillFiles() =>
		AssertRule(SkillRules.Structure, Evaluator.Structure());

	[Test]
	public Task SkillNames_GivenTheSpecification_AreLegalUniqueAndMatchTheirFolders() =>
		AssertRule(SkillRules.Name, Evaluator.Names());

	[Test]
	public Task SkillDescriptions_GivenTheSpecification_ArePresentAndWithinTheLimit() =>
		AssertRule(SkillRules.Description, Evaluator.Descriptions());

	[Test]
	public Task SkillFrontmatter_GivenItsOptionalFields_DeclaresOnlyDefinedOnes() =>
		AssertRule(SkillRules.OptionalFields, Evaluator.OptionalFields());

	[Test]
	public Task SkillBodies_GivenTheirLinks_ResolveToBundledFiles() =>
		AssertRule(SkillRules.BundledResources, Evaluator.BundledResources());

	[Test]
	public Task SkillFiles_GivenProgressiveDisclosure_StayWithinTheRecommendedSize() =>
		AssertRule(SkillRules.BodySize, Evaluator.BodySize());

	/// <summary>Asserts a rule produced no violations, unless the repository skipped it.</summary>
	/// <param name="rule">The rule identifier.</param>
	/// <param name="violations">The violations the rule produced.</param>
	/// <returns>The assertion.</returns>
	async Task AssertRule(string rule, IReadOnlyList<SkillViolation> violations)
	{
		if (SkipRule(rule))
			return;

		await Assert.That(violations).IsEmpty().Because(SkillPolicyEvaluator.Report(violations));
	}
}
