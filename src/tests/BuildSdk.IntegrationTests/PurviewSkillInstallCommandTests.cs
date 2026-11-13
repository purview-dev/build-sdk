using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the command <c>InstallPurviewSkills</c> hands to the skills CLI, and the repository root it
/// runs from. The target is a thin wrapper: <c>skills add &lt;source&gt;</c> for an explicit
/// <c>PurviewSkillsSource</c>, or <c>skills experimental_install</c> to restore <c>skills-lock.json</c>.
/// </summary>
/// <remarks>
/// <c>echo</c> stands in for the CLI so a test asserts on the command without needing Node, a network,
/// or a real install. Exec runs through the platform shell, and echo is a builtin of both cmd and sh,
/// so this is identical on Windows, Linux and macOS. The throwaway solution is not a git repository and
/// has no <c>AGENTS.md</c>, so the repository root is named explicitly where a test needs one.
/// </remarks>
sealed class PurviewSkillInstallCommandTests
{
	[Test]
	public async Task InstallPurviewSkills_FromASource_AddsEverySkillItHas(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillInstaller", cancellationToken: cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" "
				+ "-p:PurviewSkillsSource=purview-dev/engineering-playbook#abc123 "
				+ "-p:PurviewSkillsCommand=echo -p:PurviewSkillsAgents=claude-code",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		// The caller's source and ref verbatim, every skill it has, then the agent selection.
		await Assert
			.That(stdOut)
			.Contains("add \"purview-dev/engineering-playbook#abc123\" --skill \"*\" --agent \"claude-code\" -y");
	}

	[Test]
	public async Task InstallPurviewSkills_WithoutAgents_LetsTheCliAutoDetect(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillInstaller", cancellationToken: cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" "
				+ "-p:PurviewSkillsSource=purview-dev/build-sdk -p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut).Contains("--skill \"*\" -y");
		await Assert.That(stdOut).DoesNotContain("--agent");
	}

	[Test]
	public async Task InstallPurviewSkills_WithoutASource_RestoresTheLockFile(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRestorer", cancellationToken: cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "skills-lock.json"),
			/*lang=json,strict*/"""{"version": 1, "skills": {}}""",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" -p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		// A fresh clone gets its skills back the standard way: the CLI restores what the lockfile records.
		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut).Contains("experimental_install");
		await Assert.That(stdOut).DoesNotContain(" add ");
	}

	[Test]
	public async Task InstallPurviewSkills_WithoutASourceOrALockFile_SaysWhatToDo(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillNothing", cancellationToken: cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" -p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		await Assert.That(exitCode).IsNotEqualTo(0);
		var output = stdOut + stdErr;
		await Assert.That(output).Contains("PRSKL0007");
		await Assert.That(output).Contains("PurviewSkillsSource");
	}

	/// <summary>
	/// The CLI is published to npm and every package runner spells "run this npm package" its own
	/// way, so the runner a repository standardises on has to map to the right launcher. A wrong
	/// mapping is not a formatting detail: it is a command that does not run.
	/// </summary>
	[Test]
	[Arguments("npx", "npx -y skills@1")]
	[Arguments("bunx", "bunx skills@1")]
	[Arguments("bun", "bun x skills@1")]
	[Arguments("pnpm", "pnpm dlx skills@1")]
	[Arguments("yarn", "yarn dlx skills@1")]
	[Arguments("deno", "deno run -A npm:skills@1")]
	[Arguments("skills", "skills")]
	// A runner this SDK has never heard of still resolves, rather than being silently dropped.
	[Arguments("future-runner", "future-runner skills@1")]
	public async Task SkillsRunner_DrivesTheExecutedCommand(
		string runner,
		string expectedLauncher,
		CancellationToken cancellationToken
	)
	{
		// An empty PATH means no runner is found, so the dry run falls back to the configured one.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillRunner",
			extraEnv: new Dictionary<string, string> { ["PATH"] = string.Empty },
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" "
				+ $"-p:PurviewSkillsSource=purview-dev/build-sdk -p:PurviewSkillsDryRun=true -p:PurviewSkillsRunner={runner}",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut).Contains($"Agent skills (dry run): {expectedLauncher} add \"purview-dev/build-sdk\"");
	}

	[Test]
	public async Task SkillsRunner_DefaultsToBun(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRunner", cancellationToken: cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-getProperty:PurviewSkillsRunner",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut.Trim()).IsEqualTo("bun");
	}

	/// <summary>
	/// The executed launcher pins the CLI to a major, so a breaking release cannot change what
	/// <c>InstallPurviewSkills</c> does. Each runner's exact invocation was verified against the real
	/// tool; getting one wrong produces a command that simply does not run, so they are asserted
	/// literally rather than derived.
	/// </summary>
	[Test]
	public async Task SkillsRunnerLaunchers_PinTheCliToAMajor(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRunner", cancellationToken: cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-getItem:PurviewSkillsRunnerLauncher",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));

		var jsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		await Assert.That(jsonStart).IsGreaterThanOrEqualTo(0);

		using var document = JsonDocument.Parse(stdOut[jsonStart..]);
		var launchers = document
			.RootElement.GetProperty("Items")
			.GetProperty("PurviewSkillsRunnerLauncher")
			.EnumerateArray()
			.ToDictionary(
				entry => entry.GetProperty("Identity").GetString()!,
				entry => entry.GetProperty("Exec").GetString()!
			);

		await Assert.That(launchers["npx"]).IsEqualTo("npx -y skills@1");
		await Assert.That(launchers["bunx"]).IsEqualTo("bunx skills@1");
		await Assert.That(launchers["bun"]).IsEqualTo("bun x skills@1");
		await Assert.That(launchers["pnpm"]).IsEqualTo("pnpm dlx skills@1");
		await Assert.That(launchers["yarn"]).IsEqualTo("yarn dlx skills@1");
		await Assert.That(launchers["deno"]).IsEqualTo("deno run -A npm:skills@1");
		// Already on PATH: used as-is, no download, works offline.
		await Assert.That(launchers["skills"]).IsEqualTo("skills");
	}

	[Test]
	public async Task InstallPurviewSkills_WithoutACliOnPath_ReportsHowToInstallOne(CancellationToken cancellationToken)
	{
		// An empty PATH is the cleanest way to stand in for a machine with no Node tooling, and it
		// does not depend on whether this machine happens to have npx installed.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillNoCli",
			extraEnv: new Dictionary<string, string> { ["PATH"] = string.Empty },
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" -p:PurviewSkillsSource=purview-dev/build-sdk",
			cancellationToken
		);

		// It fails, but with a diagnostic that names the fix rather than a raw shell error.
		await Assert.That(exitCode).IsNotEqualTo(0);
		var output = stdOut + stdErr;
		await Assert.That(output).Contains("PRSKL0004");
		await Assert.That(output).Contains("npm install -g skills");
	}

	/// <summary>
	/// Resolves the repository root every skill target runs from, by asking
	/// MSBuild for the property the shared resolution target computes.
	/// </summary>
	static async Task<string> ResolveRepositoryRootAsync(
		ProjectHarness harness,
		string extraArguments,
		CancellationToken cancellationToken
	)
	{
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRoot " + extraArguments,
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		return stdOut.Trim();
	}

	/// <summary>
	/// Every marker in the default list, on its own, identifies a repository root. Four of them are
	/// directories, which MSBuild's own <c>GetDirectoryNameOfFileAbove</c> cannot find at all - it
	/// matches files only - so each one is pinned here rather than assumed to work.
	/// </summary>
	[Test]
	[Arguments(".git", true)]
	[Arguments(".github", true)]
	[Arguments(".agents", true)]
	[Arguments(".claude", true)]
	[Arguments(".gitattributes", false)]
	[Arguments("LICENSE.md", false)]
	[Arguments("AGENTS.md", false)]
	[Arguments("CLAUDE.md", false)]
	[Arguments("package.json", false)]
	public async Task RepositoryRoot_IsFoundFromAnyDefaultMarker(
		string marker,
		bool isDirectory,
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		var markerPath = Path.Combine(harness.SolutionDirectory, marker);
		if (isDirectory)
			Directory.CreateDirectory(markerPath);
		else
			await File.WriteAllTextAsync(markerPath, "marker\n", cancellationToken);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert
			.That(Path.GetFullPath(resolved))
			.IsEqualTo(Path.GetFullPath(harness.SolutionDirectory))
			.Because($"'{marker}' should identify the repository root");
	}

	[Test]
	public async Task RepositoryRoot_MatchesAFileNamedDotGit(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.git' is a directory in a clone but a *file* in a worktree or a submodule, and both
		// identify a repository root. A directory marker therefore matches either.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, ".git"),
			"gitdir: ../../.git/worktrees/x\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRoot_LetsTheDefinitiveMarkerBeatANearerOne(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.git/' sits at the solution, a 'package.json' sits nearer in the project folder. '.git/'
		// is the repository root by definition, so a package inside a monorepo must not win.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".git"));
		await File.WriteAllTextAsync(
			Path.Combine(harness.ProjectDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"nested\" }\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
		await Assert.That(Path.GetFullPath(resolved)).IsNotEqualTo(Path.GetFullPath(harness.ProjectDirectory));
	}

	[Test]
	public async Task RepositoryRoot_PrefersANearerMarkerOverADistantNonDefinitiveOne(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// '.github/' outranks 'package.json' in the list, but only '.git/' is allowed to win from a
		// distance. Were that not so, a stray '.claude/' or '.github/' in a developer's home
		// directory would outrank the real repository root and scatter a lockfile outside it.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".github"));
		await File.WriteAllTextAsync(
			Path.Combine(harness.ProjectDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"nested\" }\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert
			.That(Path.GetFullPath(resolved))
			.IsEqualTo(Path.GetFullPath(harness.ProjectDirectory))
			.Because("the nearest marker wins once the definitive one is absent");
	}

	[Test]
	public async Task RepositoryRoot_BreaksTiesWithinADirectoryByListOrder(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// Both markers sit in the same directory, so the list order decides which one is reported.
		await File.WriteAllTextAsync(Path.Combine(harness.SolutionDirectory, "LICENSE.md"), "MIT\n", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "package.json"),
			/*lang=json,strict*/"{ \"name\": \"root\" }\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ResolvePurviewSkillsRepositoryRoot -getProperty:_PurviewSkillsRepositoryRootMarker",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut.Trim()).IsEqualTo("LICENSE.md");
	}

	[Test]
	public async Task RepositoryRoot_FallsThroughWhenTheLeadingMarkersAreAbsent(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// Nothing above '.claude/' exists, so the walk has to keep going down the list rather than
		// stopping at the first marker it cannot find.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".claude"));

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRootMarkers_CanBeReplaced(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "my-repo-marker"),
			"marker\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(
			harness,
			"-p:PurviewRepositoryRootMarkers=my-repo-marker",
			cancellationToken
		);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
	}

	[Test]
	public async Task RepositoryRoot_IsFoundFromAnAgentsMarkerAboveTheProject(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		// The marker sits beside the solution, a level above the project, which is exactly the shape
		// the walk-up exists for: skills belong to the repository, not to whichever project built.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		var resolved = await ResolveRepositoryRootAsync(harness, string.Empty, cancellationToken);

		await Assert.That(resolved).IsNotEmpty();
		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(harness.SolutionDirectory));
		// Never the project folder - that is the scattering this prevents.
		await Assert.That(Path.GetFullPath(resolved)).IsNotEqualTo(Path.GetFullPath(harness.ProjectDirectory));
	}

	[Test]
	public async Task RepositoryRoot_PrefersAnExplicitInstallRoot(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillRepoRoot", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		var elsewhere = Path.Combine(harness.SolutionDirectory, "elsewhere");
		Directory.CreateDirectory(elsewhere);

		var resolved = await ResolveRepositoryRootAsync(
			harness,
			$"-p:PurviewSkillsInstallRoot=\"{elsewhere}\"",
			cancellationToken
		);

		await Assert.That(Path.GetFullPath(resolved)).IsEqualTo(Path.GetFullPath(elsewhere));
	}

	[Test]
	public async Task InstallPurviewSkills_WithoutAResolvableRepositoryRoot_RefusesToGuess(
		CancellationToken cancellationToken
	)
	{
		// No marker anywhere between the project and the home directory, not a git repository, and no
		// CI workspace variables: nothing says where the repository is. The project folder is
		// deliberately not an acceptable answer, and neither is the home directory - a '.claude/' or
		// '.github/' commonly sits there, and resolving to it would install outside any repository.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillNoRoot",
			extraEnv: new Dictionary<string, string>
			{
				["GITHUB_WORKSPACE"] = string.Empty,
				["BUILD_SOURCESDIRECTORY"] = string.Empty,
				["CI_PROJECT_DIR"] = string.Empty,
			},
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n -p:PurviewSkillsSource=purview-dev/build-sdk -p:PurviewSkillsCommand=echo",
			cancellationToken
		);

		await Assert.That(exitCode).IsNotEqualTo(0);
		var output = stdOut + stdErr;
		await Assert.That(output).Contains("PRSKL0003");
		// The diagnostic names the way out rather than silently installing somewhere wrong.
		await Assert.That(output).Contains("PurviewSkillsInstallRoot");
	}
}
