namespace Purview.Skills.Testing.UnitTests;

/// <summary>
/// Tests each rule against a fixture skills folder, one behaviour at a time.
/// </summary>
/// <remarks>
/// Every rule is exercised in both directions - a conformant fixture produces nothing, a broken one
/// produces exactly that rule's violation - because a rule that never fires and a rule that always
/// fires are both green in a repository that happens to be clean.
/// </remarks>
public class SkillPolicyEvaluatorTests
{
	[Test]
	public async Task Discovery_GivenNoSkillsFolder_ReportsNothing()
	{
		// Arrange
		// Authoring skills is optional, so a repository without the folder is not a failure.
		using var fixture = SkillFixture.Create();

		// Act
		var violations = fixture.Evaluator().Discovery();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task Discovery_GivenAnEmptySkillsFolder_ReportsIt()
	{
		// Arrange
		// Every other rule is vacuous against an empty catalogue, so an empty folder - a rename that
		// lost its contents, usually - would otherwise read as a clean run.
		using var fixture = SkillFixture.Create().WithEmptySkillsFolder();

		// Act
		var violations = fixture.Evaluator().Discovery();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Rule).IsEqualTo(SkillRules.Discovery);
	}

	[Test]
	public async Task Structure_GivenAFolderWithNoSkillFile_ReportsIt()
	{
		// Arrange
		using var fixture = SkillFixture.Create().WithValidSkill("good").WithSkill("empty", content: null);

		// Act
		var violations = fixture.Evaluator().Structure();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Skill).IsEqualTo("empty");
	}

	[Test]
	public async Task Names_GivenAConformantSkill_ReportsNothing()
	{
		// Arrange
		using var fixture = SkillFixture.Create().WithValidSkill("purview-buildsdk-configuration");

		// Act
		var violations = fixture.Evaluator().Names();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	[Arguments("Purview-Config", "uppercase is not allowed")]
	[Arguments("-leading", "a leading hyphen is not allowed")]
	[Arguments("trailing-", "a trailing hyphen is not allowed")]
	[Arguments("double--hyphen", "consecutive hyphens are not allowed")]
	[Arguments("under_score", "only hyphens separate words")]
	public async Task Names_GivenAnIllegalName_ReportsIt(string folderName, string because)
	{
		// Arrange
		using var fixture = SkillFixture.Create().WithValidSkill(folderName);

		// Act
		var violations = fixture.Evaluator().Names();

		// Assert
		await Assert.That(violations.Select(violation => violation.Rule)).Contains(SkillRules.Name).Because(because);
	}

	[Test]
	public async Task Names_GivenAFrontmatterNameThatDiffersFromTheFolder_ReportsIt()
	{
		// Arrange
		// The CLI installs by frontmatter name while a human reads the folder name, so a mismatch
		// installs the skill under a name nobody looked for.
		using var fixture = SkillFixture.Create();
		fixture.WithSkill(
			"folder-name",
			"""
			---
			name: different-name
			description: Does a thing.
			---
			"""
		);

		// Act
		var violations = fixture.Evaluator().Names();

		// Assert
		await Assert.That(violations).IsNotEmpty();
		await Assert.That(violations[0].Rule).IsEqualTo(SkillRules.Name);
	}

	[Test]
	public async Task Names_GivenALongName_ReportsTheLimit()
	{
		// Arrange
		var name = string.Join('-', Enumerable.Repeat("segment", 10));
		using var fixture = SkillFixture.Create().WithValidSkill(name);

		// Act
		var violations = fixture.Evaluator().Names();

		// Assert
		await Assert.That(name.Length).IsGreaterThan(SkillPolicyEvaluator.MaximumNameLength);
		await Assert.That(violations).IsNotEmpty();
	}

	[Test]
	public async Task Descriptions_GivenNoDescription_ReportsIt()
	{
		// Arrange
		// The description is loaded at startup and is the only thing an agent weighs when deciding
		// to activate the skill, so without one the skill is installed but never fires.
		using var fixture = SkillFixture.Create();
		fixture.WithSkill(
			"no-description",
			"""
			---
			name: no-description
			---
			"""
		);

		// Act
		var violations = fixture.Evaluator().Descriptions();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Rule).IsEqualTo(SkillRules.Description);
	}

	[Test]
	public async Task Descriptions_GivenAnOverlongDescription_ReportsTheLimit()
	{
		// Arrange
		var description = new string('a', SkillPolicyEvaluator.MaximumDescriptionLength + 1);
		using var fixture = SkillFixture.Create();
		fixture.WithSkill(
			"long-description",
			$"""
			---
			name: long-description
			description: {description}
			---
			"""
		);

		// Act
		var violations = fixture.Evaluator().Descriptions();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
	}

	[Test]
	public async Task OptionalFields_GivenAnUndefinedField_ReportsIt()
	{
		// Arrange
		// 'tags' and 'category' are common inventions. The spec provides 'metadata' precisely so
		// extra properties have a defined home, and a strict client rejects anything else.
		using var fixture = SkillFixture.Create();
		fixture.WithSkill(
			"extra-fields",
			"""
			---
			name: extra-fields
			description: Does a thing.
			category: dotnet
			---
			"""
		);

		// Act
		var violations = fixture.Evaluator().OptionalFields();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Message).Contains("category");
	}

	[Test]
	public async Task OptionalFields_GivenTheDefinedOptionalFields_ReportsNothing()
	{
		// Arrange
		using var fixture = SkillFixture.Create();
		fixture.WithSkill(
			"all-fields",
			"""
			---
			name: all-fields
			description: Does a thing.
			license: MIT
			compatibility: Requires the .NET SDK and MSBuild.
			allowed-tools: Read Bash(dotnet:*)
			metadata:
			  author: purview-dev
			---
			"""
		);

		// Act
		var violations = fixture.Evaluator().OptionalFields();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task BundledResources_GivenALinkToAMissingFile_ReportsIt()
	{
		// Arrange
		// Progressive disclosure only works when the file is there; a dangling reference sends an
		// agent looking for something that does not exist, mid-task.
		using var fixture = SkillFixture.Create();
		fixture.WithValidSkill("dangling", "See [the reference](references/REFERENCE.md) for detail.");

		// Act
		var violations = fixture.Evaluator().BundledResources();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Rule).IsEqualTo(SkillRules.BundledResources);
	}

	[Test]
	public async Task BundledResources_GivenALinkToABundledFile_ReportsNothing()
	{
		// Arrange
		using var fixture = SkillFixture.Create();
		fixture
			.WithValidSkill("bundled", "See [the reference](references/REFERENCE.md) for detail.")
			.WithBundledFile("bundled", "references/REFERENCE.md", "# Reference\n");

		// Act
		var violations = fixture.Evaluator().BundledResources();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task BundledResources_GivenALinkOutsideTheSkill_ReportsIt()
	{
		// Arrange
		// Only the skill's own folder is installed, so a path that climbs out of it resolves
		// nowhere on a consuming machine even though it works in the authoring repository.
		using var fixture = SkillFixture.Create();
		fixture.WithValidSkill("escaping", "See [the other skill](../bundled/SKILL.md).").WithValidSkill("bundled");

		// Act
		var violations = fixture.Evaluator().BundledResources();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Skill).IsEqualTo("escaping");
	}

	[Test]
	public async Task BundledResources_GivenAnExternalUrl_IgnoresIt()
	{
		// Arrange
		using var fixture = SkillFixture.Create();
		fixture.WithValidSkill(
			"external",
			"See [the specification](https://agentskills.io/specification) and [the top](#heading)."
		);

		// Act
		var violations = fixture.Evaluator().BundledResources();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task BundledResources_GivenALinkInsideAFencedBlock_IgnoresIt()
	{
		// Arrange
		// A fenced block holds an example of what a consumer would write, not a link to follow.
		using var fixture = SkillFixture.Create();
		fixture.WithValidSkill(
			"fenced",
			"""
			Add this to your own skill:

			```markdown
			See [the reference](references/REFERENCE.md).
			```
			"""
		);

		// Act
		var violations = fixture.Evaluator().BundledResources();

		// Assert
		await Assert.That(violations).IsEmpty();
	}

	[Test]
	public async Task BodySize_GivenAnOversizedSkillFile_ReportsIt()
	{
		// Arrange
		// The whole file is loaded the moment the skill activates, so its length is a cost paid on
		// every activation rather than only when the detail is needed.
		var body = string.Join('\n', Enumerable.Repeat("Some guidance.", SkillPolicyEvaluator.RecommendedMaximumLines));
		using var fixture = SkillFixture.Create().WithValidSkill("oversized", body);

		// Act
		var violations = fixture.Evaluator().BodySize();

		// Assert
		await Assert.That(violations.Count).IsEqualTo(1);
		await Assert.That(violations[0].Rule).IsEqualTo(SkillRules.BodySize);
	}

	[Test]
	public async Task EveryRule_GivenAConformantSkill_ReportsNothing()
	{
		// Arrange
		using var fixture = SkillFixture.Create();
		fixture
			.WithValidSkill("purview-example", "See [the reference](references/REFERENCE.md).")
			.WithBundledFile("purview-example", "references/REFERENCE.md", "# Reference\n");

		var evaluator = fixture.Evaluator();

		// Act
		var violations = (SkillViolation[])
			[
				.. evaluator.Discovery(),
				.. evaluator.Structure(),
				.. evaluator.Names(),
				.. evaluator.Descriptions(),
				.. evaluator.OptionalFields(),
				.. evaluator.BundledResources(),
				.. evaluator.BodySize(),
			];

		// Assert
		await Assert.That(violations).IsEmpty().Because(SkillPolicyEvaluator.Report(violations));
	}
}
