namespace Purview.Skills.Testing;

/// <summary>One authored skill, as read from its folder.</summary>
/// <remarks>
/// A manifest is produced for every folder under the skills root, including one with no
/// <c>SKILL.md</c> and one whose frontmatter does not parse. Those are reported as violations rather
/// than skipped, because a skill the CLI silently ignores is exactly the failure worth catching.
/// </remarks>
public sealed record SkillManifest
{
	/// <summary>The folder name, which the CLI shows a human and the spec requires the name to match.</summary>
	public required string FolderName { get; init; }

	/// <summary>The absolute path to the skill folder.</summary>
	public required string DirectoryPath { get; init; }

	/// <summary>The absolute path to the folder's <c>SKILL.md</c>, whether or not it exists.</summary>
	public required string SkillFilePath { get; init; }

	/// <summary>Whether a <c>SKILL.md</c> was found.</summary>
	public bool Exists { get; init; }

	/// <summary>The declared <c>name</c>, or <see langword="null"/> when the field is absent.</summary>
	public string? Name { get; init; }

	/// <summary>The declared <c>description</c>, or <see langword="null"/> when the field is absent.</summary>
	public string? Description { get; init; }

	/// <summary>The optional <c>license</c>.</summary>
	public string? License { get; init; }

	/// <summary>The optional <c>compatibility</c>.</summary>
	public string? Compatibility { get; init; }

	/// <summary>The optional <c>allowed-tools</c>, a space-separated tool list.</summary>
	public string? AllowedTools { get; init; }

	/// <summary>The optional <c>metadata</c> mapping.</summary>
	public IReadOnlyDictionary<string, string> Metadata { get; init; } =
		new Dictionary<string, string>(System.StringComparer.Ordinal);

	/// <summary>Top-level frontmatter keys the specification does not define.</summary>
	/// <remarks>
	/// Reported rather than ignored: a client that validates strictly rejects them, and the spec
	/// provides <c>metadata</c> precisely so extra properties have a defined home.
	/// </remarks>
	public IReadOnlyList<string> UnknownFields { get; init; } = [];

	/// <summary>Problems that stopped the file being read as a skill at all.</summary>
	public IReadOnlyList<string> StructuralErrors { get; init; } = [];

	/// <summary>The Markdown body, everything after the closing frontmatter delimiter.</summary>
	public string Body { get; init; } = string.Empty;

	/// <summary>The whole file's line count, which the specification asks to keep under 500.</summary>
	public int LineCount { get; init; }
}
