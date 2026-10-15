namespace Purview.Skills.Testing.UnitTests;

/// <summary>
/// A throwaway repository on disk, holding a <c>skills/</c> folder the rules can be run against.
/// </summary>
/// <remarks>
/// The rules read files, so a fixture is a folder rather than an in-memory graph. Every path is
/// built with <see cref="Path.Combine(string, string)"/> under <see cref="Path.GetTempPath"/>, so
/// the tests behave identically on Windows, Linux and macOS.
/// </remarks>
sealed class SkillFixture : IDisposable
{
	SkillFixture(string root)
	{
		Root = root;
		SkillsFolder = Path.Combine(root, "skills");
	}

	/// <summary>The fixture repository root.</summary>
	public string Root { get; }

	/// <summary>The fixture's skills folder.</summary>
	public string SkillsFolder { get; }

	/// <summary>Creates an empty fixture repository, without a skills folder.</summary>
	/// <returns>The fixture.</returns>
	public static SkillFixture Create()
	{
		var root = Path.Combine(Path.GetTempPath(), "PurviewSkillFixture", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		return new SkillFixture(root);
	}

	/// <summary>Writes a skill folder with the given <c>SKILL.md</c> content.</summary>
	/// <param name="folderName">The folder name.</param>
	/// <param name="content">The file's content, or <see langword="null"/> to create no file.</param>
	/// <returns>This fixture, for chaining.</returns>
	public SkillFixture WithSkill(string folderName, string? content)
	{
		var directory = Path.Combine(SkillsFolder, folderName);
		Directory.CreateDirectory(directory);

		if (content is not null)
			File.WriteAllText(Path.Combine(directory, "SKILL.md"), content);

		return this;
	}

	/// <summary>Writes a well-formed skill, so a test can vary one thing at a time.</summary>
	/// <param name="folderName">The folder name, which is also the declared name.</param>
	/// <param name="body">Optional body content, appended after the heading.</param>
	/// <returns>This fixture, for chaining.</returns>
	public SkillFixture WithValidSkill(string folderName, string body = "") =>
		WithSkill(
			folderName,
			$"""
			---
			name: {folderName}
			description: Does a specific thing. Use when a task needs that specific thing done.
			---

			# {folderName}

			{body}
			"""
		);

	/// <summary>Writes a bundled file inside a skill folder.</summary>
	/// <param name="folderName">The owning skill folder.</param>
	/// <param name="relativePath">The path within the skill, using forward slashes.</param>
	/// <param name="content">The file's content.</param>
	/// <returns>This fixture, for chaining.</returns>
	public SkillFixture WithBundledFile(string folderName, string relativePath, string content)
	{
		var path = Path.Combine(SkillsFolder, folderName, relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);

		return this;
	}

	/// <summary>Creates an empty skills folder, which is different from having none.</summary>
	/// <returns>This fixture, for chaining.</returns>
	public SkillFixture WithEmptySkillsFolder()
	{
		Directory.CreateDirectory(SkillsFolder);
		return this;
	}

	/// <summary>Builds an evaluator over the fixture's skills folder.</summary>
	/// <returns>The evaluator.</returns>
	public SkillPolicyEvaluator Evaluator() => new(SkillCatalogue.ForFolder(Root, SkillsFolder));

	/// <summary>Deletes the fixture.</summary>
	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Root))
				Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
			// A fixture left behind in the temp folder is not worth failing a test over.
		}
	}
}
