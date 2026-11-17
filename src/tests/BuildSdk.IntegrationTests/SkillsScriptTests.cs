using System.ComponentModel;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>skills.mts</c> and its generated <c>skills.mjs</c>, the script the
/// <c>purview-buildsdk-justfile</c> skill's <c>skills-*</c> recipes run to install, update, check and
/// fix agent skills without MSBuild.
/// </summary>
/// <remarks>
/// <para>
/// Every behaviour test runs once per runtime the recipes can choose: Node with the generated
/// <c>skills.mjs</c>, and Bun and Deno with the <c>skills.mts</c> source. A runtime that is not on
/// <c>PATH</c> is skipped, not failed; CI always has Node.
/// </para>
/// <para>
/// <c>echo</c> stands in for the skills CLI, and the script prints every command before it runs it, so a
/// test asserts on the command without a network, a download or a real install. Never let a test reach
/// <c>bun x</c> or <c>npx</c>.
/// </para>
/// </remarks>
sealed class SkillsScriptTests
{
	const string PurviewSource = "purview-dev/build-sdk";
	const string OtherSource = "vercel-labs/agent-skills";
	const string OldCommit = "aaaaaaaabbbbbbbbccccccccddddddddeeeeeeee";

	public static IEnumerable<string> Runtimes() => ["node", "bun", "deno"];

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

			throw new InvalidOperationException($"No AGENTS.md was found above '{AppContext.BaseDirectory}'.");
		}
	}

	static string ScriptFolder => Path.Combine(RepositoryRoot, "skills", "purview-buildsdk-justfile", "assets", "just");

	/// <summary>A throwaway repository root under the temporary folder, deleted afterwards.</summary>
	sealed class TempRoot : IDisposable
	{
		public string Path { get; } =
			System.IO.Path.Combine(System.IO.Path.GetTempPath(), "purview-skills-" + Guid.NewGuid().ToString("N"));

		public TempRoot() => Directory.CreateDirectory(Path);

		public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

		public void Dispose()
		{
			try
			{
				Directory.Delete(Path, recursive: true);
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}
	}

	static async Task<(int Code, string Output)> RunAsync(
		string runtime,
		TempRoot root,
		string arguments,
		IReadOnlyDictionary<string, string>? environment = null,
		CancellationToken cancellationToken = default
	)
	{
		var (fileName, script) = runtime switch
		{
			"node" => ("node", $"\"{Path.Combine(ScriptFolder, "skills.mjs")}\""),
			"bun" => ("bun", $"\"{Path.Combine(ScriptFolder, "skills.mts")}\""),
			"deno" => ("deno", $"run -A \"{Path.Combine(ScriptFolder, "skills.mts")}\""),
			_ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, null),
		};

		try
		{
			var (code, stdOut, stdErr) = await ProcessRunner.RunWithEnvironmentAsync(
				fileName,
				$"{script} {arguments} --root \"{root.Path}\"",
				root.Path,
				environment,
				cancellationToken
			);

			return (code, stdOut + stdErr);
		}
		catch (Win32Exception exception)
		{
			Skip.Test($"{fileName} is not on PATH: {exception.Message}");
			return default;
		}
	}

	/// <summary>One skill pinned at an old ref, plus two skills from a second repository.</summary>
	static void GivenInstalledSkills(TempRoot root) =>
		SkillLockFileFixtures.WriteLockFile(
			root.Combine("skills-lock.json"),
			("purview-buildsdk-testing", PurviewSource, OldCommit),
			("web-design", OtherSource, null),
			("frontend-design", OtherSource, null)
		);

	static void GivenConfig(TempRoot root, string json)
	{
		Directory.CreateDirectory(root.Combine(".config"));
		File.WriteAllText(root.Combine(".config", "purview-build-sdk.json"), json);
	}

	// An empty PATH stands in for a machine with no package runner, whatever this one has installed.
	static readonly Dictionary<string, string> NoRunners = new() { ["PATH"] = string.Empty };

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Add_PassesTheSourceVerbatim_WithEverySkill_AndOneFlagPerAgent(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			"add purview-dev/engineering-playbook#abc123 --command echo --agents claude-code,codex",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		// The CLI does not split a comma-separated --agent, so each agent is its own flag.
		await Assert
			.That(output)
			.Contains(
				"Agent skills: echo add \"purview-dev/engineering-playbook#abc123\" --skill \"*\" --agent \"claude-code\" --agent \"codex\" -y"
			);
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Install_WithoutAgents_LetsTheCliAutoDetect(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			$"install {PurviewSource} --command echo",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("--skill \"*\" -y");
		await Assert.That(output).DoesNotContain("--agent");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Install_WithoutASource_RestoresTheLockFile(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenInstalledSkills(root);

		var (code, output) = await RunAsync(
			runtime,
			root,
			"install --command echo",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("Agent skills: echo experimental_install");
		await Assert.That(output).DoesNotContain(" add ");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Install_WithoutASourceOrALockFile_SaysWhatToDo(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			"install --command echo",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0007");
		await Assert.That(output).Contains("just skills-add");
	}

	/// <summary>
	/// Every package runner spells "run this npm package" its own way, so each must map to the launcher
	/// verified against the real tool. A wrong mapping is a command that does not run.
	/// </summary>
	[Test]
	[Arguments("npx", "npx -y skills@1")]
	[Arguments("bunx", "bunx skills@1")]
	[Arguments("bun", "bun x skills@1")]
	[Arguments("pnpm", "pnpm dlx skills@1")]
	[Arguments("yarn", "yarn dlx skills@1")]
	[Arguments("deno", "deno run -A npm:skills@1")]
	[Arguments("skills", "skills")]
	// A runner this script has never heard of still resolves, rather than being dropped.
	[Arguments("future-runner", "future-runner skills@1")]
	public async Task Runner_DrivesTheLauncher(
		string runner,
		string expectedLauncher,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			"node",
			root,
			$"add {PurviewSource} --dry-run --runner {runner}",
			NoRunners,
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains($"Agent skills (dry run): {expectedLauncher} add \"{PurviewSource}\"");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Runner_DefaultsToBun_AndThePackageToTheFirstMajor(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			$"add {PurviewSource} --dry-run",
			NoRunners,
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains($"Agent skills (dry run): bun x skills@1 add \"{PurviewSource}\"");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task WithoutARunnerOnPath_ReportsHowToInstallOne(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(runtime, root, $"add {PurviewSource}", NoRunners, cancellationToken);

		// It fails, but with a diagnostic that names the fix rather than a raw shell error.
		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0004");
		await Assert.That(output).Contains("npm install -g skills");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Update_ReAddsEachSource_AtItsLatest_WithEverySkill(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();
		GivenInstalledSkills(root);

		var (code, output) = await RunAsync(
			runtime,
			root,
			"update --command echo",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		// Each unique source once, unpinned, with every skill it has - so a skill the source has gained
		// comes in too, and the old ref is not kept.
		await Assert.That(output).Contains($"echo add \"{PurviewSource}\" --skill \"*\" -y");
		await Assert.That(output).Contains($"echo add \"{OtherSource}\" --skill \"*\" -y");
		await Assert.That(output).DoesNotContain("--skill \"web-design\"");
		await Assert.That(output).DoesNotContain(OldCommit);
		await Assert.That(output).Contains("2 source(s) refreshed.");
		// The re-adds are the whole update; 'skills update' is never run.
		await Assert.That(output).DoesNotContain("echo update");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Update_DryRun_RunsNothing(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenInstalledSkills(root);

		// A command that cannot run proves nothing was executed.
		var (code, output) = await RunAsync(
			runtime,
			root,
			"update --command purview-no-such-command --dry-run",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains($"purview-no-such-command add \"{OtherSource}\"");
		await Assert.That(output).Contains("dry run, 2 source(s), nothing changed");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Update_EveryFailedSource_IsNamed(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenInstalledSkills(root);

		// 'dotnet <unknown>' exits non-zero on every platform without touching the network.
		var (code, output) = await RunAsync(
			runtime,
			root,
			"update --command \"dotnet purview-no-such-command\"",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0008");
		await Assert.That(output).Contains($"re-adding '{OtherSource}' failed");
		await Assert.That(output).Contains($"re-adding '{PurviewSource}' failed");
		await Assert.That(output).DoesNotContain("source(s) refreshed.");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Update_WithoutALockFile_ReportsHowToInstall(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			"update --command echo",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0007");
		await Assert.That(output).Contains("just skills-add");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Check_WithoutAgents_EnforcesNothing(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		Directory.CreateDirectory(root.Combine(".claude", "skills", "purview-alpha"));

		var (code, output) = await RunAsync(runtime, root, "check", cancellationToken: cancellationToken);

		// The CLI auto-detects agents, so an agent directory is expected, not a violation.
		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("no agents are selected");
		await Assert.That(output).DoesNotContain("PRSKL0015");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Check_WithAgents_ReportsOnlyUnselectedAgentDirectories(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();
		Directory.CreateDirectory(root.Combine(".agents", "skills", "purview-alpha"));
		Directory.CreateDirectory(root.Combine(".claude", "skills", "purview-alpha"));
		Directory.CreateDirectory(root.Combine(".windsurf", "skills", "purview-alpha"));
		// The authored folder, which openclaw would also install into, is never a violation.
		Directory.CreateDirectory(root.Combine("skills", "my-skill"));

		var (code, output) = await RunAsync(
			runtime,
			root,
			"check --agents claude-code",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0015");
		await Assert.That(output).Contains("'.windsurf/skills'");
		// '.agents/skills' is the canonical store, and claude-code was selected.
		await Assert.That(output).DoesNotContain("'.agents/skills'");
		await Assert.That(output).DoesNotContain("'.claude/skills'");
		await Assert.That(output).DoesNotContain("'skills'");
		// Check is read-only.
		await Assert.That(Directory.Exists(root.Combine(".windsurf", "skills"))).IsTrue();
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Check_UnknownAgent_IsAWarning(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();

		var (code, output) = await RunAsync(
			runtime,
			root,
			"check --agents universal,purview-imaginary",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0016");
		await Assert.That(output).Contains("purview-imaginary");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Fix_RemovesEmptyAgentDirectories_AndKeepsRealContent(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();
		// Emptied of links already: nothing real left, so the folder - and its now-empty parent - go.
		Directory.CreateDirectory(root.Combine(".windsurf", "skills", "purview-alpha"));
		// Real content beside the skills folder is not the CLI's, and neither is a real file inside it.
		Directory.CreateDirectory(root.Combine(".claude", "skills", "hand-written"));
		await File.WriteAllTextAsync(root.Combine(".claude", "settings.json"), "{}", cancellationToken);
		await File.WriteAllTextAsync(
			root.Combine(".claude", "skills", "hand-written", "SKILL.md"),
			"---\nname: hand-written\n---\n",
			cancellationToken
		);

		var (code, output) = await RunAsync(
			runtime,
			root,
			"fix --agents universal",
			cancellationToken: cancellationToken
		);

		await Assert.That(Directory.Exists(root.Combine(".windsurf"))).IsFalse().Because(output);
		await Assert.That(File.Exists(root.Combine(".claude", "settings.json"))).IsTrue();
		await Assert.That(File.Exists(root.Combine(".claude", "skills", "hand-written", "SKILL.md"))).IsTrue();
		// What could not be removed fails the command and names what was kept.
		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0015");
		await Assert.That(output).Contains(".claude/skills/hand-written/SKILL.md");
	}

	/// <summary>
	/// <c>just skills-update --dry-run</c> hands <c>--dry-run</c> to fix as well, so a dry-run fix must
	/// only say what it would remove.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Fix_DryRun_RemovesNothing(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		Directory.CreateDirectory(root.Combine(".windsurf", "skills", "purview-alpha"));

		var (code, output) = await RunAsync(
			runtime,
			root,
			"fix --agents universal --dry-run",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(Directory.Exists(root.Combine(".windsurf", "skills", "purview-alpha"))).IsTrue();
		await Assert.That(output).Contains("would remove the skills CLI's links from '.windsurf/skills'");
		await Assert.That(output).Contains("dry run, nothing removed");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Fix_RemovesLinks_NeverTheirTargets(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		var canonical = root.Combine(".agents", "skills", "purview-alpha");
		Directory.CreateDirectory(canonical);
		await File.WriteAllTextAsync(
			Path.Combine(canonical, "SKILL.md"),
			"---\nname: purview-alpha\n---\n",
			cancellationToken
		);
		Directory.CreateDirectory(root.Combine(".claude", "skills"));

		try
		{
			Directory.CreateSymbolicLink(root.Combine(".claude", "skills", "purview-alpha"), canonical);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// Windows only allows symlinks with Developer Mode or elevation; CI on Linux always runs this.
			Skip.Test($"Symbolic links cannot be created here: {exception.Message}");
		}

		var (code, output) = await RunAsync(
			runtime,
			root,
			"fix --agents universal",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(Directory.Exists(root.Combine(".claude", "skills"))).IsFalse();
		// The canonical copy the link pointed at is untouched.
		await Assert.That(File.Exists(Path.Combine(canonical, "SKILL.md"))).IsTrue();
		await Assert.That(output).Contains("removed .claude/skills/purview-alpha");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Config_SuppliesTheSettings(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenConfig(
			root,
			/*lang=json,strict*/"""{ "Skills": { "Agents": ["claude-code", "codex"], "Command": "echo" } }"""
		);

		var (code, output) = await RunAsync(
			runtime,
			root,
			$"add {PurviewSource}",
			cancellationToken: cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert
			.That(output)
			.Contains($"echo add \"{PurviewSource}\" --skill \"*\" --agent \"claude-code\" --agent \"codex\" -y");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Options_OutrankTheEnvironment_WhichOutranksTheConfig(
		string runtime,
		CancellationToken cancellationToken
	)
	{
		using var root = new TempRoot();
		GivenConfig(
			root, /*lang=json,strict*/
			"""{ "Skills": { "Agents": "from-config", "Command": "echo" } }"""
		);
		var environment = new Dictionary<string, string> { ["PURVIEW_SKILLS_AGENTS"] = "from-environment" };

		var (_, fromEnvironment) = await RunAsync(
			runtime,
			root,
			$"add {PurviewSource}",
			environment,
			cancellationToken
		);
		var (_, fromOption) = await RunAsync(
			runtime,
			root,
			$"add {PurviewSource} --agents from-option",
			environment,
			cancellationToken
		);

		await Assert.That(fromEnvironment).Contains("--agent \"from-environment\"");
		await Assert.That(fromEnvironment).DoesNotContain("from-config");
		await Assert.That(fromOption).Contains("--agent \"from-option\"");
		await Assert.That(fromOption).DoesNotContain("from-environment\"");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Config_AgentDirectories_ExtendTheKnownTable(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenConfig(
			root,
			/*lang=json,strict*/"""{ "Skills": { "Agents": "new-agent", "AgentDirectories": { ".new-agent/skills": ["new-agent"] } } }"""
		);
		Directory.CreateDirectory(root.Combine(".new-agent", "skills", "purview-alpha"));

		var (code, output) = await RunAsync(runtime, root, "check", cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).DoesNotContain("PRSKL0016");
		await Assert.That(output).DoesNotContain("PRSKL0015");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Config_InvalidJson_IsAnError(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenConfig(root, "{ \"Skills\": ");

		var (code, output) = await RunAsync(runtime, root, "check", cancellationToken: cancellationToken);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0018");
	}

	[Test]
	[MethodDataSource(nameof(Runtimes))]
	public async Task Config_UnknownSetting_IsAWarning(string runtime, CancellationToken cancellationToken)
	{
		using var root = new TempRoot();
		GivenConfig(
			root, /*lang=json,strict*/
			"""{ "Skills": { "Agent": "claude-code" } }"""
		);

		var (code, output) = await RunAsync(runtime, root, "check", cancellationToken: cancellationToken);

		// A misspelt key would otherwise be silently ignored.
		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0019");
		await Assert.That(output).Contains("Skills.Agent");
	}

	/// <summary>
	/// skills.mjs is generated from skills.mts. A hand edit to either, or an edit to skills.mts that was
	/// not regenerated, would leave Node running different code from Bun and Deno.
	/// </summary>
	[Test]
	public async Task GeneratedJavaScript_MatchesTheTypeScriptSource(CancellationToken cancellationToken)
	{
		(int Code, string StdOut, string StdErr) result;
		try
		{
			result = await ProcessRunner.RunAsync(
				"node",
				$"\"{Path.Combine(RepositoryRoot, "scripts", "sync-skills-script.mjs")}\" --check",
				RepositoryRoot,
				cancellationToken
			);
		}
		catch (Win32Exception exception)
		{
			Skip.Test($"node is not on PATH: {exception.Message}");
			return;
		}

		// Exit 3: this Node has no type stripper (before 22.13), so it cannot tell.
		if (result.Code == 3)
			Skip.Test(result.StdErr.Trim());

		await Assert.That(result.Code).IsZero().Because(result.StdOut + result.StdErr);
	}
}
