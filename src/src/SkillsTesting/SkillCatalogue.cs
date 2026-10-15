namespace Purview.Skills.Testing;

/// <summary>Every skill authored in a repository, read from the skills folder.</summary>
/// <remarks>
/// The catalogue reads files and nothing else - no CLI, no network, no Node - so it behaves
/// identically on a developer machine and on a CI runner that has none of them.
/// </remarks>
public sealed class SkillCatalogue
{
	/// <summary>The conventional folder name the skills CLI discovers skills in.</summary>
	/// <remarks>
	/// The CLI only looks at a <i>root</i> <c>skills/</c> folder, which is why skills are authored
	/// there rather than beside the package content they ship in.
	/// </remarks>
	public const string DefaultFolderName = "skills";

	SkillCatalogue(string repositoryRoot, string skillsFolder, bool folderExists, IReadOnlyList<SkillManifest> skills)
	{
		RepositoryRoot = repositoryRoot;
		SkillsFolder = skillsFolder;
		FolderExists = folderExists;
		Skills = skills;
	}

	/// <summary>The repository root the catalogue was loaded from.</summary>
	public string RepositoryRoot { get; }

	/// <summary>The absolute path to the skills folder, whether or not it exists.</summary>
	public string SkillsFolder { get; }

	/// <summary>Whether the skills folder exists.</summary>
	/// <remarks>
	/// A repository that authors no skills is not a failure, so every rule is vacuous when this is
	/// <see langword="false"/>. An <i>empty</i> folder is different: it is almost always a rename or
	/// a move that lost its contents, and <see cref="SkillPolicyEvaluator.Discovery"/> reports it.
	/// </remarks>
	public bool FolderExists { get; }

	/// <summary>Every skill folder found, in folder-name order.</summary>
	public IReadOnlyList<SkillManifest> Skills { get; }

	/// <summary>Loads the catalogue for a repository.</summary>
	/// <param name="repositoryRoot">The repository root, or <see langword="null"/> to resolve one.</param>
	/// <param name="folderName">The skills folder name, relative to the root.</param>
	/// <returns>The catalogue.</returns>
	/// <exception cref="InvalidOperationException">No repository root could be resolved.</exception>
	public static SkillCatalogue ForRepository(string? repositoryRoot = null, string folderName = DefaultFolderName)
	{
		var root =
			repositoryRoot
			?? SkillRepositoryRoot.Find()
			?? throw new InvalidOperationException(
				$"Could not resolve a repository root above '{AppContext.BaseDirectory}'. Expected one of "
					+ $"{string.Join(", ", SkillRepositoryRoot.DefaultMarkers)}, or override RepositoryRoot on the test class. "
					+ "The search deliberately stops below the home directory rather than continuing into it."
			);

		return ForFolder(root, Path.Combine(root, folderName));
	}

	/// <summary>Loads the catalogue from an explicit skills folder.</summary>
	/// <param name="repositoryRoot">The repository root, used only for reporting.</param>
	/// <param name="skillsFolder">The folder holding one directory per skill.</param>
	/// <returns>The catalogue.</returns>
	public static SkillCatalogue ForFolder(string repositoryRoot, string skillsFolder)
	{
		ArgumentException.ThrowIfNullOrEmpty(skillsFolder);

		if (!Directory.Exists(skillsFolder))
			return new SkillCatalogue(repositoryRoot, skillsFolder, folderExists: false, skills: []);

		var skills = Directory
			.EnumerateDirectories(skillsFolder)
			.OrderBy(Path.GetFileName, StringComparer.Ordinal)
			.Select(SkillManifestReader.Read)
			.ToArray();

		return new SkillCatalogue(repositoryRoot, skillsFolder, folderExists: true, skills);
	}
}
