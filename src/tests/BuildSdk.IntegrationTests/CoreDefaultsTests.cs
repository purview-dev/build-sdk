using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the C# compiler defaults injected by Sdk.props — Nullable, ImplicitUsings,
/// LangVersion, Deterministic, RootNamespace derivation, and CI flag passthrough.
/// Default-property assertions are batched into a single MSBuild evaluation per test
/// so the suite stays fast without losing any assertion.
/// </summary>
public sealed class CoreDefaultsTests
{
	[Test]
	public async Task DefaultProject_CSharpCompilerDefaults(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		var eval = await h.EvaluateAsync(
			[
				"Nullable",
				"ImplicitUsings",
				"LangVersion",
				"Deterministic",
				"ManagePackageVersionsCentrally",
				"PublishRepositoryUrl",
				"IncludeSymbols",
				"SymbolPackageFormat",
				"AnalysisLevel",
				"AnalysisMode",
				"EnableNETAnalyzers",
				"EnforceCodeStyleInBuild",
			],
			cancellationToken: cancellationToken
		);

		await Assert.That(eval.Properties["Nullable"]).IsEqualTo("enable").Because("Nullable should default to enable");
		await Assert
			.That(eval.Properties["ImplicitUsings"])
			.IsEqualTo("enable")
			.Because("ImplicitUsings should default to enable");
		await Assert
			.That(eval.Properties["LangVersion"])
			.IsEqualTo("preview")
			.Because("LangVersion should default to preview");
		await Assert
			.That(eval.Properties["Deterministic"])
			.IsEqualTo("true")
			.Because("Deterministic should default to true");
		await Assert
			.That(eval.Properties["ManagePackageVersionsCentrally"])
			.IsEqualTo("true")
			.Because("ManagePackageVersionsCentrally should default to true");
		await Assert
			.That(eval.Properties["PublishRepositoryUrl"])
			.IsEqualTo("true")
			.Because("PublishRepositoryUrl should default to true");
		await Assert
			.That(eval.Properties["IncludeSymbols"])
			.IsEqualTo("true")
			.Because("IncludeSymbols should default to true");
		await Assert
			.That(eval.Properties["SymbolPackageFormat"])
			.IsEqualTo("snupkg")
			.Because("SymbolPackageFormat should default to snupkg");
		await Assert
			.That(eval.Properties["AnalysisLevel"])
			.IsEqualTo("latest")
			.Because("AnalysisLevel should default to latest");
		await Assert
			.That(eval.Properties["AnalysisMode"])
			.IsEqualTo("All")
			.Because("AnalysisMode should default to All");
		await Assert
			.That(eval.Properties["EnableNETAnalyzers"])
			.IsEqualTo("true")
			.Because("EnableNETAnalyzers should default to true");
		await Assert
			.That(eval.Properties["EnforceCodeStyleInBuild"])
			.IsEqualTo("true")
			.Because("EnforceCodeStyleInBuild should default to true");
	}

	[Test]
	public async Task DefaultProject_EditorConfig_ResolvesSdkEditorConfigAndListsIt(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		var eval = await h.EvaluateAsync(["EditorConfigFilePath"], ["EditorConfigFiles"], cancellationToken);

		var editorConfigPath = eval.Properties["EditorConfigFilePath"];
		await Assert.That(string.IsNullOrWhiteSpace(editorConfigPath)).IsFalse();
		await Assert.That(File.Exists(editorConfigPath)).IsTrue();

		var normalizedEditorConfigPath = Path.GetFullPath(editorConfigPath).TrimEnd('\\', '/');
		var hasSdkEditorConfig = eval.Items["EditorConfigFiles"]
			.Any(path => Path.GetFullPath(path).TrimEnd('\\', '/') == normalizedEditorConfigPath);

		await Assert.That(hasSdkEditorConfig).IsTrue();
	}

	[Test]
	public async Task WarnOnPackingNonPackableProject_WhenPackableIsFalse_DefaultsToFalse(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<IsPackable>false</IsPackable>",
			cancellationToken: cancellationToken
		);
		// The SDK sets LangVersion to "preview" unless explicitly overridden.
		await Assert
			.That(await h.GetPropertyAsync("WarnOnPackingNonPackableProject", cancellationToken))
			.IsEqualTo("false");
	}

	[Test]
	public async Task RootNamespace_DerivedFromNamespacePrefixAndProjectName(CancellationToken cancellationToken)
	{
		// NamespacePrefix=Test, ProjectName=MyLibrary → Test.MyLibrary
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			namespacePrefix: "Test",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("RootNamespace", cancellationToken)).IsEqualTo("Test.MyLibrary");
	}

	[Test]
	public async Task Ci_PropertySet_WhenEnvironmentVariablePresent(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraEnv: new Dictionary<string, string> { ["CI"] = "true" },
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("ContinuousIntegrationBuild", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task Ci_PropertyNotSet_WhenEnvironmentVariableAbsent(CancellationToken cancellationToken)
	{
		// Ensure CI env var is not set for this test (it may be set in CI environments, so
		// we override with empty string to simulate a local dev machine).
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraEnv: new Dictionary<string, string> { ["CI"] = "" },
			cancellationToken: cancellationToken
		);
		var value = await h.GetPropertyAsync("ContinuousIntegrationBuild", cancellationToken);
		// ContinuousIntegrationBuild should be empty (not "true") when CI is not set.
		await Assert.That(value).IsNotEqualTo("true");
	}

	[Test]
	public async Task Ci_PropertyNotSet_WhenPackableButNotCi(CancellationToken cancellationToken)
	{
		// Packability alone must not force ContinuousIntegrationBuild: local packs must not enter
		// SourceLink's CI-mode dirty-repository checks (which fail under TreatWarningsAsErrors).
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraEnv: new Dictionary<string, string> { ["CI"] = "" },
			extraProps: "<IsPackable>true</IsPackable>",
			cancellationToken: cancellationToken
		);
		var value = await h.GetPropertyAsync("ContinuousIntegrationBuild", cancellationToken);
		await Assert
			.That(value)
			.IsNotEqualTo("true")
			.Because("Only real CI environment variables may set ContinuousIntegrationBuild.");
	}

	[Test]
	[Arguments("net9.0", "net9.0")]
	[Arguments("net10.0", "net10.0")]
	public async Task TargetFramework_Honoured_WhenExplicitlySet(
		string tfm,
		string expected,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: tfm,
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("TargetFramework", cancellationToken)).IsEqualTo(expected);
	}

	[Test]
	public async Task Nullable_CanBeOverriddenInProjectFile(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<Nullable>disable</Nullable>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("Nullable", cancellationToken)).IsEqualTo("disable");
	}

	[Test]
	public async Task Nullable_CanBeOverriddenInDirectoryBuildProps(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<Nullable>disable</Nullable>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("Nullable", cancellationToken)).IsEqualTo("disable");
	}
}
