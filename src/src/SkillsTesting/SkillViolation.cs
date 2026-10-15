namespace Purview.Skills.Testing;

/// <summary>A single way one skill departs from the Agent Skills specification.</summary>
/// <param name="Rule">The rule identifier, from <see cref="SkillRules"/>.</param>
/// <param name="Skill">The skill's folder name, or an empty string for a repository-wide problem.</param>
/// <param name="Message">What is wrong, and what would make it right.</param>
public sealed record SkillViolation(string Rule, string Skill, string Message)
{
	/// <summary>Renders the violation as one line of a failure report.</summary>
	/// <returns>The rendered violation.</returns>
	public override string ToString() => Skill.Length == 0 ? Message : $"{Skill}: {Message}";
}

/// <summary>The rule identifiers, so a skip or a report names a rule rather than a string literal.</summary>
public static class SkillRules
{
	/// <summary>The skills folder exists and holds at least one skill.</summary>
	public const string Discovery = "skills-discovery";

	/// <summary>Every skill folder holds a <c>SKILL.md</c> whose frontmatter parses.</summary>
	public const string Structure = "skill-structure";

	/// <summary>Every <c>name</c> is spec-legal, unique, and matches its folder.</summary>
	public const string Name = "skill-name";

	/// <summary>Every <c>description</c> is present, non-empty and within 1024 characters.</summary>
	public const string Description = "skill-description";

	/// <summary>Optional frontmatter fields are spec-defined and within their limits.</summary>
	public const string OptionalFields = "skill-optional-fields";

	/// <summary>Every bundled file a body links to exists, inside the skill folder.</summary>
	public const string BundledResources = "skill-bundled-resources";

	/// <summary>Each <c>SKILL.md</c> stays within the size progressive disclosure assumes.</summary>
	public const string BodySize = "skill-body-size";
}
