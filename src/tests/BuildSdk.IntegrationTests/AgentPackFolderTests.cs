using System.IO.Compression;
using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the Sdk/.agents folder packaging workflow and PurviewAutoSdkPack behaviour.
/// </summary>
public sealed class AgentPackFolderTests
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
			<PackageLicenseFile>LICENSE.md</PackageLicenseFile>
			<PackageIcon>purview-logo-light.png</PackageIcon>
			""",
			extraItems: """
			<None Include="..\LICENSE.md" Link="Sdk\LICENSE.md" />
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
		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, "LICENSE.md"), "License", cancellationToken);
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
		await Assert.That(entries).Contains("LICENSE.md");
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
		var expectedPath = Path.GetFullPath(
			Path.Combine(
				Path.GetDirectoryName(sdkProjectPath)!,
				"Sdk",
				".agents",
				"skills",
				"sdk-configuration-reference",
				"SKILL.md"
			)
		);

		var agentEntry = noneItems.FirstOrDefault(item =>
			string.Equals(item.GetProperty("FullPath").GetString(), expectedPath, StringComparison.OrdinalIgnoreCase)
		);

		await Assert.That(agentEntry.ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert
			.That(agentEntry.GetProperty("PackagePath").GetString())
			.IsEqualTo(".agents/skills/sdk-configuration-reference/SKILL.md");
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
				"# Ignore all files\n*\n\n# Don't ignore directories, so Git can traverse them\n!*/\n\n# Keep this file\n!.gitignore"
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
