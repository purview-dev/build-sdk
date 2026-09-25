using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies how the SDK synchronises files into shared repository locations
/// (.agents content, .editorconfig and global.json). These destinations are written by
/// every project in a solution, so the behaviour under concurrency, unchanged content
/// and blocked destinations is part of the contract:
///
/// * unchanged content is skipped using the repository manifest,
/// * retries are quiet and only the terminal failure is reported - as an error by
///   default, or a warning when the caller opts out,
/// * bootstrap files are never overwritten unless explicitly requested.
/// </summary>
public sealed class RepositoryFileSyncTests
{
	const string AgentSourceFolder = "agent-source";
	const string AgentsFolder = ".agents";
	const string ManifestRelativePath = ".purview/agent-sync.cache";

	[Test]
	public async Task AgentFolderSync_ColdBuild_MirrorsContentAndWritesManifest(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		// Retry notices are demoted to messages, so they never appear as warnings.
		await Assert.That(output + errors).DoesNotContain("warning MSB3026");
		await Assert.That(output).DoesNotContain("already in sync");

		await Assert
			.That(await File.ReadAllTextAsync(DestinationPath(h, "agents", "guide.md"), cancellationToken))
			.IsEqualTo("# Guide\n");
		await Assert
			.That(await File.ReadAllTextAsync(DestinationPath(h, "skills", "demo", "SKILL.md"), cancellationToken))
			.IsEqualTo("# Skill\n");
		await Assert
			.That(
				await File.ReadAllTextAsync(
					DestinationPath(h, "prompts", "nested", "deep", "prompt.md"),
					cancellationToken
				)
			)
			.IsEqualTo("# Prompt\n");

		var manifestPath = ManifestPath(h);
		await Assert.That(File.Exists(manifestPath)).IsTrue();

		var manifest = await File.ReadAllTextAsync(manifestPath, cancellationToken);
		await Assert.That(manifest).Contains("schema|1");
		await Assert.That(manifest).Contains("group|sdk:");
		await Assert
			.That(manifest.Split('\n').Count(line => line.StartsWith("file|", StringComparison.Ordinal)))
			.IsEqualTo(3);

		// The manifest folder must stay invisible to Git without consumer configuration.
		await Assert
			.That(
				await File.ReadAllTextAsync(
					Path.Combine(h.SolutionDirectory, ".purview", ".gitignore"),
					cancellationToken
				)
			)
			.Contains("*");
	}

	[Test]
	public async Task AgentFolderSync_SecondBuild_SkipsUnchangedContent(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		var (firstSuccess, firstOutput, firstErrors) = await h.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken
		);
		await Assert.That(firstSuccess).IsTrue().Because(TestHelpers.GenerateError(firstOutput, firstErrors));

		var mirroredFile = DestinationPath(h, "agents", "guide.md");
		var manifestPath = ManifestPath(h);
		var mirroredTime = File.GetLastWriteTimeUtc(mirroredFile);
		var manifestTime = File.GetLastWriteTimeUtc(manifestPath);

		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("already in sync");
		await Assert.That(output).Contains("0 copied");
		await Assert.That(output + errors).DoesNotContain("warning MSB3026");
		await Assert.That(File.GetLastWriteTimeUtc(mirroredFile)).IsEqualTo(mirroredTime);
		await Assert.That(File.GetLastWriteTimeUtc(manifestPath)).IsEqualTo(manifestTime);
	}

	[Test]
	public async Task AgentFolderSync_SourceChange_IsMirroredOnNextBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		var (firstSuccess, firstOutput, firstErrors) = await h.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken
		);
		await Assert.That(firstSuccess).IsTrue().Because(TestHelpers.GenerateError(firstOutput, firstErrors));

		await File.WriteAllTextAsync(SourcePath(h, "agents", "guide.md"), "# Guide v2\n", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("already in sync");
		await Assert
			.That(await File.ReadAllTextAsync(DestinationPath(h, "agents", "guide.md"), cancellationToken))
			.IsEqualTo("# Guide v2\n");
	}

	[Test]
	public async Task AgentFolderSync_DeletedDestination_IsRestored(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		var (firstSuccess, firstOutput, firstErrors) = await h.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken
		);
		await Assert.That(firstSuccess).IsTrue().Because(TestHelpers.GenerateError(firstOutput, firstErrors));

		var mirroredFile = DestinationPath(h, "skills", "demo", "SKILL.md");
		File.Delete(mirroredFile);

		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(File.Exists(mirroredFile)).IsTrue();
		await Assert.That(await File.ReadAllTextAsync(mirroredFile, cancellationToken)).IsEqualTo("# Skill\n");
	}

	[Test]
	public async Task AgentFolderSync_BlockedDestination_FailsWithErrorAfterRetries(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: """
			<PurviewAgentFolderCopyRetries>1</PurviewAgentFolderCopyRetries>
			<PurviewAgentFolderCopyRetryDelayMilliseconds>1</PurviewAgentFolderCopyRetryDelayMilliseconds>
			""",
			cancellationToken: cancellationToken
		);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		// A directory occupying the destination file path fails the rename on every
		// platform, which exercises the terminal-failure path deterministically.
		Directory.CreateDirectory(DestinationPath(h, "agents", "guide.md"));

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("The Purview SDK could not copy");
		await Assert.That(output + errors).Contains("guide.md");
		await Assert.That(output + errors).Contains("after 1 attempt");
	}

	[Test]
	public async Task AgentFolderSync_BlockedDestination_WithFailureAsWarning_KeepsBuildPassing(
		CancellationToken cancellationToken
	)
	{
		using var h = await CreateHarnessAsync(
			extraProps: """
			<PurviewAgentFolderCopyRetries>1</PurviewAgentFolderCopyRetries>
			<PurviewAgentFolderCopyRetryDelayMilliseconds>1</PurviewAgentFolderCopyRetryDelayMilliseconds>
			<PurviewAgentFolderCopyFailureAsError>false</PurviewAgentFolderCopyFailureAsError>
			""",
			cancellationToken: cancellationToken
		);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		Directory.CreateDirectory(DestinationPath(h, "agents", "guide.md"));

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("The Purview SDK could not copy");
		await Assert.That(output + errors).Contains("warning");
	}

	[Test]
	public async Task GlobalJsonBootstrap_CreatesFileOnceAndLeavesItUntouched(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var (firstSuccess, firstOutput, firstErrors) = await h.BuildAsync(
			restore: true,
			verbose: true,
			cancellationToken
		);
		await Assert.That(firstSuccess).IsTrue().Because(TestHelpers.GenerateError(firstOutput, firstErrors));

		var globalJsonPath = Path.Combine(h.SolutionDirectory, "global.json");
		await Assert.That(File.Exists(globalJsonPath)).IsTrue();

		var content = await File.ReadAllTextAsync(globalJsonPath, cancellationToken);
		await Assert.That(content).Contains("\"Purview.BuildSdk\"");
		await Assert.That(content).Contains("Microsoft.Testing.Platform");
		var writtenTime = File.GetLastWriteTimeUtc(globalJsonPath);

		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("warning MSB3026");
		await Assert.That(File.GetLastWriteTimeUtc(globalJsonPath)).IsEqualTo(writtenTime);
	}

	[Test]
	public async Task EditorConfigBootstrap_ExistingFile_IsNeverOverwritten(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(editorConfigPath, "# user owned\n", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken)).IsEqualTo("# user owned\n");
	}

	[Test]
	public async Task EditorConfigBootstrap_WarnOnDrift_ReportsWarningAndKeepsUserFile(
		CancellationToken cancellationToken
	)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<PurviewRepoBootstrapMode>WarnOnDrift</PurviewRepoBootstrapMode>",
			cancellationToken: cancellationToken
		);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(editorConfigPath, "# user owned\n", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("was left untouched");
		await Assert.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken)).IsEqualTo("# user owned\n");
	}

	[Test]
	public async Task EditorConfigBootstrap_AlwaysOverwrite_ReplacesUserFile(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<PurviewRepoBootstrapMode>Always</PurviewRepoBootstrapMode>",
			cancellationToken: cancellationToken
		);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(editorConfigPath, "# user owned\n", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert
			.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken))
			.DoesNotContain("# user owned");
	}

	[Test]
	public async Task RepoBootstrapModeNever_SkipsAllBootstrapWrites(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<PurviewRepoBootstrapMode>Never</PurviewRepoBootstrapMode>",
			cancellationToken: cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(File.Exists(Path.Combine(h.SolutionDirectory, "global.json"))).IsFalse();
		await Assert.That(File.Exists(Path.Combine(h.SolutionDirectory, ".editorconfig"))).IsFalse();
	}

	[Test]
	public async Task CopyRetryWarnings_AreDemotedByDefaultAndRestorable(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("MSBuildWarningsAsMessages", cancellationToken)).Contains("MSB3026");

		using var restored = await CreateHarnessAsync(
			extraProps: "<PurviewSuppressCopyRetryWarnings>false</PurviewSuppressCopyRetryWarnings>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await restored.GetPropertyAsync("MSBuildWarningsAsMessages", cancellationToken))
			.DoesNotContain("MSB3026");
	}

	static async Task<ProjectHarness> CreateHarnessAsync(string? extraProps, CancellationToken cancellationToken)
	{
		var props = "<PurviewAgentFolderSourcePath>../" + AgentSourceFolder + "</PurviewAgentFolderSourcePath>";
		if (!string.IsNullOrWhiteSpace(extraProps))
			props += extraProps;

		// The harness only declares SourceLink centrally, so the automatically injected
		// telemetry package references are removed like the other harness-based tests do.
		var harness = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: props,
			extraItems: """
			<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
			<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
			""",
			cancellationToken: cancellationToken
		);

		// An AGENTS.md at the repository root is one of the discovery mechanisms that
		// resolve the .agents destination root, and it keeps the tests free of git.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Agent guidance\n",
			cancellationToken
		);

		return harness;
	}

	static Task CreateAgentSourceFilesAsync(ProjectHarness harness, CancellationToken cancellationToken) =>
		Task.WhenAll(
			WriteAsync(SourcePath(harness, "agents", "guide.md"), "# Guide\n", cancellationToken),
			WriteAsync(SourcePath(harness, "skills", "demo", "SKILL.md"), "# Skill\n", cancellationToken),
			WriteAsync(SourcePath(harness, "prompts", "nested", "deep", "prompt.md"), "# Prompt\n", cancellationToken)
		);

	static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		await File.WriteAllTextAsync(path, content, cancellationToken);
	}

	static string SourcePath(ProjectHarness harness, params string[] segments) =>
		Path.Combine([harness.SolutionDirectory, AgentSourceFolder, .. segments]);

	static string DestinationPath(ProjectHarness harness, params string[] segments) =>
		Path.Combine([harness.SolutionDirectory, AgentsFolder, .. segments]);

	static string ManifestPath(ProjectHarness harness) =>
		Path.Combine(harness.SolutionDirectory, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
}
