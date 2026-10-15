using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

// CA1308: NuGet's global-packages layout is lowercase on disk and in the assets file's "path" value,
// so a fake restored package has to be written to the lowercase folder to be found at all. This is
// not normalisation for comparison.
#pragma warning disable CA1308

/// <summary>
/// Verifies the command <c>InstallPurviewSkills</c> hands to the skills CLI. The CLI does not split a
/// comma-separated <c>--skill</c> value - it matches nothing and reports "No matching skills found" -
/// so the shape of this command is a correctness concern, not a formatting one.
/// </summary>
sealed class PurviewSkillInstallCommandTests
{
	static void WriteFakePackage(string packagesRoot, string packageId, string version, string[] skillNames)
	{
		var packageRoot = Path.Combine(packagesRoot, packageId.ToLowerInvariant(), version);
		Directory.CreateDirectory(packageRoot);

		foreach (var skillName in skillNames)
		{
			var skillDirectory = Path.Combine(packageRoot, ".agents", "skills", skillName);
			Directory.CreateDirectory(skillDirectory);
			File.WriteAllText(
				Path.Combine(skillDirectory, "SKILL.md"),
				$"---\nname: {skillName}\ndescription: Use when exercising skill installation.\n---\n"
			);
		}

		File.WriteAllText(
			Path.Combine(packageRoot, $"{packageId.ToLowerInvariant()}.nuspec"),
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
				<metadata>
					<id>{packageId}</id>
					<version>{version}</version>
					<repository type="git" url="https://github.com/purview-dev/purview-example/" commit="0123456789abcdef0123456789abcdef01234567" />
				</metadata>
			</package>
			"""
		);
	}

	static void WriteAssetsFile(string assetsPath, string packageId, string version)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
		File.WriteAllText(
			assetsPath,
			$$"""
			{
				"version": 3,
				"libraries": {
					"{{packageId}}/{{version}}": {
						"sha512": "",
						"type": "package",
						"path": "{{packageId.ToLowerInvariant()}}/{{version}}",
						"files": []
					}
				}
			}
			"""
		);
	}

	[Test]
	public async Task InstallPurviewSkills_RepeatsTheSkillFlagPerSkill(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillInstaller", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.Example", "1.2.3", ["purview-alpha", "purview-beta"]);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, "Purview.Example", "1.2.3");

		// 'echo' stands in for the CLI so the test asserts on the command without needing Node, a
		// network, or a real install. Exec runs through the platform shell, and echo is a builtin of
		// both cmd and sh, so this is identical on Windows, Linux and macOS.
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\" "
				+ "-p:PurviewSkillsCommand=echo -p:PurviewSkillsAgents=claude-code",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));

		// The pinned source, then one --skill flag per resolved skill, then the agent selection.
		await Assert.That(stdOut).Contains("purview-dev/purview-example#0123456789abcdef0123456789abcdef01234567");
		await Assert.That(stdOut).Contains("--skill \"purview-alpha\" --skill \"purview-beta\"");
		await Assert.That(stdOut).Contains("--agent \"claude-code\"");
		await Assert.That(stdOut).Contains("-y");
		// The comma form is the bug this test exists to prevent.
		await Assert.That(stdOut).DoesNotContain("purview-alpha,purview-beta");
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

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.Example", "1.2.3", ["purview-alpha"]);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, "Purview.Example", "1.2.3");

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\"",
			cancellationToken
		);

		// It fails, but with a diagnostic that names the fix rather than a raw shell error.
		await Assert.That(exitCode).IsNotEqualTo(0);
		var output = stdOut + stdErr;
		await Assert.That(output).Contains("PRSKL0004");
		await Assert.That(output).Contains("npm install -g skills");
		await Assert.That(output).Contains("ListPurviewSkills");
	}

	[Test]
	public async Task ListPurviewSkills_ReportsCliAvailabilityWithoutNeedingIt(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync(
			"SkillListNoCli",
			extraEnv: new Dictionary<string, string> { ["PATH"] = string.Empty },
			cancellationToken: cancellationToken
		);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.Example", "1.2.3", ["purview-alpha"]);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, "Purview.Example", "1.2.3");

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ListPurviewSkills -v:n "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\"",
			cancellationToken
		);

		// Listing resolves and reports with no Node tooling present - it must not fail.
		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut).Contains("Skills CLI: not found on PATH");
		await Assert.That(stdOut).Contains("purview-dev/purview-example");
	}

	[Test]
	public async Task InstallPurviewSkills_WithExplicitSource_NeedsNoRestore(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync(
			"SkillExplicitSource",
			cancellationToken: cancellationToken
		);

		// No ProjectAssetsFile is written: an explicit source does not come from the restore graph,
		// so PRSKL0001 must not fire.
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:InstallPurviewSkills -v:n "
				+ "-p:ProjectAssetsFile=\"does-not-exist.json\" "
				+ "-p:PurviewSkillsSource=purview-dev/engineering-playbook#abc123 "
				+ "-p:PurviewSkillsCommand=echo -p:PurviewSkillsAgents=claude-code",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).DoesNotContain("PRSKL0001");
		await Assert.That(stdOut).Contains("add \"purview-dev/engineering-playbook#abc123\"");
	}
}

#pragma warning restore CA1308
