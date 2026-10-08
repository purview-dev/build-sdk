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
sealed class RepositoryFileSyncTests
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

	/// <summary>
	/// The mirrored files have to be ignored per file rather than by a blanket rule. The packaged
	/// blanket '.gitignore' only covers a folder the SDK wholly owns (a skill is its own folder), so
	/// files mirrored directly into '.agents/agents' and '.agents/prompts' stayed tracked and every SDK
	/// upgrade that changed one of them showed up as a local modification. A blanket rule in those two
	/// folders is not an option: a consuming repository keeps its own authored prompts and agents
	/// beside the mirrored ones, and ignoring those would hide the author's work.
	/// </summary>
	[Test]
	public async Task AgentFolderSync_IgnoresMirroredFilesPerFile_AndLeavesAuthoredFilesTracked(
		CancellationToken cancellationToken
	)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var gitIgnorePath = Path.Combine(h.SolutionDirectory, AgentsFolder, ".gitignore");
		await Assert.That(File.Exists(gitIgnorePath)).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var lines = await File.ReadAllLinesAsync(gitIgnorePath, cancellationToken);
		// The self-ignoring entry is asserted separately below.
		var entries = lines.Where(static line => line.StartsWith('/') && line != "/.gitignore").ToArray();

		// Every mirrored file, at whatever depth, listed by path - never a bare '*'.
		await Assert
			.That(entries)
			.IsEquivalentTo(["/agents/guide.md", "/prompts/nested/deep/prompt.md", "/skills/demo/SKILL.md"]);
		// Generated, so it ignores itself: committing it, or leaving it untracked without the entry,
		// would show up as a repository modification - the thing this exists to prevent.
		await Assert.That(lines).Contains("/.gitignore");
		await Assert.That(lines).DoesNotContain("*");

		// A file the repository authored beside a mirrored one must not be covered.
		await Assert.That(entries.Any(static entry => entry.Contains("authored", StringComparison.Ordinal))).IsFalse();

		// A no-op build must not rewrite it, so the working tree stays clean.
		var written = File.GetLastWriteTimeUtc(gitIgnorePath);
		var (secondSuccess, secondOutput, secondErrors) = await h.BuildAsync(
			restore: false,
			verbose: true,
			cancellationToken
		);

		await Assert.That(secondSuccess).IsTrue().Because(TestHelpers.GenerateError(secondOutput, secondErrors));
		await Assert.That(File.GetLastWriteTimeUtc(gitIgnorePath)).IsEqualTo(written);
	}

	/// <summary>
	/// Git applies ignore rules only to untracked paths, so a mirrored file that is tracked - usually
	/// committed before the SDK started ignoring it per file - is rewritten by every upgrade that changes
	/// it and shows up as a local modification, however correct the generated '.gitignore' is. The
	/// repository has to untrack it once and the SDK cannot do that for it, so it has to say so: left
	/// unsaid, the symptom reads as the SDK dirtying the working tree.
	/// </summary>
	[Test]
	public async Task AgentFolderSync_TrackedMirroredFile_WarnsWithTheUntrackCommand(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);
		await TrackMirroredGuideAsync(h, cancellationToken);

		// Act
		// A changed source makes the next build mirror again, which is when the check runs.
		await WriteAsync(SourcePath(h, "agents", "guide.md"), "# Guide v2\n", cancellationToken);
		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		// Assert
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var log = output + errors;
		await Assert.That(log).Contains("PurviewTrackedMirroredFile");
		await Assert.That(log).Contains(".agents/agents/guide.md");
		// The warning has to carry the remedy: the fix is a git command the SDK cannot run itself.
		await Assert.That(log).Contains("git rm --cached");
		// An authored file beside a mirrored one is tracked on purpose and must never be named.
		await Assert.That(log).DoesNotContain("authored.md");
	}

	[Test]
	public async Task AgentFolderSync_UntrackedMirroredFiles_DoNotWarn(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);
		await InitialiseRepositoryAsync(h, cancellationToken);

		// Act
		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		// Assert
		// The normal arrangement: the generated .gitignore does its job, so there is nothing to report.
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("PurviewTrackedMirroredFile");
	}

	[Test]
	public async Task AgentFolderSync_TrackedMirroredFile_CheckIsOptOut(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await CreateHarnessAsync(
			extraProps: "<DisableMirroredFileTrackingCheck>true</DisableMirroredFileTrackingCheck>",
			cancellationToken: cancellationToken
		);
		await CreateAgentSourceFilesAsync(h, cancellationToken);
		await TrackMirroredGuideAsync(h, cancellationToken);

		// Act
		await WriteAsync(SourcePath(h, "agents", "guide.md"), "# Guide v2\n", cancellationToken);
		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		// Assert
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("PurviewTrackedMirroredFile");
	}

	/// <summary>
	/// A no-op build must stay quiet and must not pay for a git invocation: the check only runs when
	/// something was actually mirrored, which is both when the churn appears and when the advice applies.
	/// </summary>
	[Test]
	public async Task AgentFolderSync_TrackedMirroredFile_IsNotReportedWhenNothingWasMirrored(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);
		await CreateAgentSourceFilesAsync(h, cancellationToken);
		await TrackMirroredGuideAsync(h, cancellationToken);

		// Act
		// No source change, so the manifest reports everything already in sync.
		var (success, output, errors) = await h.BuildAsync(restore: false, verbose: true, cancellationToken);

		// Assert
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("0 copied");
		await Assert.That(output + errors).DoesNotContain("PurviewTrackedMirroredFile");
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
	public async Task StylePolicy_DisabledInEditorConfig_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_diagnostic.IDE0040.severity = none\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0007");
	}

	[Test]
	public async Task StylePolicy_ModifierOptionOverride_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_style_require_accessibility_modifiers = always\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0006");
	}

	[Test]
	public async Task StylePolicy_SuppressedThroughNoWarn_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<NoWarn>$(NoWarn);CA1515</NoWarn>",
			cancellationToken: cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0008");
	}

	[Test]
	public async Task StylePolicy_ValidationCanBeDisabled(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<DisablePurviewStylePolicyValidation>true</DisablePurviewStylePolicyValidation>",
			cancellationToken: cancellationToken
		);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_diagnostic.IDE0040.severity = none\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
	}

	[Test]
	public async Task StylePolicy_NamingDiagnosticHidden_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_diagnostic.IDE1006.severity = silent\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0009");
	}

	[Test]
	public async Task StylePolicy_FieldNamingRuleDisabled_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_naming_rule.private_instance_fields_must_be_camel_case_with_underscore_prefix.severity = none\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0009");
	}

	[Test]
	public async Task StylePolicy_FieldPrefixRemoved_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"[*.cs]\ndotnet_naming_style.camel_case_underscore_style.required_prefix =\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("PRSGD0009");
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

	static async Task InitialiseRepositoryAsync(ProjectHarness harness, CancellationToken cancellationToken)
	{
		// Once the folder is a git repository, version detection resolves the repository root here and
		// requires a package.json alongside it; the other tests in this file never reach that path
		// because they stay free of git.
		await WriteAsync(
			Path.Combine(harness.SolutionDirectory, "package.json"),
			"""{ "version": "1.0.0" }""",
			cancellationToken
		);

		var (code, _, stdErr) = await harness.RunGitAsync("init --initial-branch=main", cancellationToken);
		await Assert.That(code).IsZero().Because(stdErr);
	}

	/// <summary>
	/// Mirrors once, then puts the mirrored 'agents/guide.md' in the index - the state a repository is left
	/// in when the file was committed before the SDK began ignoring mirrored files per file. '-f' is
	/// required because the generated '.gitignore' already covers it. An authored file is added beside it
	/// so the check is shown to name only what the manifest owns.
	/// </summary>
	static async Task TrackMirroredGuideAsync(ProjectHarness harness, CancellationToken cancellationToken)
	{
		var (success, output, errors) = await harness.BuildAsync(restore: true, verbose: true, cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		await InitialiseRepositoryAsync(harness, cancellationToken);
		await WriteAsync(DestinationPath(harness, "agents", "authored.md"), "# Authored\n", cancellationToken);

		var (code, _, stdErr) = await harness.RunGitAsync(
			"add -f -- .agents/agents/guide.md .agents/agents/authored.md",
			cancellationToken
		);
		await Assert.That(code).IsZero().Because(stdErr);
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
