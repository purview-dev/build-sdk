using System.IO.Compression;
using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the Sdk/.agents folder packaging workflow and PurviewAutoSdkPack behaviour.
/// </summary>
sealed class AgentPackFolderTests
{
	[Test]
	public async Task PurviewAutoSdkPack_PacksPhysicalAndLinkedRootAssets(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"PackableProject",
			extraProps: """
			<TargetFramework></TargetFramework>
			<TargetFrameworks>net8.0;net9.0</TargetFrameworks>
			<IsPackable>true</IsPackable>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<PackageReadmeFile>README.md</PackageReadmeFile>
			<PackageLicensesExpression>MIT</PackageLicensesExpression>
			<PackageIcon>purview-logo-light.png</PackageIcon>
			""",
			extraItems: """
			<None Include="..\purview-logo-light.png" Link="Sdk\purview-logo-light.png" />
			""",
			cancellationToken: cancellationToken
		);

		Directory.CreateDirectory(Path.Combine(h.ProjectDirectory, "Sdk"));
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "Sdk", "README.md"),
			"# Package",
			cancellationToken
		);
		await File.WriteAllBytesAsync(
			Path.Combine(h.SolutionDirectory, "purview-logo-light.png"),
			[0xFF, 0xD8, 0xFF, 0xD9],
			cancellationToken
		);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).DoesNotContain("NU5118");
		var packagePath = Directory.GetFiles(feedDirectory, "Test.PackableProject.*.nupkg").Single();
		using var package = await ZipFile.OpenReadAsync(packagePath, cancellationToken);
		var entries = package.Entries.Select(entry => entry.FullName).ToList();
		await Assert.That(entries).Contains("README.md");
		await Assert.That(entries).Contains("purview-logo-light.png");
	}

	[Test]
	public async Task PurviewAutoSdkPack_ExposesSdkDotAgentsInProjectTree(CancellationToken cancellationToken)
	{
		var sdkProjectPath = Path.GetFullPath(Path.Combine(SdkPaths.SdkDirectory, "..", "BuildSdk.csproj"));

		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{sdkProjectPath}\" -nologo -noconlog -t:PrepareForBuild -getItem:None",
			Path.GetDirectoryName(sdkProjectPath)!,
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var jsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		await Assert.That(jsonStart >= 0).IsTrue();

		using var doc = JsonDocument.Parse(stdOut[jsonStart..]);
		var noneItems = doc.RootElement.GetProperty("Items").GetProperty("None").EnumerateArray();
		// Sub-agent definitions and prompts still live physically under Sdk/.agents and flow through
		// the None/PackagePath mapping. Skills do not: they are authored at the repository root so
		// the skills CLI can find them in a git clone, and are packed by IncludePurviewSkillsInPackage.
		var expectedPath = Path.GetFullPath(
			Path.Combine(Path.GetDirectoryName(sdkProjectPath)!, "Sdk", ".agents", "agents", "sdk-consumer-setup.md")
		);

		var agentEntry = noneItems.FirstOrDefault(item =>
			string.Equals(item.GetProperty("FullPath").GetString(), expectedPath, StringComparison.OrdinalIgnoreCase)
		);

		await Assert.That(agentEntry.ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert
			.That(agentEntry.GetProperty("PackagePath").GetString())
			.IsEqualTo(".agents/agents/sdk-consumer-setup.md");
	}

	[Test]
	public async Task PurviewPackRepositorySkills_PacksRootSkillsFolderIntoAgentsSkills(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SkillPackable",
			extraProps: """
			<IsPackable>true</IsPackable>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			<PurviewPackRepositorySkills>true</PurviewPackRepositorySkills>
			""",
			cancellationToken: cancellationToken
		);

		// AGENTS.md is the repository-root marker PurviewSkillsSourceFolder resolves against.
		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, "AGENTS.md"), "# Agents", cancellationToken);

		var skillDirectory = Path.Combine(h.SolutionDirectory, "skills", "purview-sample");
		Directory.CreateDirectory(Path.Combine(skillDirectory, "references"));
		await File.WriteAllTextAsync(
			Path.Combine(skillDirectory, "SKILL.md"),
			"---\nname: purview-sample\ndescription: Use when exercising skill packaging.\n---\n",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(skillDirectory, "references", "detail.md"),
			"# Detail",
			cancellationToken
		);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory.GetFiles(feedDirectory, "Test.SkillPackable.*.nupkg").Single();
		using var package = await ZipFile.OpenReadAsync(packagePath, cancellationToken);
		var entries = package.Entries.Select(entry => entry.FullName).ToList();

		// The package delivers the skills through the same .agents/skills path the mirror reads, so
		// an existing consumer receives them with no change in behaviour.
		await Assert.That(entries).Contains(".agents/skills/purview-sample/SKILL.md");
		await Assert.That(entries).Contains(".agents/skills/purview-sample/references/detail.md");
		// And each packed skill folder carries the generated ignore that keeps a package upgrade
		// from showing up as a local modification in the consuming repository.
		await Assert.That(entries).Contains(".agents/skills/purview-sample/.gitignore");
	}

	[Test]
	public async Task PurviewSkillsToPack_PacksOnlyTheNamedSkills(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SkillSubset",
			extraProps: """
			<IsPackable>true</IsPackable>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			<PurviewPackRepositorySkills>true</PurviewPackRepositorySkills>
			<PurviewSkillsToPack>purview-shipped</PurviewSkillsToPack>
			""",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, "AGENTS.md"), "# Agents", cancellationToken);

		foreach (var skillName in (string[])["purview-shipped", "purview-repo-local"])
		{
			var skillDirectory = Path.Combine(h.SolutionDirectory, "skills", skillName);
			Directory.CreateDirectory(skillDirectory);
			await File.WriteAllTextAsync(
				Path.Combine(skillDirectory, "SKILL.md"),
				$"---\nname: {skillName}\ndescription: Use when exercising skill subsetting.\n---\n",
				cancellationToken
			);
		}

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory.GetFiles(feedDirectory, "Test.SkillSubset.*.nupkg").Single();
		using var package = await ZipFile.OpenReadAsync(packagePath, cancellationToken);
		var entries = package.Entries.Select(entry => entry.FullName).ToList();

		await Assert.That(entries).Contains(".agents/skills/purview-shipped/SKILL.md");
		await Assert.That(entries).Contains(".agents/skills/purview-shipped/.gitignore");
		// A skill authored in the repository but not named is installable from git and absent from
		// the package - which is how one repository ships different skills in different packages.
		await Assert
			.That(entries.Any(entry => entry.Contains("purview-repo-local", StringComparison.Ordinal)))
			.IsFalse();
	}

	[Test]
	public async Task PurviewAutoSdkPack_PacksSdkDotAgentsSkillsIntoNuGetPackage(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await ProjectHarness.CreateAsync(
			"PackableProject",
			extraProps: "<IsPackable>true</IsPackable><DisableSourceLink>true</DisableSourceLink><EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "packable-project", "version": "1.0.0"}""",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

		var agentPackSkillsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "skills", "observability");
		Directory.CreateDirectory(agentPackSkillsDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(agentPackSkillsDirectory, "SKILL.md"),
			"# Observability\n",
			cancellationToken
		);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		Directory.CreateDirectory(feedDirectory);
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

		// Act
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory
			.GetFiles(feedDirectory, $"Test.PackableProject.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert.That(packagePath).IsNotNull().Because("The packed project package was not created.");

		using var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken);
		var entries = zip.Entries.Select(entry => entry.FullName).ToList();
		await Assert.That(entries).Contains(".agents/skills/observability/SKILL.md");
		await Assert
			.That(entries)
			.Contains(".agents/skills/observability/.gitignore")
			.Because($"{stdOut}\n{stdErr}\n--- package entries ---\n{string.Join("\n", entries)}");

		var gitIgnoreEntry = zip.Entries.Single(entry => entry.FullName == ".agents/skills/observability/.gitignore");
		using var gitIgnoreStream = await gitIgnoreEntry.OpenAsync(cancellationToken);
		using var reader = new StreamReader(gitIgnoreStream);
		var gitIgnoreContent = (await reader.ReadToEndAsync(cancellationToken)).ReplaceLineEndings("\n");
		await Assert
			.That(gitIgnoreContent)
			.IsEqualTo(
				// The last entry ignores this file itself. Re-including it ('!.gitignore') left a
				// mirrored, never-committed file permanently untracked in every consuming repository,
				// and overrode the generated '.agents/.gitignore' because the nearest file wins.
				"# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# This file is mirrored from a NuGet package, so ignore it too\n/.gitignore"
			);
	}

	[Test]
	public async Task PurviewAutoSdkPack_PacksSdkDotAgentsContentOutsideSkillsIntoNuGetPackage(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await ProjectHarness.CreateAsync(
			"PackableProject",
			extraProps: "<IsPackable>true</IsPackable><DisableSourceLink>true</DisableSourceLink><EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "packable-project", "version": "1.0.0"}""",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

		var agentPackPromptsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "prompts", "example");
		Directory.CreateDirectory(agentPackPromptsDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(agentPackPromptsDirectory, "PROMPT.md"),
			"# Prompt\n",
			cancellationToken
		);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		Directory.CreateDirectory(feedDirectory);
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

		// Act
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory
			.GetFiles(feedDirectory, $"Test.PackableProject.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert.That(packagePath).IsNotNull().Because("The packed project package was not created.");

		using var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken);
		var entries = zip.Entries.Select(entry => entry.FullName).ToList();
		await Assert.That(entries).Contains(".agents/prompts/example/PROMPT.md");
		await Assert
			.That(entries)
			.Contains(".agents/prompts/example/.gitignore")
			.Because($"{stdOut}\n{stdErr}\n--- package entries ---\n{string.Join("\n", entries)}");
	}

	/// <summary>
	/// A blanket '.gitignore' is packed only for a folder this SDK wholly owns - the two tests above
	/// use nested folders ('skills/observability', 'prompts/example'), which is that case. It must
	/// **not** be packed for a file sitting directly in '.agents/prompts' or '.agents/agents', because
	/// in a consuming repository those folders hold the mirrored files beside the repository's own
	/// authored prompts and agents: a blanket rule there would hide the author's work. Those files are
	/// ignored per file by the consumer-side sync instead - see
	/// <c>RepositoryFileSyncTests.AgentFolderSync_IgnoresMirroredFilesPerFile_AndLeavesAuthoredFilesTracked</c>.
	/// </summary>
	[Test]
	public async Task PurviewAutoSdkPack_DoesNotPackABlanketGitIgnoreForASharedAgentFolder(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await ProjectHarness.CreateAsync(
			"PackableProject",
			extraProps: "<IsPackable>true</IsPackable><DisableSourceLink>true</DisableSourceLink><EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "packable-project", "version": "1.0.0"}""",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

		// Files directly inside the first-level folders, exactly as the SDK's own Sdk/.agents is laid out.
		var promptsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "prompts");
		Directory.CreateDirectory(promptsDirectory);
		await File.WriteAllTextAsync(Path.Combine(promptsDirectory, "diagnose.md"), "# Diagnose\n", cancellationToken);

		var agentsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "agents");
		Directory.CreateDirectory(agentsDirectory);
		await File.WriteAllTextAsync(Path.Combine(agentsDirectory, "setup.md"), "# Setup\n", cancellationToken);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		Directory.CreateDirectory(feedDirectory);
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

		// Act
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory
			.GetFiles(feedDirectory, $"Test.PackableProject.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert.That(packagePath).IsNotNull().Because("The packed project package was not created.");

		using var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken);
		var entries = zip.Entries.Select(entry => entry.FullName).ToList();
		var because = $"{stdOut}\n{stdErr}\n--- package entries ---\n{string.Join("\n", entries)}";

		// The content itself still ships.
		await Assert.That(entries).Contains(".agents/prompts/diagnose.md").Because(because);
		await Assert.That(entries).Contains(".agents/agents/setup.md").Because(because);

		// ...but without a blanket ignore over a folder the consumer also authors into.
		await Assert.That(entries).DoesNotContain(".agents/prompts/.gitignore").Because(because);
		await Assert.That(entries).DoesNotContain(".agents/agents/.gitignore").Because(because);
	}

	[Test]
	public async Task PurviewAutoSdkPack_PacksAllSdkRootFoldersIntoNuGetPackage(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await ProjectHarness.CreateAsync(
			"PackableProject",
			extraProps: "<IsPackable>true</IsPackable><DisableSourceLink>true</DisableSourceLink><EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "packable-project", "version": "1.0.0"}""",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

		var sdkAgentSkillsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "skills", "test");
		Directory.CreateDirectory(sdkAgentSkillsDirectory);
		await File.WriteAllTextAsync(Path.Combine(sdkAgentSkillsDirectory, "SKILL.md"), "# Test\n", cancellationToken);

		var sdkAgentAgentsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "agents");
		Directory.CreateDirectory(sdkAgentAgentsDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(sdkAgentAgentsDirectory, "this-is-a-test-agent.md"),
			"# Test\n",
			cancellationToken
		);

		var sdkAgentPromptsDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".agents", "prompts");
		Directory.CreateDirectory(sdkAgentPromptsDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(sdkAgentPromptsDirectory, "this-is-a-test-prompt.md"),
			"# Test\n",
			cancellationToken
		);

		var sdkGitHubDirectory = Path.Combine(h.ProjectDirectory, "Sdk", ".github", "workflows");
		Directory.CreateDirectory(sdkGitHubDirectory);
		await File.WriteAllTextAsync(Path.Combine(sdkGitHubDirectory, "ci.yml"), "name: CI\n", cancellationToken);

		var sdkBuildDirectory = Path.Combine(h.ProjectDirectory, "Sdk", "build");
		Directory.CreateDirectory(sdkBuildDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(sdkBuildDirectory, "Custom.targets"),
			"<Project />\n",
			cancellationToken
		);

		var sdkBuildTransitiveDirectory = Path.Combine(h.ProjectDirectory, "Sdk", "buildTransitive");
		Directory.CreateDirectory(sdkBuildTransitiveDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(sdkBuildTransitiveDirectory, "Custom.props"),
			"<Project />\n",
			cancellationToken
		);

		var sdkBuildMultiTargetingDirectory = Path.Combine(h.ProjectDirectory, "Sdk", "buildMultiTargeting");
		Directory.CreateDirectory(sdkBuildMultiTargetingDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(sdkBuildMultiTargetingDirectory, "Custom.props"),
			"<Project />\n",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "Sdk", "README.md"),
			"# README\n",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "Sdk", "logo.svg"),
			"<svg />\n",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "Sdk", "Custom.props"),
			"<Project />\n",
			cancellationToken
		);

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		Directory.CreateDirectory(feedDirectory);
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

		// Act
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{h.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			h.SolutionDirectory,
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packagePath = Directory
			.GetFiles(feedDirectory, $"Test.PackableProject.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert.That(packagePath).IsNotNull().Because("The packed project package was not created.");

		using var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken);
		var entries = zip.Entries.Select(entry => entry.FullName).ToList();

		await Assert.That(entries).Contains(".agents/skills/test/SKILL.md");
		await Assert.That(entries).Contains(".agents/skills/test/.gitignore");
		await Assert.That(entries).Contains(".agents/prompts/this-is-a-test-prompt.md");
		await Assert.That(entries).Contains(".agents/agents/this-is-a-test-agent.md");
		await Assert.That(entries).Contains(".github/workflows/ci.yml");
		await Assert.That(entries).Contains("build/Custom.targets");
		await Assert.That(entries).Contains("buildTransitive/Custom.props");
		await Assert.That(entries).Contains("buildMultiTargeting/Custom.props");
		await Assert.That(entries).Contains("README.md");
		await Assert.That(entries).Contains("logo.svg");
		await Assert.That(entries).Contains("Sdk/Custom.props");
	}

	static async Task<(int Code, string StdOut, string StdErr)> RunProcessAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken
	)
	{
		using var process = new System.Diagnostics.Process
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		process.Start();
		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdoutTask, await stderrTask);
	}
}
