using System.Text.RegularExpressions;

namespace Purview.Skills.Testing;

/// <summary>
/// Checks a repository's authored skills against the Agent Skills specification.
/// </summary>
/// <remarks>
/// <para>
/// Each rule returns its own violations rather than throwing, so a test class can report one failure
/// per rule and a repository can skip a rule it has not adopted. The evaluator takes a catalogue, so
/// a rule is tested against a fixture folder rather than against a repository on disk.
/// </para>
/// <para>
/// What is deliberately <b>not</b> checked: prose quality, whether a description triggers reliably,
/// and whether an agent obeys the skill. A test can only check that a skill is well formed - the
/// same boundary the SDK's pack-time <c>ValidatePurviewSkills</c> target draws.
/// </para>
/// <para>
/// The constraints come from the specification at
/// <see href="https://agentskills.io/specification">agentskills.io/specification</see>.
/// </para>
/// </remarks>
public sealed partial class SkillPolicyEvaluator
{
	/// <summary>The maximum length of <c>name</c>.</summary>
	public const int MaximumNameLength = 64;

	/// <summary>The maximum length of <c>description</c>.</summary>
	public const int MaximumDescriptionLength = 1024;

	/// <summary>The maximum length of <c>compatibility</c>.</summary>
	public const int MaximumCompatibilityLength = 500;

	/// <summary>The line count beyond which the specification asks for a referenced file instead.</summary>
	public const int RecommendedMaximumLines = 500;

	/// <summary>
	/// Lowercase alphanumerics and single hyphens, never leading, trailing or consecutive.
	/// </summary>
	[GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex LegalName();

	/// <summary>A Markdown inline link, whose target may be a bundled file.</summary>
	[GeneratedRegex(@"\[[^\]]*\]\((?<target>[^)\s]+)\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLink();

	/// <summary>A fenced code block, whose contents are examples rather than real links.</summary>
	[GeneratedRegex("^```.*?^```", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
	private static partial Regex FencedCodeBlock();

	/// <summary>Creates an evaluator over a catalogue.</summary>
	/// <param name="catalogue">The skills to check.</param>
	public SkillPolicyEvaluator(SkillCatalogue catalogue)
	{
		ArgumentNullException.ThrowIfNull(catalogue);
		Catalogue = catalogue;
	}

	/// <summary>Creates an evaluator for a repository.</summary>
	/// <param name="repositoryRoot">The repository root, or <see langword="null"/> to resolve one.</param>
	/// <param name="folderName">The skills folder name, relative to the root.</param>
	/// <returns>The evaluator.</returns>
	public static SkillPolicyEvaluator ForRepository(
		string? repositoryRoot = null,
		string folderName = SkillCatalogue.DefaultFolderName
	) => new(SkillCatalogue.ForRepository(repositoryRoot, folderName));

	/// <summary>The catalogue under evaluation.</summary>
	public SkillCatalogue Catalogue { get; }

	/// <summary>
	/// Checks that the skills folder, when present, actually holds skills.
	/// </summary>
	/// <returns>The violations.</returns>
	/// <remarks>
	/// Runs before the rules it protects: an empty catalogue satisfies every one of them, so without
	/// this a repository that lost its skills in a rename would report a clean run. A repository with
	/// <i>no</i> skills folder is not failed - authoring skills is optional.
	/// </remarks>
	public IReadOnlyList<SkillViolation> Discovery()
	{
		if (!Catalogue.FolderExists || Catalogue.Skills.Count > 0)
			return [];

		return
		[
			new SkillViolation(
				SkillRules.Discovery,
				string.Empty,
				$"'{Catalogue.SkillsFolder}' exists but holds no skill folders. Every other rule passes vacuously "
					+ "against an empty catalogue, so an empty folder is reported rather than ignored."
			),
		];
	}

	/// <summary>Checks that every skill folder holds a readable <c>SKILL.md</c>.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<SkillViolation> Structure() =>
		[
			.. Catalogue.Skills.SelectMany(skill =>
				skill.StructuralErrors.Select(error => new SkillViolation(
					SkillRules.Structure,
					skill.FolderName,
					error
				))
			),
		];

	/// <summary>Checks the <c>name</c> field against the specification and the folder it sits in.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<SkillViolation> Names()
	{
		List<SkillViolation> violations = [];
		Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);

		foreach (var skill in Catalogue.Skills.Where(skill => skill.Exists))
		{
			if (string.IsNullOrEmpty(skill.Name))
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Name,
						skill.FolderName,
						"frontmatter has no 'name'. The skills CLI requires it and addresses the skill by it."
					)
				);
				continue;
			}

			if (skill.Name.Length > MaximumNameLength)
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Name,
						skill.FolderName,
						$"name is {skill.Name.Length} characters; the specification allows at most {MaximumNameLength}."
					)
				);
			}

			if (!LegalName().IsMatch(skill.Name))
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Name,
						skill.FolderName,
						$"name '{skill.Name}' is not spec-legal. Use lowercase letters, digits and single hyphens, "
							+ "with no leading, trailing or consecutive hyphen."
					)
				);
			}

			// The CLI installs by frontmatter name while a human reads the folder; when they disagree
			// the skill installs under a name nobody expects, and the spec requires them to match.
			if (!string.Equals(skill.Name, skill.FolderName, StringComparison.Ordinal))
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Name,
						skill.FolderName,
						$"frontmatter name is '{skill.Name}' but the folder is '{skill.FolderName}'. They must match."
					)
				);
			}

			if (seen.TryGetValue(skill.Name, out var first))
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Name,
						skill.FolderName,
						$"name '{skill.Name}' is already used by '{first}'. Two skills cannot install under one name."
					)
				);
			}
			else
			{
				seen[skill.Name] = skill.FolderName;
			}
		}

		return violations;
	}

	/// <summary>Checks the <c>description</c> field, which is what makes a skill trigger at all.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<SkillViolation> Descriptions()
	{
		List<SkillViolation> violations = [];

		foreach (var skill in Catalogue.Skills.Where(skill => skill.Exists))
		{
			if (string.IsNullOrWhiteSpace(skill.Description))
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Description,
						skill.FolderName,
						"frontmatter has no non-empty 'description'. It is loaded at startup and is the only thing "
							+ "an agent sees when deciding whether to activate the skill."
					)
				);
				continue;
			}

			if (skill.Description.Length > MaximumDescriptionLength)
			{
				violations.Add(
					new SkillViolation(
						SkillRules.Description,
						skill.FolderName,
						$"description is {skill.Description.Length} characters; the specification allows at most "
							+ $"{MaximumDescriptionLength}. Move the detail into the body or a references/ file."
					)
				);
			}
		}

		return violations;
	}

	/// <summary>Checks the optional frontmatter fields and rejects undefined ones.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<SkillViolation> OptionalFields()
	{
		List<SkillViolation> violations = [];

		foreach (var skill in Catalogue.Skills.Where(skill => skill.Exists))
		{
			foreach (var field in skill.UnknownFields)
			{
				violations.Add(
					new SkillViolation(
						SkillRules.OptionalFields,
						skill.FolderName,
						$"frontmatter declares '{field}', which the specification does not define. The defined fields are "
							+ $"{string.Join(", ", SkillManifestReader.KnownFields)}; put anything else under 'metadata'."
					)
				);
			}

			if (skill.Compatibility is { Length: > MaximumCompatibilityLength })
			{
				violations.Add(
					new SkillViolation(
						SkillRules.OptionalFields,
						skill.FolderName,
						$"compatibility is {skill.Compatibility.Length} characters; the specification allows at most "
							+ $"{MaximumCompatibilityLength}."
					)
				);
			}

			if (skill.Compatibility is { Length: 0 })
			{
				violations.Add(
					new SkillViolation(
						SkillRules.OptionalFields,
						skill.FolderName,
						"compatibility is declared but empty. Omit the field instead; most skills do not need it."
					)
				);
			}
		}

		return violations;
	}

	/// <summary>Checks that every bundled file a body links to exists inside the skill folder.</summary>
	/// <returns>The violations.</returns>
	/// <remarks>
	/// Progressive disclosure only works if the file is there: a link to a missing
	/// <c>references/</c> or <c>scripts/</c> file sends an agent looking for something that does not
	/// exist, mid-task. Links out of the folder are rejected for the same reason the authored path
	/// matters - a relative path that escapes the skill does not survive installation.
	/// </remarks>
	public IReadOnlyList<SkillViolation> BundledResources()
	{
		List<SkillViolation> violations = [];

		foreach (var skill in Catalogue.Skills.Where(skill => skill.Exists))
		{
			// Fenced blocks hold examples and command lines, not links an agent would follow.
			var prose = FencedCodeBlock().Replace(skill.Body, string.Empty);
			var root = Path.GetFullPath(skill.DirectoryPath);

			foreach (var target in MarkdownLink().Matches(prose).Select(match => match.Groups["target"].Value))
			{
				if (!IsBundledPath(target))
					continue;

				var relative = target.Split('#')[0].Replace('/', Path.DirectorySeparatorChar);
				var resolved = Path.GetFullPath(Path.Combine(root, relative));

				if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				{
					violations.Add(
						new SkillViolation(
							SkillRules.BundledResources,
							skill.FolderName,
							$"links to '{target}', which resolves outside the skill folder. Only the skill's own files "
								+ "are installed with it, so the link is broken everywhere except this repository."
						)
					);
					continue;
				}

				if (!File.Exists(resolved) && !Directory.Exists(resolved))
				{
					violations.Add(
						new SkillViolation(
							SkillRules.BundledResources,
							skill.FolderName,
							$"links to '{target}', which does not exist."
						)
					);
				}
			}
		}

		return violations;
	}

	/// <summary>Checks that each <c>SKILL.md</c> stays small enough to be loaded whole.</summary>
	/// <returns>The violations.</returns>
	/// <remarks>
	/// The body is loaded in full the moment a skill activates, so length is a cost paid on every
	/// activation. The specification's guidance is to keep it under 500 lines and move detail into
	/// <c>references/</c>, which is loaded only when it is needed.
	/// </remarks>
	public IReadOnlyList<SkillViolation> BodySize() =>
		[
			.. Catalogue
				.Skills.Where(skill => skill.Exists && skill.LineCount > RecommendedMaximumLines)
				.Select(skill => new SkillViolation(
					SkillRules.BodySize,
					skill.FolderName,
					$"SKILL.md is {skill.LineCount} lines; the specification recommends under {RecommendedMaximumLines}. "
						+ "Move the detail into references/, which an agent loads only when it needs it."
				)),
		];

	/// <summary>Reports whether a link target is a path inside the skill rather than a URL.</summary>
	/// <param name="target">The raw link target.</param>
	/// <returns><see langword="true"/> when it is a bundled relative path.</returns>
	static bool IsBundledPath(string target) =>
		target.Length > 0
		&& !target.StartsWith('#')
		&& !target.StartsWith('/')
		&& !target.Contains("://", StringComparison.Ordinal)
		&& !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

	/// <summary>Renders violations as a readable failure report.</summary>
	/// <param name="violations">The violations.</param>
	/// <returns>The report.</returns>
	public static string Report(IReadOnlyList<SkillViolation> violations)
	{
		ArgumentNullException.ThrowIfNull(violations);

		return violations.Count == 0
			? string.Empty
			: Environment.NewLine + string.Join(Environment.NewLine, violations.Select(violation => $"  {violation}"));
	}
}
