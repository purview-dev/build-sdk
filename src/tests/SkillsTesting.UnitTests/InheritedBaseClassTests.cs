namespace Purview.Skills.Testing.UnitTests;

/// <summary>
/// Proves TUnit discovers the tests a consuming repository inherits from
/// <see cref="PurviewSkillTests"/>.
/// </summary>
/// <remarks>
/// The whole adoption story is "author skills and add one derived class", so test discovery through
/// inheritance is load-bearing. If TUnit did not walk base types, a consumer would add the class,
/// see a green run, and be validating nothing at all - the worst possible failure for this package.
///
/// It points at a fixture repository rather than this one so the assertion is about discovery and
/// not about whatever happens to be authored in <c>skills/</c> today. This repository's real skills
/// are checked by <c>RepositorySkillTests</c> in BuildSdk.IntegrationTests.
/// </remarks>
[InheritsTests]
public sealed class InheritedBaseClassTests : PurviewSkillTests
{
	static readonly Lazy<string> Fixture = new(CreateFixtureRepository);

	/// <inheritdoc />
	protected override string? RepositoryRoot => Fixture.Value;

	/// <summary>Writes a minimal, spec-conformant skills folder to a temporary location.</summary>
	/// <returns>The fixture repository root.</returns>
	static string CreateFixtureRepository()
	{
		var root = Path.Combine(Path.GetTempPath(), "PurviewSkillInheritanceFixture", Guid.NewGuid().ToString("N"));
		var skill = Path.Combine(root, "skills", "example-skill");
		Directory.CreateDirectory(Path.Combine(skill, "references"));

		File.WriteAllText(
			Path.Combine(skill, "SKILL.md"),
			"""
			---
			name: example-skill
			description: Demonstrates a conformant skill. Use when checking that the rule set passes a skill that is correct.
			license: MIT
			metadata:
			  author: purview-dev
			---

			# Example skill

			This skill exists so the rule set has something correct to agree with.

			See [the reference](references/REFERENCE.md) for the detail an agent loads only on demand.
			"""
		);

		File.WriteAllText(Path.Combine(skill, "references", "REFERENCE.md"), "# Reference\n\nDetail.\n");

		return root;
	}
}
