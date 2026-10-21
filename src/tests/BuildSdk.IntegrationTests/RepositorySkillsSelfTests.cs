using System.Text.Json;
using System.Text.RegularExpressions;

namespace Purview.BuildSdk;

/// <summary>
/// Tests how this repository arranges its own skills, rather than a fixture in a throwaway harness.
///
/// Two folders, two purposes, and keeping them apart is the whole point of these tests:
///
/// <list type="bullet">
/// <item>
/// <c>skills/</c> holds the skills this repository <i>authors and publishes</i>. They describe
/// Purview.BuildSdk and its sibling packages, and they exist for other repositories to install with
/// <c>npx skills add purview-dev/build-sdk</c>. They are tracked, and they are packed.
/// </item>
/// <item>
/// <c>.agents/skills/</c> is the skills CLI's universal install location, holding skills installed
/// <i>from other sources</i>. It is gitignored, reproducible from <c>skills-lock.json</c>, and it is
/// not where this repository's own skills belong - installing them into it would make this
/// repository a consumer of advice it wrote for somebody else, and would put a second writer on
/// paths the CLI owns.
/// </item>
/// </list>
///
/// Content conformance - frontmatter, names, descriptions, bundled links - is checked by
/// <see cref="RepositorySkillTests"/>, through the shipped rule set. These tests cover only the
/// arrangement, which no package can check for us.
///
/// Every assertion here reads files or asks git what is tracked. There is no CLI invocation, no
/// network and no Node, so this runs identically on a developer machine and on a CI runner that has
/// none of them.
/// </summary>
sealed partial class RepositorySkillsSelfTests
{
	/// <summary>Matches a markdown link whose target is a <c>SKILL.md</c> under some skills folder.</summary>
	[GeneratedRegex(@"\]\(\.?/?(?<path>[^)\s]*skills/[^)\s]+\.md)\)")]
	private static partial Regex SkillLinkPattern();

	/// <summary>
	/// Walks up from the test assembly to the repository root, identified by the same
	/// <c>AGENTS.md</c> marker the SDK's own root resolution uses. Walking beats a relative path
	/// from the assembly because it does not break when the output folder depth changes.
	/// </summary>
	static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new InvalidOperationException(
				$"No AGENTS.md was found above '{AppContext.BaseDirectory}', so the repository root could not be resolved."
			);
		}
	}

	static string SkillsFolder => Path.Combine(RepositoryRoot, "skills");

	static string LockFilePath => Path.Combine(RepositoryRoot, "skills-lock.json");

	/// <summary>Every authored skill, by folder name, in a stable order.</summary>
	static string[] AuthoredSkillNames() =>
		[
			.. Directory
				.GetDirectories(SkillsFolder)
				.Select(Path.GetFileName)
				.Where(name => !string.IsNullOrEmpty(name))
				.Select(name => name!)
				.Order(StringComparer.Ordinal),
		];

	sealed record LockEntry(string Name, string Source, string SourceType);

	static LockEntry[] ReadLockFile()
	{
		if (!File.Exists(LockFilePath))
			return [];

		using var document = JsonDocument.Parse(File.ReadAllText(LockFilePath));
		if (!document.RootElement.TryGetProperty("skills", out var skills))
			return [];

		// The lock file is a dictionary of skill names to objects with "source" and "sourceType" properties.
		return
		[
			.. skills
				.EnumerateObject()
				.Select(entry => new LockEntry(
					entry.Name,
					entry.Value.TryGetProperty("source", out var source)
						? source.GetString() ?? string.Empty
						: string.Empty,
					entry.Value.TryGetProperty("sourceType", out var sourceType)
						? sourceType.GetString() ?? string.Empty
						: string.Empty
				))
				.OrderBy(entry => entry.Name, StringComparer.Ordinal),
		];
	}

	[Test]
	public async Task AuthoredSkillsAreNotInstalledIntoTheAgentDirectories()
	{
		// The authored copies are read from skills/ directly - that is the path AGENTS.md links to
		// and the path the CLI clones. Installing them into .agents/skills/ as well would put this
		// repository's own published advice into the folder reserved for skills from other sources,
		// and leave two copies that nothing keeps in step.
		var authored = AuthoredSkillNames();
		await Assert.That(authored).IsNotEmpty();

		var selfInstalled = ReadLockFile()
			.Select(entry => entry.Name)
			.Where(name => authored.Contains(name, StringComparer.Ordinal))
			.ToArray();

		await Assert
			.That(selfInstalled)
			.IsEmpty()
			.Because(
				"skills-lock.json records skills installed from other sources. This repository's own skills are "
					+ $"authored at skills/ and read from there: {string.Join(", ", selfInstalled)}"
			);

		foreach (var root in (string[])[".agents", ".claude", "agent"])
		{
			var installed = Path.Combine(RepositoryRoot, root, "skills");
			if (!Directory.Exists(installed))
				continue;

			var collisions = Directory
				.GetDirectories(installed)
				.Select(Path.GetFileName)
				.Where(name => name is not null && authored.Contains(name, StringComparer.Ordinal))
				.ToArray();

			await Assert
				.That(collisions)
				.IsEmpty()
				.Because($"'{root}/skills' holds skills from other sources, not this repository's own");
		}
	}

	[Test]
	public async Task InstalledSkillsAreNotTracked()
	{
		// Everything the CLI writes is reproducible from skills-lock.json, so none of it belongs in
		// the repository. '.agents/skills/' is the one that most easily ends up committed by
		// accident, because the rest of '.agents/' is authored content and stays tracked.
		foreach (var root in (string[])[".agents", ".claude", "agent"])
		{
			var skillsPath = Path.Combine(RepositoryRoot, root, "skills");
			if (!Directory.Exists(skillsPath))
				continue;

			var tracked = await TrackedFilesUnderAsync($"{root}/skills");
			await Assert
				.That(tracked)
				.IsEmpty()
				.Because($"'{root}/skills' is written by the skills CLI and must stay gitignored");
		}
	}

	[Test]
	public async Task SkillLinksInAgentsMarkdownResolve()
	{
		var agentsMarkdown = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "AGENTS.md"));

		// A link into '.agents/skills/' points at a path the CLI produces and gitignores, so it is
		// broken in a fresh clone. Links must point at the authored copies under skills/.
		var links = SkillLinkPattern()
			.Matches(agentsMarkdown)
			.Select(match => match.Groups["path"].Value)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		await Assert.That(links).IsNotEmpty().Because("AGENTS.md is expected to link to the authored skills");

		foreach (var link in links)
		{
			await Assert
				.That(link.Contains(".agents/skills/", StringComparison.Ordinal))
				.IsFalse()
				.Because($"'{link}' points at the CLI's gitignored install location rather than the authored skill");

			var resolved = Path.Combine(RepositoryRoot, link.Replace('/', Path.DirectorySeparatorChar));
			await Assert.That(File.Exists(resolved)).IsTrue().Because($"'{link}' does not resolve to a file");
		}
	}

	static async Task<string[]> TrackedFilesUnderAsync(string repositoryRelativePath)
	{
		using var process = System.Diagnostics.Process.Start(
			new System.Diagnostics.ProcessStartInfo("git", $"ls-files -- \"{repositoryRelativePath}\"")
			{
				WorkingDirectory = RepositoryRoot,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			}
		);

		if (process is null)
			return [];

		var output = await process.StandardOutput.ReadToEndAsync();
		await process.WaitForExitAsync();

		return
		[
			.. output
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.Where(line => line.Length > 0),
		];
	}
}
