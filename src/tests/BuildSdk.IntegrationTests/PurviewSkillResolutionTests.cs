using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the package-to-skill resolution that backs the explicit <c>InstallPurviewSkills</c>
/// workflow: which packages contribute skills, which git source and commit each resolves to, and
/// that resolution never happens as a side effect of an ordinary build.
/// </summary>
sealed class PurviewSkillResolutionTests
{
	static void WriteFakePackage(
		string packagesRoot,
		string packageId,
		string version,
		string[] skillNames,
		string? repositoryUrl = "https://github.com/purview-dev/purview-example/",
		string? repositoryCommit = SkillPackageFixtures.DefaultCommit
	) =>
		SkillPackageFixtures.WritePackage(
			packagesRoot,
			packageId,
			version,
			skillNames,
			repositoryUrl,
			repositoryCommit
		);

	static void WriteAssetsFile(string assetsPath, params (string Id, string Version)[] packages) =>
		SkillPackageFixtures.WriteAssetsFile(assetsPath, packages);

	static async Task<IReadOnlyList<Dictionary<string, string>>> ResolveAsync(
		ProjectHarness harness,
		string packagesRoot,
		string assetsPath,
		string itemType,
		CancellationToken cancellationToken
	)
	{
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-noconlog -t:ResolvePurviewSkillsTarget -getItem:{itemType} "
				+ $"-p:ProjectAssetsFile=\"{assetsPath}\" -p:NuGetPackageFolders=\"{packagesRoot}\"",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));

		var jsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		if (jsonStart < 0)
			return [];

		using var document = JsonDocument.Parse(stdOut[jsonStart..]);
		if (
			!document.RootElement.TryGetProperty("Items", out var items)
			|| !items.TryGetProperty(itemType, out var entries)
		)
		{
			return [];
		}

		return
		[
			.. entries
				.EnumerateArray()
				.Select(entry =>
					entry
						.EnumerateObject()
						.ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty)
				),
		];
	}

	[Test]
	public async Task ResolvePurviewSkills_ResolvesPackageSkillsToGitSourceAndCommit(
		CancellationToken cancellationToken
	)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.Example", "1.2.3", ["purview-alpha", "purview-beta"]);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, ("Purview.Example", "1.2.3"));

		var resolved = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);

		await Assert.That(resolved).Count().IsEqualTo(1);
		await Assert.That(resolved[0]["PackageId"]).IsEqualTo("Purview.Example");
		await Assert.That(resolved[0]["PackageVersion"]).IsEqualTo("1.2.3");
		await Assert.That(resolved[0]["Source"]).IsEqualTo("purview-dev/purview-example");
		// The ref is the commit the restored package was built from, so the skills installed match
		// the package version rather than whatever is currently on the default branch.
		await Assert
			.That(resolved[0]["SourceWithRef"])
			.IsEqualTo("purview-dev/purview-example#0123456789abcdef0123456789abcdef01234567");
		// Skill names come from the folders that actually contain a SKILL.md, in a stable order.
		await Assert.That(resolved[0]["Skills"]).IsEqualTo("purview-alpha;purview-beta");
		await Assert.That(resolved[0]["SkillCount"]).IsEqualTo("2");
	}

	[Test]
	public async Task ResolvePurviewSkills_ExcludesPackagesWithoutSkills(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.WithSkills", "1.0.0", ["purview-alpha"]);
		WriteFakePackage(packagesRoot, "Purview.NoSkills", "1.0.0", []);
		// A package that is restored but ships nothing agent-related at all.
		Directory.CreateDirectory(Path.Combine(packagesRoot, "unrelated.package", "4.5.6"));

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(
			assetsPath,
			("Purview.WithSkills", "1.0.0"),
			("Purview.NoSkills", "1.0.0"),
			("Unrelated.Package", "4.5.6")
		);

		var resolved = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);

		await Assert.That(resolved).Count().IsEqualTo(1);
		await Assert.That(resolved[0]["PackageId"]).IsEqualTo("Purview.WithSkills");
	}

	[Test]
	public async Task ResolvePurviewSkills_ResolvesEveryContributingPackage(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(
			packagesRoot,
			"Purview.First",
			"1.0.0",
			["purview-first"],
			"https://github.com/purview-dev/first.git",
			"1111111111111111111111111111111111111111"
		);
		WriteFakePackage(
			packagesRoot,
			"Purview.Second",
			"2.0.0",
			["purview-second"],
			"https://github.com/purview-dev/second",
			"2222222222222222222222222222222222222222"
		);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, ("Purview.First", "1.0.0"), ("Purview.Second", "2.0.0"));

		var resolved = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);

		await Assert.That(resolved).Count().IsEqualTo(2);
		// A '.git' suffix and a missing trailing slash are both normal in nuspec repository metadata
		// and must normalise to the CLI's 'owner/repo' form.
		await Assert
			.That(resolved.Select(entry => entry["SourceWithRef"]).Order())
			.IsEquivalentTo(
				(string[])
					[
						"purview-dev/first#1111111111111111111111111111111111111111",
						"purview-dev/second#2222222222222222222222222222222222222222",
					]
			);
	}

	[Test]
	public async Task ResolvePurviewSkills_ReportsPackagesThatCannotBePinned(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		// No repository metadata at all.
		WriteFakePackage(packagesRoot, "Purview.NoRepository", "1.0.0", ["purview-orphan"], null, null);
		// Repository metadata, but no commit to pin to.
		WriteFakePackage(
			packagesRoot,
			"Purview.NoCommit",
			"1.0.0",
			["purview-floating"],
			"https://github.com/purview-dev/no-commit",
			null
		);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, ("Purview.NoRepository", "1.0.0"), ("Purview.NoCommit", "1.0.0"));

		var resolved = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);
		var unresolvable = await ResolveAsync(
			harness,
			packagesRoot,
			assetsPath,
			"PurviewUnresolvableSkillSource",
			cancellationToken
		);

		// Neither is installed from a guessed default branch; both are surfaced instead.
		await Assert.That(resolved).IsEmpty();
		await Assert.That(unresolvable).Count().IsEqualTo(2);
		await Assert
			.That(unresolvable.Select(entry => entry["PackageId"]).Order())
			.IsEquivalentTo((string[])["Purview.NoCommit", "Purview.NoRepository"]);
		await Assert.That(unresolvable.All(entry => entry["Reason"].Length > 0)).IsTrue();
	}

	[Test]
	public async Task ResolvePurviewSkills_IsDeterministicAcrossRepeatedRuns(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync("SkillConsumer", cancellationToken: cancellationToken);

		var packagesRoot = Path.Combine(harness.SolutionDirectory, "packages");
		WriteFakePackage(packagesRoot, "Purview.Example", "1.0.0-preview.3", ["purview-zulu", "purview-alpha"]);

		var assetsPath = Path.Combine(harness.SolutionDirectory, "obj", "project.assets.json");
		WriteAssetsFile(assetsPath, ("Purview.Example", "1.0.0-preview.3"));

		var first = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);
		var second = await ResolveAsync(harness, packagesRoot, assetsPath, "PurviewSkillSource", cancellationToken);

		await Assert.That(first).Count().IsEqualTo(1);
		// A pre-release version resolves like any other, and the skill order does not depend on the
		// order the filesystem happened to return the directories in.
		await Assert.That(first[0]["PackageVersion"]).IsEqualTo("1.0.0-preview.3");
		await Assert.That(first[0]["Skills"]).IsEqualTo("purview-alpha;purview-zulu");
		await Assert.That(second[0]["Skills"]).IsEqualTo(first[0]["Skills"]);
		await Assert.That(second[0]["SourceWithRef"]).IsEqualTo(first[0]["SourceWithRef"]);
	}

	[Test]
	public async Task OrdinaryBuild_DoesNotInstallSkills(CancellationToken cancellationToken)
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

		// A restore is part of the scenario: the point is that a *complete*, ordinary
		// restore-and-build cycle still installs nothing.
		var (success, output, errors) = await harness.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		// Installation is explicit: a build must not write the skills CLI's lockfile, and must not
		// create an agent directory the CLI owns.
		await Assert.That(File.Exists(Path.Combine(harness.SolutionDirectory, "skills-lock.json"))).IsFalse();
		await Assert.That(Directory.Exists(Path.Combine(harness.SolutionDirectory, ".claude", "skills"))).IsFalse();
		await Assert.That(Directory.Exists(Path.Combine(harness.ProjectDirectory, ".claude", "skills"))).IsFalse();
	}
}
