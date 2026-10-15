using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the recommendation half of the skills integration: what the SDK says about the skills a
/// referenced package ships versus the skills the repository has installed, and - just as
/// importantly - when it says nothing at all. Every scenario here is offline and spawns no process:
/// the inputs are the restore graph and the skills CLI's own <c>skills-lock.json</c>, both written
/// directly by the test.
/// </summary>
sealed class PurviewSkillRecommendationTests
{
	const string PackageId = "Purview.Example";
	const string PackageVersion = "1.2.3";
	const string Source = "purview-dev/purview-example";
	const string OtherCommit = "aaaaaaaabbbbbbbbccccccccddddddddeeeeeeee";

	/// <summary>
	/// Drives a recommendation target against a fabricated restore graph. The assets file and package
	/// folder are supplied explicitly so no real restore is needed, and the repository root is pinned
	/// to the harness solution so the lockfile and the deduplication stamp stay inside the throwaway
	/// tree.
	/// </summary>
	/// <remarks>
	/// <c>BuildingProject</c> is supplied because MSBuild only sets it when the <c>Build</c> target
	/// actually runs; invoking a target by name leaves it <c>false</c>. Passing the value a real
	/// build would have is what lets these scenarios exercise the rest of the build-hook gating in
	/// isolation. That the hook genuinely fires on an ordinary build is covered separately by
	/// <see cref="AnOrdinaryBuild_ReportsSkillState"/>.
	/// </remarks>
	static async Task<(int Code, string Output)> RecommendAsync(
		ProjectHarness harness,
		string target = "RecommendPurviewSkills",
		string extraArguments = "",
		CancellationToken cancellationToken = default
	)
	{
		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");

		var (code, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-v:m -t:{target} -p:BuildingProject=true "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\" "
				+ $"-p:PurviewSkillsRepoRoot=\"{harness.SolutionDirectory}\" {extraArguments}",
			cancellationToken
		);

		return (code, stdOut + stdErr);
	}

	/// <summary>Writes the default one-package restore graph used by most scenarios here.</summary>
	static void GivenPackageShipping(ProjectHarness harness, params string[] skillNames)
	{
		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		SkillPackageFixtures.WritePackage(packagesRoot, PackageId, PackageVersion, skillNames);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json"),
			(PackageId, PackageVersion)
		);
	}

	static string LockFilePath(ProjectHarness harness) => Path.Combine(harness.SolutionDirectory, "skills-lock.json");

	/// <summary>
	/// Stands in for a repository whose lockfile has actually been restored. The SDK looks for any
	/// dot-folder at the repository root holding a <c>skills</c> folder rather than hard-coding the
	/// agent directories, which belong to the CLI.
	/// </summary>
	static void GivenSkillsRestored(ProjectHarness harness) =>
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".claude", "skills"));

	[Test]
	public async Task MissingSkills_AreReportedWithAPinnedInstallCommand(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha", "purview-beta");

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		// A missing skill is advice, not a defect: it must never fail the build.
		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0010");
		await Assert.That(output).Contains("purview-alpha");
		await Assert.That(output).Contains("purview-beta");
		// The ref is the commit the restored package was built from, and each skill is named with its
		// own flag because the CLI matches nothing when given one comma-separated value.
		await Assert
			.That(output)
			.Contains(
				$"npx skills add \"{Source}#{SkillPackageFixtures.DefaultCommit}\" "
					+ "--skill \"purview-alpha\" --skill \"purview-beta\""
			);
	}

	[Test]
	public async Task SkillsInstalledAtTheReferencedCommit_ReportNothing(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		// A correctly installed set is the silent case: no diagnostic of any kind.
		await Assert.That(output).DoesNotContain("PRSKL");
	}

	[Test]
	public async Task DriftedSkills_RecommendAPinnedInstallRatherThanAnUpdate(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		// Installed from the right repository, but from some other commit: the installed skill may
		// describe a different version of the library than the one this project references.
		SkillPackageFixtures.WriteLockFile(LockFilePath(harness), ("purview-alpha", Source, OtherCommit));

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0011");
		await Assert.That(output).Contains(OtherCommit);
		// The recommendation is pinned to the referenced package's own commit. 'npx skills update'
		// installs the newest published skill regardless of package version, so it is named only to
		// explain why it is not the recommendation.
		await Assert.That(output).Contains($"npx skills add \"{Source}#{SkillPackageFixtures.DefaultCommit}\"");
		await Assert.That(output).Contains("Prefer this over 'npx skills update'");
		await Assert.That(output).DoesNotContain("PRSKL0010");
	}

	[Test]
	public async Task SkillsInstalledWithoutARef_AreReportedAsUnverifiable(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		// An unpinned install records no ref, so there is nothing to compare the package commit to.
		SkillPackageFixtures.WriteLockFile(LockFilePath(harness), ("purview-alpha", Source, null));

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0012");
		await Assert.That(output).Contains("npx skills list");
		// Unverifiable is not the same as wrong, so it is never reported as drift or as missing.
		await Assert.That(output).DoesNotContain("PRSKL0011");
		await Assert.That(output).DoesNotContain("PRSKL0010");
	}

	[Test]
	public async Task PackagesThatCannotBePinned_AreReportedAsUnverifiable(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		// No repository metadata at all, so no source and no commit can be derived.
		SkillPackageFixtures.WritePackage(packagesRoot, PackageId, PackageVersion, ["purview-alpha"], null, null);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json"),
			(PackageId, PackageVersion)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0012");
		// Never recommended from a guessed default branch.
		await Assert.That(output).DoesNotContain("PRSKL0010");
	}

	[Test]
	public async Task SkillsFromAnUnreferencedRepositoryOfTheSameOwner_AreReportedAsOrphaned(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit),
			("purview-legacy", "purview-dev/retired-library", SkillPackageFixtures.DefaultCommit)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0013");
		await Assert.That(output).Contains("purview-legacy");
		await Assert.That(output).Contains("npx skills remove \"purview-legacy\"");
		// Removal is the developer's, so it is only ever suggested.
		await Assert
			.That(await File.ReadAllTextAsync(LockFilePath(harness), cancellationToken))
			.Contains("purview-legacy");
	}

	[Test]
	public async Task SkillsOwnedByTheDeveloper_AreNeverReported(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit),
			// A skill from an owner this project has no relationship with: the developer installed it,
			// and orphan detection is owner-scoped precisely so it is left alone.
			("web-design", "vercel-labs/agent-skills", "1111111111111111111111111111111111111111")
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).DoesNotContain("PRSKL");
		await Assert.That(output).DoesNotContain("web-design");
	}

	[Test]
	public async Task SkillsInstalledUnderTheSameNameFromElsewhere_AreReportedAsUnverifiable(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		GivenSkillsRestored(harness);
		// Same skill name, different repository. Overwriting somebody else's skill is not this SDK's
		// call, and calling it drift would be plainly wrong.
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", "someone-else/their-skills", SkillPackageFixtures.DefaultCommit)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0012");
		await Assert.That(output).Contains("someone-else/their-skills");
		await Assert.That(output).DoesNotContain("PRSKL0011");
	}

	[Test]
	public async Task ACommittedLockfileWithNothingRestored_RecommendsTheRestoreCommand(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		// Deliberately no agent skill directory: the shape of a fresh clone.
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0014");
		await Assert.That(output).Contains("npx skills experimental_install");
		// The command is experimental, so the fallback is named alongside it.
		await Assert.That(output).Contains("npx skills add");
	}

	[Test]
	public async Task AgentsSkillsFolder_CountsAsARestoredInstall(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");
		// '.agents/skills' is the skills CLI's universal install location. It used to be excluded
		// from this scan because the SDK's own mirror wrote there, so finding it proved nothing -
		// but the mirror is gone, and the folder now means exactly what it appears to mean.
		// Excluding it would report PRSKL0014 against a repository whose skills are installed.
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, ".agents", "skills"));
		SkillPackageFixtures.WriteLockFile(
			LockFilePath(harness),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit)
		);

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		await Assert
			.That(output)
			.DoesNotContain("PRSKL0014")
			.Because("the lockfile has been restored into the CLI's universal location");
	}

	[Test]
	public async Task TheBuildHook_ReportsEachDistinctStateOnce(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");

		var first = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			cancellationToken: cancellationToken
		);
		var second = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			cancellationToken: cancellationToken
		);

		await Assert.That(first.Output).Contains("PRSKL0010");
		// A solution has many projects sharing one repository, and a rebuild changes nothing: the
		// same advice must not be repeated until the state it describes actually changes.
		await Assert.That(second.Output).DoesNotContain("PRSKL0010");
		await Assert
			.That(File.Exists(Path.Combine(harness.SolutionDirectory, ".purview", "skill-recommendations.cache")))
			.IsTrue();

		// The explicit target answers the question whenever it is asked, stamp or not.
		var explicitRun = await RecommendAsync(harness, cancellationToken: cancellationToken);
		await Assert.That(explicitRun.Output).Contains("PRSKL0010");
	}

	[Test]
	public async Task TheBuildHook_ReportsAgainWhenTheStateChanges(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");

		await RecommendAsync(harness, "RecommendPurviewSkillsOnBuild", cancellationToken: cancellationToken);

		// A second package starts shipping skills: a different state, so a fresh report.
		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		SkillPackageFixtures.WritePackage(
			packagesRoot,
			"Purview.Second",
			"2.0.0",
			["purview-second"],
			"https://github.com/purview-dev/second",
			"2222222222222222222222222222222222222222"
		);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json"),
			(PackageId, PackageVersion),
			("Purview.Second", "2.0.0")
		);

		var (_, output) = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			cancellationToken: cancellationToken
		);

		await Assert.That(output).Contains("purview-second");
	}

	[Test]
	public async Task TheBuildHook_StaysSilentOnCI(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");

		// CI installs nothing and has nobody to act on the advice.
		var suppressed = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			"-p:ContinuousIntegrationBuild=true",
			cancellationToken
		);
		await Assert.That(suppressed.Output).DoesNotContain("PRSKL");

		// A pipeline that deliberately verifies skill state can opt back in.
		var opted = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			"-p:ContinuousIntegrationBuild=true -p:PurviewSkillRecommendationsInCI=true",
			cancellationToken
		);
		await Assert.That(opted.Output).Contains("PRSKL0010");
	}

	[Test]
	public async Task TheBuildHook_StaysSilentDuringADesignTimeBuild(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");

		// An IDE runs design-time builds constantly; none of them is a moment to advise anybody.
		var (code, output) = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			"-p:DesignTimeBuild=true",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).DoesNotContain("PRSKL");
	}

	[Test]
	public async Task Recommendations_CanBeTurnedOff(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha");

		var (code, output) = await RecommendAsync(
			harness,
			"RecommendPurviewSkillsOnBuild",
			"-p:EnablePurviewSkillRecommendations=false",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).DoesNotContain("PRSKL");
	}

	[Test]
	public async Task Recommendations_InstallNothingAndTouchNoLockfile(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);
		GivenPackageShipping(harness, "purview-alpha", "purview-beta");

		await RecommendAsync(harness, cancellationToken: cancellationToken);

		// Recommending is the whole job: the SDK names the command and the developer runs it.
		await Assert.That(File.Exists(LockFilePath(harness))).IsFalse();
		await Assert.That(Directory.Exists(Path.Combine(harness.SolutionDirectory, ".claude"))).IsFalse();
		await Assert.That(Directory.Exists(Path.Combine(harness.ProjectDirectory, ".claude"))).IsFalse();
	}

	[Test]
	public async Task Recommendations_WorkWithoutAnyNodeTooling(CancellationToken cancellationToken)
	{
		// An empty PATH is the cleanest way to stand in for a machine with no Node tooling, and it
		// does not depend on whether this machine happens to have npx installed. Recommending reads
		// the restore graph and a lockfile, both local, so it must work anyway - and it must still be
		// able to name the command the developer would need Node for.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillConsumer",
			extraEnv: new Dictionary<string, string> { ["PATH"] = string.Empty },
			cancellationToken: cancellationToken
		);
		GivenPackageShipping(harness, "purview-alpha");

		var (code, output) = await RecommendAsync(harness, cancellationToken: cancellationToken);

		// Unlike InstallPurviewSkills, a missing CLI is never an error here.
		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).DoesNotContain("PRSKL0004");
		await Assert.That(output).Contains("PRSKL0010");
		await Assert.That(output).Contains("npx skills add");
	}

	[Test]
	public async Task AnOrdinaryBuild_ReportsSkillState(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync(
			"SkillConsumer",
			extraProps: """
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			""",
			cancellationToken: cancellationToken
		);

		// An AGENTS.md beside the solution is what makes the throwaway tree look like a repository:
		// the hook resolves its root by the same walk-up the rest of the SDK uses, and refuses to
		// fall back to the project folder.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Test repository\n",
			cancellationToken
		);

		// A committed lockfile with nothing restored beside it needs no skill-shipping package to
		// detect, which makes it the one state a genuine end-to-end build can be checked against.
		SkillPackageFixtures.WriteLockFile(
			Path.Combine(harness.SolutionDirectory, "skills-lock.json"),
			("purview-alpha", Source, SkillPackageFixtures.DefaultCommit)
		);

		// Verbose, because a recommendation is a high-importance message and the harness builds quiet
		// by default - at quiet verbosity an assertion about messages would pass for the wrong reason.
		var (success, output, errors) = await harness.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken: cancellationToken
		);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("PRSKL0014");
	}

	[Test]
	public async Task OrdinaryBuild_ReportsNothingWhenNoPackageShipsSkills(CancellationToken cancellationToken)
	{
		// The telemetry packages the SDK adds by default are not part of this scenario and would
		// need Central Package Management versions the throwaway solution does not declare.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillConsumer",
			extraProps: """
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			""",
			cancellationToken: cancellationToken
		);

		// Verbose, because a recommendation is a high-importance message and the harness builds quiet
		// by default - at quiet verbosity an assertion about messages would pass for the wrong reason.
		var (success, output, errors) = await harness.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken: cancellationToken
		);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		// A real, complete restore-and-build cycle in a repository with no skill-shipping packages is
		// silent, and still writes neither the CLI's lockfile nor an agent directory.
		await Assert.That(output + errors).DoesNotContain("PRSKL");
		await Assert.That(File.Exists(Path.Combine(harness.SolutionDirectory, "skills-lock.json"))).IsFalse();
	}
}
