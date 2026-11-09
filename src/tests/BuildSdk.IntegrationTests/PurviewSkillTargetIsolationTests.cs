using System.Xml.Linq;
using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// <c>InstallPurviewSkills</c>, <c>UpdatePurviewSkills</c> and <c>FixPurviewSkills</c> change the
/// repository - they run the skills CLI or delete files - so they must only ever run because a person
/// named them. Two layers keep it that way, and each is tested: nothing in the SDK hooks or depends on
/// them, and a guard refuses to run when something else has pulled one into a build, a restore or a
/// design-time build.
/// </summary>
sealed class PurviewSkillTargetIsolationTests
{
	static readonly string[] ExplicitTargets =
	[
		"InstallPurviewSkills",
		"UpdatePurviewSkills",
		"FixPurviewSkills",
		"CheckPurviewSkills",
		// The leader-side halves the public targets hand their work to; reaching one directly is
		// reaching the work.
		"_InstallPurviewSkillsCore",
		"_UpdatePurviewSkillsCore",
		"_FixPurviewSkillsCore",
		"_CheckPurviewSkillsCore",
	];

	static IEnumerable<string> SdkProjectFiles() =>
		Directory
			.EnumerateFiles(SdkPaths.SdkDirectory, "*.*", SearchOption.AllDirectories)
			.Where(path =>
				path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
				|| path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
			);

	static string[] SplitTargetList(string? value) =>
		(value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	[Test]
	public async Task ExplicitTargets_AreNeverHookedOrDependedOn()
	{
		var problems = new List<string>();

		foreach (var file in SdkProjectFiles())
		{
			var document = XDocument.Load(file);
			var name = Path.GetFileName(file);

			foreach (var target in document.Descendants().Where(element => element.Name.LocalName == "Target"))
			{
				var targetName = (string?)target.Attribute("Name") ?? string.Empty;

				if (ExplicitTargets.Contains(targetName, StringComparer.OrdinalIgnoreCase))
				{
					foreach (var hook in new[] { "BeforeTargets", "AfterTargets" })
					{
						if (target.Attribute(hook) is not null)
							problems.Add($"{name}: {targetName} has {hook}, so it would run without being named");
					}

					continue;
				}

				foreach (var attribute in new[] { "BeforeTargets", "AfterTargets", "DependsOnTargets" })
				{
					foreach (var referenced in SplitTargetList((string?)target.Attribute(attribute)))
					{
						if (ExplicitTargets.Contains(referenced, StringComparer.OrdinalIgnoreCase))
							problems.Add($"{name}: {targetName} references {referenced} in {attribute}");
					}
				}
			}

			// $(BuildDependsOn), $(CoreBuildDependsOn) and friends are the other way to splice a
			// target into an ordinary build.
			foreach (
				var property in document
					.Descendants()
					.Where(element => element.Name.LocalName.EndsWith("DependsOn", StringComparison.Ordinal))
			)
			{
				foreach (var referenced in SplitTargetList(property.Value))
				{
					if (ExplicitTargets.Contains(referenced, StringComparer.OrdinalIgnoreCase))
						problems.Add($"{name}: {property.Name.LocalName} includes {referenced}");
				}
			}
		}

		await Assert.That(problems).IsEmpty().Because(string.Join(Environment.NewLine, problems));
	}

	[Test]
	[Arguments("UpdatePurviewSkills", "BuildingProject")]
	[Arguments("InstallPurviewSkills", "BuildingProject")]
	[Arguments("FixPurviewSkills", "BuildingProject")]
	[Arguments("UpdatePurviewSkills", "DesignTimeBuild")]
	[Arguments("InstallPurviewSkills", "DesignTimeBuild")]
	[Arguments("InstallPurviewSkills", "MSBuildIsRestoring")]
	public async Task ExplicitTargets_RefuseToRunInsideABuild(
		string target,
		string context,
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillGuard", cancellationToken: cancellationToken);
		SkillPackageFixtures.WriteLockFile(
			Path.Combine(harness.SolutionDirectory, "skills-lock.json"),
			("web-design", "vercel-labs/agent-skills", null)
		);

		// 'echo' would succeed, so reaching the CLI at all would show up as a pass.
		var (code, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-v:n -t:{target} -p:{context}=true "
				+ $"-p:PurviewSkillsInstallRoot=\"{harness.SolutionDirectory}\" "
				+ "-p:PurviewSkillsSource=vercel-labs/agent-skills -p:PurviewSkillsAgents=universal "
				+ "-p:PurviewSkillsCommand=purview-guard-reached-the-cli",
			cancellationToken
		);

		var output = stdOut + stdErr;
		await Assert.That(code).IsNotEqualTo(0);
		await Assert.That(output).Contains("PRSKL0009");
		await Assert.That(output).DoesNotContain("purview-guard-reached-the-cli add");
	}

	[Test]
	public async Task AConsumerHook_CannotRunUpdateDuringABuild(CancellationToken cancellationToken)
	{
		// The shape somebody might reasonably try: chaining update onto every build.
		// The default telemetry packages would need Central Package Management versions the throwaway
		// solution does not declare, and are not part of this scenario.
		using var harness = await ProjectHarness.CreateAsync(
			"SkillHook",
			extraProps: """
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			""",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "Directory.Build.targets"),
			$"""
			<Project>
				<Import Project="{SdkPaths.SdkDirectory}/Sdk.targets" />
				<Target Name="UpdateSkillsOnBuild" AfterTargets="Build" DependsOnTargets="UpdatePurviewSkills" />
			</Project>
			""",
			cancellationToken
		);

		var (success, output, errors) = await harness.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSKL0009");
	}
}
