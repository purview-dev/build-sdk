using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>UpdatePurviewSkills</c>, <c>CheckPurviewSkills</c> and <c>FixPurviewSkills</c>. The
/// skills CLI is stood in for by <c>echo</c>, so the assertions are about the commands handed to it -
/// never letting a test reach <c>bun x</c> or <c>npx</c>, which would download from the network.
/// </summary>
sealed class PurviewSkillUpdateTests
{
	const string PackageId = "Purview.Example";
	const string PackageVersion = "1.2.3";
	const string PackageSource = "purview-dev/purview-example";
	const string OtherSource = "vercel-labs/agent-skills";
	const string OldCommit = "aaaaaaaabbbbbbbbccccccccddddddddeeeeeeee";

	static string LockFilePath(ProjectHarness harness) => Path.Combine(harness.SolutionDirectory, "skills-lock.json");

	/// <summary>
	/// One package shipping <c>purview-alpha</c>, installed at an old ref, plus two skills from a
	/// repository no package speaks for.
	/// </summary>
	static void GivenInstalledSkills(ProjectHarness harness)
	{
		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		SkillPackageFixtures.WritePackage(packagesRoot, PackageId, PackageVersion, ["purview-alpha"]);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json"),
			(PackageId, PackageVersion)
		);
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", PackageSource, OldCommit),
			("web-design", OtherSource, null),
			("frontend-design", OtherSource, null)
		);
	}

	static async Task<(int Code, string Output)> RunAsync(
		ProjectHarness harness,
		string target,
		string extraArguments = "",
		CancellationToken cancellationToken = default
	)
	{
		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");

		var (code, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-v:n \"-t:{target}\" "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\" "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" {extraArguments}",
			cancellationToken
		);

		return (code, stdOut + stdErr);
	}

	[Test]
	public async Task Update_ReAddsEachSource_PinningThePackagesOwnCommit(CancellationToken cancellationToken)
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
		// The package's source moves to the commit the referenced version was built from, not to the
		// newest skill and not left at the old ref.
		await Assert
			.That(output)
			.Contains($"add \"{PackageSource}#{SkillPackageFixtures.DefaultCommit}\" --skill \"purview-alpha\"");
		// Every other source is re-added once, naming only the skills the lockfile records for it.
		await Assert.That(output).Contains($"add \"{OtherSource}\" --skill \"frontend-design\" --skill \"web-design\"");
		await Assert.That(output).DoesNotContain(OldCommit + "\" --skill");
		// No agents named: the CLI auto-detects, so no agent flag is passed.
		await Assert.That(output).DoesNotContain("--agent");
		// 'echo' leaves the lockfile untouched, so Auto skips 'skills update'.
		await Assert.That(output).Contains("skipped 'echo update -y -p'");
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
	public async Task Update_ModeAlways_RunsSkillsUpdate(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"-p:PurviewSkillsCommand=echo -p:PurviewSkillsUpdateMode=Always",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("Agent skills: echo update -y -p");
		await Assert.That(output).Contains("refreshed and updated");
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
	public async Task Update_AFailedSource_IsNamed_AndSkillsUpdateIsSkipped(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);

		// 'dotnet <unknown>' exits non-zero on every platform without touching the network.
		var (code, output) = await RunAsync(
			harness,
			"UpdatePurviewSkills",
			"\"-p:PurviewSkillsCommand=dotnet purview-no-such-command\" -p:PurviewSkillsUpdateMode=Always",
			cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0008");
		await Assert.That(output).Contains($"re-adding '{OtherSource}' failed");
		await Assert.That(output).Contains($"re-adding '{PackageSource}#{SkillPackageFixtures.DefaultCommit}' failed");
		await Assert.That(output).DoesNotContain("Agent skills: dotnet purview-no-such-command update");
	}

	[Test]
	public async Task Update_WithoutALockFile_ReportsHowToInstall(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillUpdate", cancellationToken: cancellationToken);
		GivenInstalledSkills(harness);
		File.Delete(LockFilePath(harness));

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
