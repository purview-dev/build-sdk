using System.Text.Json;
using System.Text.RegularExpressions;

namespace Purview.BuildSdk;

/// <summary>
/// Tests this repository's own agent skills, rather than a fixture in a throwaway harness.
///
/// This repository both authors the skills and ships the SDK that installs them, so it is the first
/// consumer of its own installation mode: the authored copies live at <c>skills/&lt;name&gt;/</c> and
/// are installed into the agent directories by the skills CLI, with <c>skills-lock.json</c>
/// committed as the record of that. Nothing in the build keeps those two in step - a skill can be
/// added, renamed or deleted without anyone re-running the install - so these tests are what keeps
/// the conversion honest.
///
/// Every assertion here reads files only. There is no CLI invocation, no network and no Node, so
/// this runs identically on a developer machine and on a CI runner that has none of them.
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
		using var document = JsonDocument.Parse(File.ReadAllText(LockFilePath));
		return
		[
			.. document
				.RootElement.GetProperty("skills")
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
	public async Task EverySkillIsInstalled()
	{
		var authored = AuthoredSkillNames();
		var locked = ReadLockFile().Select(entry => entry.Name).Order(StringComparer.Ordinal).ToArray();

		await Assert.That(authored).IsNotEmpty();
		// A skill added to skills/ but never installed is invisible to every agent, and a lockfile
		// entry with no authored folder behind it is a skill that was deleted or renamed without the
		// install being re-run. Both are fixed the same way: `just skills-install`.
		await Assert
			.That(locked)
			.IsEquivalentTo(authored)
			.Because(
				"skills-lock.json must list exactly the skills authored under skills/. Run 'just skills-install' to bring them back into step."
			);
	}

	[Test]
	public async Task SkillsAreInstalledFromTheWorkingTree()
	{
		var entries = ReadLockFile();

		await Assert.That(entries).IsNotEmpty();
		foreach (var entry in entries)
		{
			// This repository authors these skills, so it installs them from '.', not from a pinned
			// git ref. That is what makes an edit to a SKILL.md visible to an agent without a
			// push-and-reinstall round trip, and it keeps the committed lockfile portable - an
			// absolute path or a machine-specific ref would not survive a clone.
			await Assert
				.That(entry.Source)
				.IsEqualTo(".")
				.Because($"'{entry.Name}' must be installed from the working tree");
			await Assert.That(entry.SourceType).IsEqualTo("local");
		}
	}

	[Test]
	public async Task EverySkillIsWellFormed()
	{
		// The same shape ValidatePurviewSkills enforces at pack time, asserted here against the real
		// authored skills: the pack-time check only covers PurviewSkillsToPack, which is a subset.
		foreach (var name in AuthoredSkillNames())
		{
			var skillFile = Path.Combine(SkillsFolder, name, "SKILL.md");
			await Assert.That(File.Exists(skillFile)).IsTrue().Because($"'{name}' must have a SKILL.md");

			var text = await File.ReadAllTextAsync(skillFile);
			var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

			await Assert.That(lines[0]).IsEqualTo("---").Because($"'{name}' must open with YAML frontmatter");

			var closing = Array.IndexOf(lines, "---", 1);
			await Assert.That(closing).IsGreaterThan(0).Because($"'{name}' frontmatter must be terminated");

			var frontmatter = lines[1..closing];
			var declaredName = frontmatter
				.FirstOrDefault(line => line.StartsWith("name:", StringComparison.Ordinal))
				?[5..].Trim();

			// The CLI installs by frontmatter name while a human reads the folder name; when they
			// disagree, the skill installs under a name nobody expects.
			await Assert
				.That(declaredName)
				.IsEqualTo(name)
				.Because($"'{name}' declares a frontmatter name that does not match its folder");

			var hasDescription = frontmatter.Any(line =>
				line.StartsWith("description:", StringComparison.Ordinal) && line[12..].Trim().Length > 0
			);
			// The description is the activation trigger: without one the skill never fires.
			await Assert.That(hasDescription).IsTrue().Because($"'{name}' must declare a non-empty description");
		}
	}

	[Test]
	public async Task SkillLinksInAgentsMarkdownResolve()
	{
		var agentsMarkdown = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "AGENTS.md"));

		// AGENTS.md pointed at ./.agents/skills/<name>/SKILL.md before this repository converted to
		// the installed-skills mode. Those paths are produced by the CLI and gitignored, so every one
		// of those links was broken in a fresh clone. They must point at the authored copies.
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

	[Test]
	public async Task InstalledSkillsAreNotTracked()
	{
		// The authored copies are the tracked ones. The CLI's universal location happens to be
		// '.agents/skills/', which is also where this SDK's own package mirror writes, so it is the
		// one that most easily ends up committed by accident.
		var installRoots = (string[])[".agents", ".claude", "agent"];

		foreach (var root in installRoots)
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
