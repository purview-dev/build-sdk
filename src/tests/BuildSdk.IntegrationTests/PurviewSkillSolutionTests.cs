using System.Text.RegularExpressions;
using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Skills belong to the repository, so a skill target pointed at a solution must do its work once
/// for the whole solution rather than once per project. MSBuild runs a custom target in every project
/// of a solution, so each project hands the work to one elected leader, and MSBuild's result cache runs
/// the leader's target once however many projects asked.
/// </summary>
sealed partial class PurviewSkillSolutionTests
{
	const string SolutionFileName = "Skills.slnx";

	// The dry run prints the command it would have run, once per run of the install target.
	[GeneratedRegex("Agent skills \\(dry run\\):")]
	private static partial Regex DryRunInstallLine();

	// MSBuild logs this line when a target actually runs, not when a cached result is handed back.
	[GeneratedRegex("is building \"[^\"]+\" \\([^)]+\\) on node \\d+ \\(_ListPurviewSkillsCore")]
	private static partial Regex ListCoreRun();

	[GeneratedRegex("No restored package ships agent skills\\.")]
	private static partial Regex NoSkillsLine();

	[GeneratedRegex("Purview\\.Example 1\\.2\\.3 -> purview-dev/purview-example#")]
	private static partial Regex ExampleListedLine();

	/// <summary>
	/// Three SDK projects in one solution, each with its own restore graph. The harness's own solution
	/// lists projects as files, so this writes one that lists them as projects.
	/// </summary>
	static async Task<ProjectHarness[]> CreateSolutionAsync(CancellationToken cancellationToken)
	{
		var first = await ProjectHarness.CreateAsync(
			"Alpha",
			extraProps: "<ProjectAssetsFile>$(MSBuildProjectDirectory)/fake.assets.json</ProjectAssetsFile>",
			cancellationToken: cancellationToken
		);

		var projects = new List<ProjectHarness> { first };
		foreach (var name in new[] { "Bravo", "Charlie" })
		{
			projects.Add(
				await ProjectHarness
					.For(name)
					.WithNamespacePrefix("Test")
					.AddPropertyRaw(
						"<ProjectAssetsFile>$(MSBuildProjectDirectory)/fake.assets.json</ProjectAssetsFile>"
					)
					.WithSolutionDirectory(first.SolutionDirectory)
					.BuildAsync(cancellationToken)
			);
		}

		await File.WriteAllTextAsync(
			Path.Combine(first.SolutionDirectory, SolutionFileName),
			$"""
			<Solution>
			{string.Concat(
				projects.Select(project =>
					$"\t<Project Path=\"{project.ProjectName}/{project.ProjectName}.csproj\" />\n"
				)
			)}</Solution>
			""",
			cancellationToken
		);

		// The repository root resolves from a marker, as it would in a real repository.
		await File.WriteAllTextAsync(Path.Combine(first.SolutionDirectory, "AGENTS.md"), "# Test", cancellationToken);

		foreach (var project in projects)
			SkillPackageFixtures.WriteAssetsFile(Path.Combine(project.ProjectDirectory, "fake.assets.json"));

		return [.. projects];
	}

	static async Task<(int Code, string Output)> RunOnSolutionAsync(
		ProjectHarness harness,
		string arguments,
		CancellationToken cancellationToken
	)
	{
		var (code, stdOut, stdErr) = await harness.RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine(harness.SolutionDirectory, SolutionFileName)}\" -nologo -v:n {arguments}",
			harness.SolutionDirectory,
			cancellationToken
		);

		return (code, stdOut + stdErr);
	}

	[Test]
	[Arguments("-m:1")]
	[Arguments("-m")]
	public async Task ASkillTargetOnASolution_RunsOnce(string parallelism, CancellationToken cancellationToken)
	{
		var projects = await CreateSolutionAsync(cancellationToken);
		using var owner = projects[0];

		var (code, output) = await RunOnSolutionAsync(
			owner,
			$"{parallelism} -t:InstallPurviewSkills -p:PurviewSkillsDryRun=true "
				+ "-p:PurviewSkillsSource=purview-dev/engineering-playbook -p:PurviewSkillsRunner=npx",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(DryRunInstallLine().Count(output)).IsEqualTo(1).Because(output);
	}

	[Test]
	public async Task ASolution_ElectsTheSameLeaderFromEveryProject(CancellationToken cancellationToken)
	{
		var projects = await CreateSolutionAsync(cancellationToken);
		using var owner = projects[0];

		var (code, output) = await RunOnSolutionAsync(owner, "-t:ListPurviewSkills", cancellationToken);

		await Assert.That(code).IsZero().Because(output);
		// Each project runs the public target, but only the leader runs the work behind it.
		await Assert.That(ListCoreRun().Count(output)).IsEqualTo(1).Because(output);
		await Assert.That(NoSkillsLine().Count(output)).IsEqualTo(1);
	}

	[Test]
	public async Task APackageOnlyOneProjectReferences_IsFoundFromTheSolution(CancellationToken cancellationToken)
	{
		var projects = await CreateSolutionAsync(cancellationToken);
		using var owner = projects[0];

		var packagesRoot = Path.Combine(owner.SolutionDirectory, "packages");
		SkillPackageFixtures.WritePackage(packagesRoot, "Purview.Example", "1.2.3", ["purview-alpha"]);
		// Only the last project - never the leader - references the package, the way a test project
		// is often the only one to reference a testing library.
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(projects[2].ProjectDirectory, "fake.assets.json"),
			("Purview.Example", "1.2.3")
		);

		var (code, output) = await RunOnSolutionAsync(
			owner,
			$"-t:ListPurviewSkills -p:NuGetPackageFolders=\"{packagesRoot}\"",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(ExampleListedLine().Count(output)).IsEqualTo(1).Because(output);
	}

	[Test]
	public async Task TwoVersionsOfOnePackage_AreReportedAsAConflict(CancellationToken cancellationToken)
	{
		var projects = await CreateSolutionAsync(cancellationToken);
		using var owner = projects[0];

		var packagesRoot = Path.Combine(owner.SolutionDirectory, "packages");
		SkillPackageFixtures.WritePackage(packagesRoot, "Purview.Example", "1.0.0", ["purview-alpha"]);
		SkillPackageFixtures.WritePackage(packagesRoot, "Purview.Example", "2.0.0", ["purview-alpha"]);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(projects[1].ProjectDirectory, "fake.assets.json"),
			("Purview.Example", "1.0.0")
		);
		SkillPackageFixtures.WriteAssetsFile(
			Path.Combine(projects[2].ProjectDirectory, "fake.assets.json"),
			("Purview.Example", "2.0.0")
		);

		var (code, output) = await RunOnSolutionAsync(
			owner,
			$"-t:ListPurviewSkills -p:NuGetPackageFolders=\"{packagesRoot}\"",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(output);
		await Assert.That(output).Contains("PRSKL0017");
		await Assert.That(output).Contains("1.0.0, 2.0.0");
	}

	[Test]
	public async Task AnUnrestoredProject_IsNamedInPRSKL0001(CancellationToken cancellationToken)
	{
		var projects = await CreateSolutionAsync(cancellationToken);
		using var owner = projects[0];

		File.Delete(Path.Combine(projects[1].ProjectDirectory, "fake.assets.json"));

		var (code, output) = await RunOnSolutionAsync(owner, "-t:ListPurviewSkills", cancellationToken);

		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0001");
		await Assert.That(output).Contains("Bravo.csproj");
	}
}
