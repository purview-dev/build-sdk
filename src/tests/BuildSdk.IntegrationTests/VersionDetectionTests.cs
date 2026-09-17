using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies SDK version detection from package.json: property defaults, explicit and
/// auto-discovered package.json paths, opt-out behaviour, and build-time validation errors.
/// </summary>
public sealed partial class VersionDetectionTests
{
	[Test]
	public async Task UsePackageJsonVersion_DefaultsToTrue(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("UsePackageJsonVersion", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task UsePackageJsonVersion_CanBeSetFalse(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<UsePackageJsonVersion>false</UsePackageJsonVersion>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("UsePackageJsonVersion", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task UsePackageJsonVersion_CanBeSetStrict(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<UsePackageJsonVersion>Strict</UsePackageJsonVersion>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("UsePackageJsonVersion", cancellationToken)).IsEqualTo("Strict");
	}

	[Test]
	public async Task RootPackageJsonWasSpecified_FalseByDefault(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert
			.That(await h.GetPropertyAsync("_RootPackageJsonWasSpecified", cancellationToken))
			.IsEqualTo("false");
	}

	[Test]
	public async Task RootPackageJsonWasSpecified_TrueWhenExplicitlySet(CancellationToken cancellationToken)
	{
		// RootPackageJson must be set before Sdk.props is imported (pre-import props),
		// otherwise _RootPackageJsonWasSpecified is evaluated before the project file's
		// own PropertyGroups are processed.
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("_RootPackageJsonWasSpecified", cancellationToken))
			.IsEqualTo("true");
	}

	// ---------- explicit RootPackageJson ----------

	[Test]
	public async Task Version_SetFromExplicitPackageJson(CancellationToken cancellationToken)
	{
		// RootPackageJson must be set before Sdk.props import so VersionDetection.props
		// sees the value during its evaluation phase.
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "1.2.3"}""",
			cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("Version", cancellationToken)).IsEqualTo("1.2.3");
	}

	[Test]
	public async Task PackageVersion_SetFromExplicitPackageJson(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "4.5.6"}""",
			cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("PackageVersion", cancellationToken)).IsEqualTo("4.5.6");
	}

	// ---------- auto-discovery via .git marker ----------

	[Test]
	public async Task Version_SetFromAutoDiscoveredPackageJson(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		// Place a .git marker in the project directory so GetDirectoryNameOfFileAbove
		// resolves RepoRoot to the project directory, then put package.json alongside it.
		await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, ".git"), "", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "7.8.9"}""",
			cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("Version", cancellationToken)).IsEqualTo("7.8.9");
	}

	[Test]
	public async Task PackageVersion_SetFromAutoDiscoveredPackageJson(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, ".git"), "", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "3.0.1"}""",
			cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("PackageVersion", cancellationToken)).IsEqualTo("3.0.1");
	}

	[Test]
	public async Task Version_SetFromGitHubWorkspacePackageJson_WhenGitMarkersAreUnavailable(
		CancellationToken cancellationToken
	)
	{
		var workspaceRoot = Path.Combine(Path.GetTempPath(), $"PurviewSdkWorkspace_{Guid.NewGuid():N}");

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			using var h = await ProjectHarness.CreateAsync(
				"MyLibrary",
				extraEnv: new Dictionary<string, string> { ["GITHUB_WORKSPACE"] = workspaceRoot },
				cancellationToken: cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(workspaceRoot, "package.json"),
				/*lang=json,strict*/
				"""{"name": "my-lib", "version": "2.4.6"}""",
				cancellationToken
			);

			await Assert.That(await h.GetPropertyAsync("Version", cancellationToken)).IsEqualTo("2.4.6");
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[Test]
	public async Task VersionDetection_UsesSessionCache_WhenGitMarkerIsRemoved(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, ".git"), "", cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "6.7.8"}""",
			cancellationToken
		);

		var cacheFile = await h.GetPropertyAsync("VersionDetectionCacheFile", cancellationToken);
		await Assert.That(cacheFile).IsNotEqualTo(string.Empty);

		var (exitCode, _) = await RunMSBuildAsync(h, "-t:WriteVersionDetectionCache", cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0);
		await Assert.That(File.Exists(cacheFile)).IsTrue();

		File.Delete(Path.Combine(h.ProjectDirectory, ".git"));

		// If cache isn't used, RepoRoot discovery fails and Version falls back to 0.0.1.
		await Assert.That(await h.GetPropertyAsync("Version", cancellationToken)).IsEqualTo("6.7.8");
	}

	// ---------- opt-out disables version detection ----------

	[Test]
	public async Task Version_NotSetFromPackageJson_WhenUsePackageJsonVersionFalse(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<UsePackageJsonVersion>false</UsePackageJsonVersion><RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "9.9.9"}""",
			cancellationToken
		);

		// UsePackageJsonVersion=false → SDK falls back to its own default (0.0.1)
		await Assert.That(await h.GetPropertyAsync("Version", cancellationToken)).IsEqualTo("0.0.1");
	}

	// ---------- ValidatePackageJsonVersion build-time errors ----------

	[Test]
	public async Task Build_Errors_WhenExplicitPackageJsonNotFound(CancellationToken cancellationToken)
	{
		// RootPackageJson must be set pre-import so _RootPackageJsonWasSpecified=true,
		// which is required for ValidatePackageJsonVersion to emit the explicit-path error.
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>missing-package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("does not exist");
	}

	[Test]
	public async Task Build_Errors_WhenAutoDiscoveredRepoRootHasNoPackageJson(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		// .git marker makes RepoRoot = ProjectDirectory, but no package.json is created.
		await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, ".git"), "", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("no package.json exists at");
	}

	[Test]
	public async Task Build_Errors_WhenPackageJsonHasNoVersionProperty(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib"}""",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("no version property could be read");
	}

	// ---------- version detection logging ----------

	[Test]
	public async Task Build_DoesNotLogDetectedVersion_ByDefault(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "5.4.3"}""",
			cancellationToken
		);

		var (exitCode, output) = await RunMSBuildAsync(
			h,
			"-v:minimal -t:ValidatePackageJsonVersion",
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0);
		await Assert.That(output).DoesNotContain("Detected package version '5.4.3' from");
	}

	[Test]
	public async Task Build_DoesNotLogDetectedVersion_WhenConfigurationIsRelease(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			extraProps: "<Configuration>Release</Configuration>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "5.4.3"}""",
			cancellationToken
		);

		var (exitCode, output) = await RunMSBuildAsync(
			h,
			"-v:minimal -t:ValidatePackageJsonVersion",
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0);
		await Assert.That(output).DoesNotContain("Detected package version '5.4.3' from");
	}

	[Test]
	public async Task Build_DoesNotLogDetectedVersion_WhenIsPackable(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson>",
			extraProps: "<IsPackable>true</IsPackable>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "5.4.3"}""",
			cancellationToken
		);

		var (exitCode, output) = await RunMSBuildAsync(
			h,
			"-v:minimal -t:ValidatePackageJsonVersion",
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0);
		await Assert.That(output).DoesNotContain("Detected package version '5.4.3' from");
	}

	[Test]
	public async Task Build_LogsDetectedVersion_WhenExplicitlyEnabled(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<RootPackageJson>package.json</RootPackageJson><VersionDetectionLogEnabled>true</VersionDetectionLogEnabled>",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "5.4.3"}""",
			cancellationToken
		);

		var (exitCode, output) = await RunMSBuildAsync(
			h,
			"-v:minimal -t:ValidatePackageJsonVersion",
			cancellationToken
		);

		await Assert.That(exitCode).IsEqualTo(0);
		await Assert.That(output).Contains("Detected package version '5.4.3' from");
	}

	[Test]
	public async Task Build_Errors_WhenUsePackageJsonVersionStrict_AndNoPackageJsonSourceIsFound(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<UsePackageJsonVersion>Strict</UsePackageJsonVersion>",
			cancellationToken: cancellationToken
		);

		var (exitCode, output) = await RunMSBuildAsync(
			h,
			"-v:minimal -t:ValidatePackageJsonVersion",
			cancellationToken
		);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(output).Contains("UsePackageJsonVersion=Strict requires resolving version from package.json");
	}

	[Test]
	public async Task Build_Strict_Succeeds_WhenRepoHasGitDirectoryMarkerAndRootPackageJson(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<UsePackageJsonVersion>Strict</UsePackageJsonVersion>",
			cancellationToken: cancellationToken
		);

		Directory.CreateDirectory(Path.Combine(h.ProjectDirectory, ".git"));
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, ".git", "HEAD"),
			"ref: refs/heads/main",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.ProjectDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "my-lib", "version": "8.8.8"}""",
			cancellationToken
		);

		var (exitCode, _) = await RunMSBuildAsync(h, "-v:minimal -t:ValidatePackageJsonVersion", cancellationToken);

		await Assert.That(exitCode).IsEqualTo(0);
	}

	/// <summary>
	/// Runs <c>dotnet msbuild</c> against the throwaway project with the host CI environment
	/// stripped out (GITHUB_WORKSPACE, GITHUB_ACTIONS, etc.), so version detection and CI
	/// detection behave deterministically on every machine and CI runner.
	/// </summary>
	static async Task<(int ExitCode, string Output)> RunMSBuildAsync(
		ProjectHarness harness,
		string msbuildArguments,
		CancellationToken cancellationToken
	)
	{
		using var process = new System.Diagnostics.Process
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = $"msbuild \"{harness.ProjectFilePath}\" -nologo {msbuildArguments}",
				WorkingDirectory = harness.ProjectDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		ProjectHarness.IsolateFromHostEnvironment(
			process.StartInfo.Environment,
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		);

		process.Start();
		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, (await stdoutTask) + (await stderrTask));
	}
}
