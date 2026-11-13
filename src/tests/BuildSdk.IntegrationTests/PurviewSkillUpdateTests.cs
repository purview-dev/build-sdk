using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>UpdatePurviewSkills</c>, <c>CheckPurviewSkills</c> and <c>FixPurviewSkills</c>. The
/// skills CLI is stood in for by <c>echo</c>, so the assertions are about the commands handed to it -
/// never letting a test reach <c>bun x</c> or <c>npx</c>, which would download from the network.
/// </summary>
sealed class PurviewSkillUpdateTests
{
	const string PurviewSource = "purview-dev/build-sdk";
	const string OtherSource = "vercel-labs/agent-skills";
	const string OldCommit = "aaaaaaaabbbbbbbbccccccccddddddddeeeeeeee";

	static string LockFilePath(ProjectHarness harness) => Path.Combine(harness.SolutionDirectory, "skills-lock.json");

	/// <summary>One skill pinned at an old ref, plus two skills from a second repository.</summary>
	static void GivenInstalledSkills(ProjectHarness harness) =>
		SkillLockFileFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-buildsdk-testing", PurviewSource, OldCommit),
			("web-design", OtherSource, null),
			("frontend-design", OtherSource, null)
		);

	static async Task<(int Code, string Output)> RunAsync(
		ProjectHarness harness,
		string target,
		string extraArguments = "",
		CancellationToken cancellationToken = default
	)
	{
		var (code, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-v:n \"-t:{target}\" -p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" {extraArguments}",
			cancellationToken
		);

		return (code, stdOut + stdErr);
	}

	[Test]
	public async Task Update_ReAddsEachSource_AtItsLatest_WithEverySkill(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"-p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		// Each unique source once, unpinned, with every skill it has - so a skill the repository has
		// gained since the last install comes in too, and the old ref is not kept.
		await Assert.That(output).Contains($"add \"{PurviewSource}\" --skill \"*\"");
		await Assert.That(output).Contains($"add \"{OtherSource}\" --skill \"*\"");
		await Assert.That(output).DoesNotContain("--skill \"web-design\"");
		await Assert.That(output).DoesNotContain(OldCommit);
		// No agents named: the CLI auto-detects, so no agent flag is passed.
		await Assert.That(output).DoesNotContain("--agent");
		// The re-adds are the whole update: 'skills add' refreshes installed skills and adds new ones,
		// so 'skills update' is never run.
		await Assert.That(output).Contains("2 source(s) refreshed.");
		await Assert.That(output).DoesNotContain("echo update");
	}

	[Test]
	public async Task Update_NamedAgents_BecomeOneFlagEach(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"-p:PurviewSkillsCommand=echo -p:PurviewSkillsAgents=claude-code%2Ccodex",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		// 'dotnet msbuild -p:' splits on a bare comma, so the separator is escaped; the CLI does not
		// split a comma-separated agent list either, so each agent gets its own flag.
		await Assert.That(output).Contains("--agent \"claude-code\" --agent \"codex\" -y");
		await Assert.That(output).DoesNotContain("claude-code,codex\"");
	}

	[Test]
	public async Task Update_DryRun_RunsNothing(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		// A command that cannot run proves nothing was executed.
		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"-p:PurviewSkillsCommand=purview-no-such-command -p:PurviewSkillsDryRun=true",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains($"purview-no-such-command add \"{OtherSource}\"");
		await Assert.That(output).Contains("dry run, 2 source(s), nothing changed");
	}

	[Test]
	public async Task Update_EveryFailedSource_IsNamed(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		// 'dotnet <unknown>' exits non-zero on every platform without touching the network.
		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"\"-p:PurviewSkillsCommand=dotnet purview-no-such-command\"",
			cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0008");
		await Assert.That(output).Contains($"re-adding '{OtherSource}' failed");
		await Assert.That(output).Contains($"re-adding '{PurviewSource}' failed");
		await Assert.That(output).DoesNotContain("source(s) refreshed.");
	}

	[Test]
	public async Task Update_WithoutALockFile_ReportsHowToInstall(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);

		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"-p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0007");
		await Assert.That(output).Contains("InstallPurviewSkills");
	}

	[Test]
	public async Task Check_WithoutAgents_EnforcesNothing(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillLayout", cancellationToken: cancellationToken);
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".claude", "skills", "purview-alpha"));

		var (code, output) = await RunAsync(harness, "CheckPurviewSkills", cancellationToken: cancellationToken);

		// The CLI auto-detects agents, so an agent directory is expected, not a violation.
		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PurviewSkillsAgents is not set");
		await Assert.That(output).DoesNotContain("PRSKL0015");
	}

	[Test]
	public async Task Check_WithAgents_ReportsOnlyUnselectedAgentDirectories(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillLayout", cancellationToken: cancellationToken);
		var root = harness.SolutionDirectory;
		Directory.CreateDirectory(Path.Combine(root, ".agents", "skills", "purview-alpha"));
		Directory.CreateDirectory(Path.Combine(root, ".claude", "skills", "purview-alpha"));
		Directory.CreateDirectory(Path.Combine(root, ".windsurf", "skills", "purview-alpha"));
		// The authored folder, which openclaw would also install into, is never a violation.
		Directory.CreateDirectory(Path.Combine(root, "skills", "my-skill"));

		var (code, output) = await RunAsync(
			harness,
			"CheckPurviewSkills",
			"-p:PurviewSkillsAgents=claude-code",
			cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0015");
		await Assert.That(output).Contains("'.windsurf/skills'");
		// '.agents/skills' is the canonical store, and claude-code was selected.
		await Assert.That(output).DoesNotContain("'.agents/skills'");
		await Assert.That(output).DoesNotContain("'.claude/skills'");
		await Assert.That(output).DoesNotContain("'skills'");
	}

	[Test]
	public async Task Check_UnknownAgent_IsAWarning(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillLayout", cancellationToken: cancellationToken);

		var (code, output) = await RunAsync(
			harness,
			"CheckPurviewSkills",
			"-p:PurviewSkillsAgents=universal%2Cpurview-imaginary",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0016");
		await Assert.That(output).Contains("purview-imaginary");
	}

	[Test]
	public async Task Fix_RemovesEmptyAgentDirectories_AndKeepsRealContent(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillLayout", cancellationToken: cancellationToken);
		var root = harness.SolutionDirectory;
		// Emptied of links already: nothing real left, so the folder - and its now-empty parent - go.
		Directory.CreateDirectory(Path.Combine(root, ".windsurf", "skills", "purview-alpha"));
		// Real content beside the skills folder is not the CLI's, and neither is a real file inside it.
		Directory.CreateDirectory(Path.Combine(root, ".claude", "skills", "hand-written"));
		await File.WriteAllTextAsync(Path.Combine(root, ".claude", "settings.json"), "{}", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(root, ".claude", "skills", "hand-written", "SKILL.md"),
			"---\nname: hand-written\n---\n",
			cancellationToken
		);

		var (code, output) = await RunAsync(
			harness,
			"FixPurviewSkills",
			"-p:PurviewSkillsAgents=universal",
			cancellationToken
		);

		await Assert.That(Directory.Exists(Path.Combine(root, ".windsurf"))).IsFalse().Because(output);
		await Assert.That(File.Exists(Path.Combine(root, ".claude", "settings.json"))).IsTrue();
		await Assert.That(File.Exists(Path.Combine(root, ".claude", "skills", "hand-written", "SKILL.md"))).IsTrue();
		// What could not be removed fails the target and names what was kept.
		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0015");
		await Assert.That(output).Contains(".claude/skills/hand-written/SKILL.md");
	}

	[Test]
	public async Task Fix_RemovesLinks_NeverTheirTargets(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillLayout", cancellationToken: cancellationToken);
		var root = harness.SolutionDirectory;
		var canonical = Path.Combine(root, ".agents", "skills", "purview-alpha");
		Directory.CreateDirectory(canonical);
		await File.WriteAllTextAsync(
			Path.Combine(canonical, "SKILL.md"),
			"---\nname: purview-alpha\n---\n",
			cancellationToken
		);
		Directory.CreateDirectory(Path.Combine(root, ".claude", "skills"));

		var link = Path.Combine(root, ".claude", "skills", "purview-alpha");
		try
		{
			Directory.CreateSymbolicLink(link, canonical);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// Windows only allows symlinks with Developer Mode or elevation; CI on Linux always runs this.
			Skip.Test($"Symbolic links cannot be created here: {exception.Message}");
		}

		var (code, output) = await RunAsync(
			harness,
			"FixPurviewSkills",
			"-p:PurviewSkillsAgents=universal",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(Directory.Exists(Path.Combine(root, ".claude", "skills"))).IsFalse();
		// The canonical copy the link pointed at is untouched.
		await Assert.That(File.Exists(Path.Combine(canonical, "SKILL.md"))).IsTrue();
		await Assert.That(output).Contains("removed .claude/skills/purview-alpha");
	}
}
