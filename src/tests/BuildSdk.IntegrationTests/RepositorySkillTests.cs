using Purview.Skills.Testing;

namespace Purview.BuildSdk;

/// <summary>
/// Runs the shipped skill rule set against this repository's own authored skills.
/// </summary>
/// <remarks>
/// <para>
/// This repository both authors the skills and ships the package that validates them, so it is the
/// first consumer of its own rule set. Nothing else keeps a <c>SKILL.md</c> honest between the pack
/// that would reject it and the agent that would silently skip it.
/// </para>
/// <para>
/// There is no <c>RepositoryRoot</c> override, deliberately: the default is
/// <see cref="SkillRepositoryRoot"/>'s marker walk, so an ordinary test run exercises the same
/// resolution <c>InstallPurviewSkills</c> uses. If the walk regressed, this class would validate
/// the wrong folder - or none - and <c>SkillsFolder_ResolvesToThisRepository</c> is what catches it.
/// </para>
/// </remarks>
[InheritsTests]
public sealed class RepositorySkillTests : PurviewSkillTests
{
	[Test]
	public async Task SkillsFolder_ResolvesToThisRepository()
	{
		// The inherited rules all pass vacuously against a catalogue that was never found, so the
		// adoption story is only true if the default resolution lands on this repository's skills.
		var catalogue = Evaluator.Catalogue;

		await Assert
			.That(catalogue.FolderExists)
			.IsTrue()
			.Because($"the marker walk resolved '{catalogue.RepositoryRoot}', whose skills folder does not exist");

		await Assert
			.That(File.Exists(Path.Combine(catalogue.RepositoryRoot, "AGENTS.md")))
			.IsTrue()
			.Because($"'{catalogue.RepositoryRoot}' does not look like this repository");

		await Assert.That(catalogue.Skills).IsNotEmpty();
	}

	[Test]
	public async Task EverySkill_IsNamedForThisProduct()
	{
		// Skills install into one flat directory per agent, shared with every skill a developer has
		// installed from anywhere. An unprefixed name - 'sdk-configuration', 'project-placement' -
		// collides with somebody else's, and the loser is whichever installed second.
		var unprefixed = Evaluator
			.Catalogue.Skills.Select(skill => skill.FolderName)
			.Where(name => !name.StartsWith("purview-", StringComparison.Ordinal))
			.ToArray();

		await Assert
			.That(unprefixed)
			.IsEmpty()
			.Because(
				"every skill this repository publishes must carry a vendor prefix: " + string.Join(", ", unprefixed)
			);
	}
}
