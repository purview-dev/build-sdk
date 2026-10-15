namespace Purview.Skills.Testing.UnitTests;

/// <summary>
/// Tests the frontmatter parser directly, because every rule downstream of it believes what it says.
/// </summary>
public class SkillManifestReaderTests
{
	static SkillManifest Parse(string text) =>
		SkillManifestReader.Parse("example", "/skills/example", "/skills/example/SKILL.md", text);

	[Test]
	public async Task Parse_GivenMinimalFrontmatter_ReadsNameAndDescription()
	{
		// Arrange
		var text = """
			---
			name: example
			description: Does a thing.
			---

			# Example
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsEmpty();
		await Assert.That(manifest.Name).IsEqualTo("example");
		await Assert.That(manifest.Description).IsEqualTo("Does a thing.");
	}

	[Test]
	public async Task Parse_GivenAQuotedValue_StripsTheQuotes()
	{
		// Arrange
		var text = """
			---
			name: example
			description: "Does a thing. Use when a thing needs doing."
			---
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.Description).IsEqualTo("Does a thing. Use when a thing needs doing.");
	}

	[Test]
	public async Task Parse_GivenAFoldedBlockScalar_JoinsTheIndentedLines()
	{
		// Arrange
		// A long description is routinely written as a block scalar, so a parser that only handles
		// the inline form reports a missing description on a perfectly valid skill.
		var text = """
			---
			name: example
			description: >-
			  Does a thing across several lines.
			  Use when a thing needs doing.
			---
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsEmpty();
		await Assert
			.That(manifest.Description)
			.IsEqualTo("Does a thing across several lines. Use when a thing needs doing.");
	}

	[Test]
	public async Task Parse_GivenAMetadataMapping_ReadsItsEntries()
	{
		// Arrange
		var text = """
			---
			name: example
			description: Does a thing.
			metadata:
			  author: purview-dev
			  version: "1.0"
			---
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsEmpty();
		await Assert.That(manifest.Metadata["author"]).IsEqualTo("purview-dev");
		await Assert.That(manifest.Metadata["version"]).IsEqualTo("1.0");
		// 'metadata' is a defined field, so reading it must not report it as unknown.
		await Assert.That(manifest.UnknownFields).IsEmpty();
	}

	[Test]
	public async Task Parse_GivenAnUndefinedField_RecordsItAsUnknown()
	{
		// Arrange
		var text = """
			---
			name: example
			description: Does a thing.
			tags:
			  - dotnet
			---
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.UnknownFields).Contains("tags");
	}

	[Test]
	public async Task Parse_GivenNoOpeningDelimiter_ReportsAStructuralError()
	{
		// Arrange
		var text = "# Example\n\nNo frontmatter at all.\n";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsNotEmpty();
		await Assert.That(manifest.Name).IsNull();
	}

	[Test]
	public async Task Parse_GivenNoClosingDelimiter_ReportsAStructuralError()
	{
		// Arrange
		var text = "---\nname: example\ndescription: Does a thing.\n";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsNotEmpty();
	}

	[Test]
	public async Task Parse_GivenADuplicateField_ReportsAStructuralError()
	{
		// Arrange
		// YAML's last-wins behaviour would hide the fact that two descriptions were authored, and
		// which one an agent sees would depend on the client's parser.
		var text = """
			---
			name: example
			description: First.
			description: Second.
			---
			""";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsNotEmpty();
		await Assert.That(manifest.Description).IsEqualTo("First.");
	}

	[Test]
	public async Task Read_GivenAFolderWithoutASkillFile_ReportsItRatherThanSkipping()
	{
		// Arrange
		using var fixture = SkillFixture.Create().WithSkill("orphan", content: null);

		// Act
		var manifest = SkillManifestReader.Read(Path.Combine(fixture.SkillsFolder, "orphan"));

		// Assert
		await Assert.That(manifest.Exists).IsFalse();
		await Assert.That(manifest.StructuralErrors).IsNotEmpty();
	}

	[Test]
	public async Task Parse_GivenCarriageReturns_CountsLinesTheSameWayOnEveryPlatform()
	{
		// Arrange
		// A file committed with CRLF endings must not be measured as one enormous line, or the
		// size rule reports differently depending on who checked the repository out.
		var text = "---\r\nname: example\r\ndescription: Does a thing.\r\n---\r\n\r\n# Example\r\n";

		// Act
		var manifest = Parse(text);

		// Assert
		await Assert.That(manifest.StructuralErrors).IsEmpty();
		await Assert.That(manifest.LineCount).IsEqualTo(7);
	}
}
