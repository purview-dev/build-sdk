using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies how the SDK bootstraps files into shared repository locations - the repository
/// <c>.editorconfig</c> and <c>global.json</c>. One file, one destination, written by every project
/// in a solution, so the behaviour under concurrency is part of the contract:
///
/// * a bootstrap file is never overwritten unless explicitly requested, which turns a lost race
///   between parallel projects into a no-op rather than a build failure,
/// * retries are quiet and only the terminal failure is reported, as an error by default or a
///   warning when the caller opts out.
///
/// This file also covers the style-policy validation that reads the bootstrapped
/// <c>.editorconfig</c>, because the two are only meaningful together.
/// </summary>
sealed class RepositoryFileSyncTests
{
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

	/// <summary>
	/// A <c>.editorconfig</c> carrying the SDK's version marker is one the SDK wrote, so a version change
	/// refreshes it. This is what stops a consuming repository silently running a release-old rule set.
	/// </summary>
	[Test]
	public async Task EditorConfigBootstrap_StaleVersionMarker_IsUpdated(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		await File.WriteAllTextAsync(
			editorConfigPath,
			"# Purview.BuildSdk 0.0.1 - managed file, do not edit\nroot = true\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var updated = await File.ReadAllTextAsync(editorConfigPath, cancellationToken);
		await Assert.That(updated).DoesNotContain("Purview.BuildSdk 0.0.1");
		await Assert.That(updated).Contains("Purview.BuildSdk ");
	}

	/// <summary>
	/// The marker is the whole of the ownership test. Without it the file is the developer's, whatever it
	/// contains, and must survive untouched — which is what makes deleting the marker line a supported way
	/// to take ownership.
	/// </summary>
	[Test]
	public async Task EditorConfigBootstrap_NoVersionMarker_IsLeftAlone(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(extraProps: null, cancellationToken: cancellationToken);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		const string owned = "root = true\n\n[*.cs]\nindent_style = space\n";

		await File.WriteAllTextAsync(editorConfigPath, owned, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken)).IsEqualTo(owned);
	}

	/// <summary>
	/// An unmarked file is left alone, but silence is how a repository bootstrapped before the marker
	/// existed stays pinned to a release-old rule set forever. It is reported once per SDK version, so
	/// the second build of the same state has to say nothing.
	/// </summary>
	[Test]
	public async Task EditorConfigBootstrap_NoVersionMarker_IsReportedOnce(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			// The suite itself runs in CI, where the advice is deliberately silent.
			extraProps: "<PurviewEditorConfigAdoptionWarningInCI>true</PurviewEditorConfigAdoptionWarningInCI>",
			cancellationToken: cancellationToken
		);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		const string owned = "root = true\n\n[*.cs]\nindent_style = space\n";

		await File.WriteAllTextAsync(editorConfigPath, owned, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("carries no Purview.BuildSdk version marker");
		await Assert.That(output + errors).Contains("PurviewRepoBootstrapMode=Always");
		await Assert.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken)).IsEqualTo(owned);

		var (secondSuccess, secondOutput, secondErrors) = await h.BuildAsync(
			restore: false,
			verbose: true,
			cancellationToken
		);

		await Assert.That(secondSuccess).IsTrue().Because(TestHelpers.GenerateError(secondOutput, secondErrors));
		await Assert.That(secondOutput + secondErrors).DoesNotContain("carries no Purview.BuildSdk version marker");
	}

	/// <summary>
	/// Taking ownership of the file is the supported outcome, so the property that says so has to
	/// silence the advice as well as the refresh.
	/// </summary>
	[Test]
	public async Task EditorConfigBootstrap_AutoUpdateDisabled_SuppressesTheAdoptionWarning(
		CancellationToken cancellationToken
	)
	{
		using var h = await CreateHarnessAsync(
			extraProps: """
			<PurviewEditorConfigAdoptionWarningInCI>true</PurviewEditorConfigAdoptionWarningInCI>
			<PurviewEditorConfigAutoUpdate>false</PurviewEditorConfigAutoUpdate>
			""",
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, ".editorconfig"),
			"root = true\n",
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("carries no Purview.BuildSdk version marker");
	}

	/// <summary>
	/// The documented opt-out has to actually pin the file, marker and all.
	/// </summary>
	[Test]
	public async Task EditorConfigBootstrap_AutoUpdateDisabled_KeepsTheStaleFile(CancellationToken cancellationToken)
	{
		using var h = await CreateHarnessAsync(
			extraProps: "<PurviewEditorConfigAutoUpdate>false</PurviewEditorConfigAutoUpdate>",
			cancellationToken: cancellationToken
		);

		var editorConfigPath = Path.Combine(h.SolutionDirectory, ".editorconfig");
		const string stale = "# Purview.BuildSdk 0.0.1 - managed file, do not edit\nroot = true\n";

		await File.WriteAllTextAsync(editorConfigPath, stale, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(await File.ReadAllTextAsync(editorConfigPath, cancellationToken)).IsEqualTo(stale);
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
		// The harness only declares SourceLink centrally, so the automatically injected
		// telemetry package references are removed like the other harness-based tests do.
		var harness = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: extraProps,
			extraItems: """
			<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
			<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
			""",
			// These are the tests that cover the bootstrap, so the solution has to start without the files
			// it creates. Every other harness test seeds them, to keep the bootstrap's inline task out of
			// the build.
			seedRepositoryBootstrapFiles: false,
			cancellationToken: cancellationToken
		);

		// An AGENTS.md at the repository root is one of the repository-root markers, so the
		// bootstrap destinations resolve to the solution folder without the tests needing git.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Agent guidance\n",
			cancellationToken
		);

		return harness;
	}
}
