using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Test and shared-testing projects enforce the same strict structure and style as the rest of the
/// repository (modifier policy, field naming, formatting, naming rules) but not the production
/// API-surface rules: test classes and fixtures are legitimately public, test names use
/// 'Method_Scenario_Expectation', helpers expose fields, return generic lists and take fixture
/// parameters without null guards, and abstract test bases have public constructors.
/// </summary>
sealed class TestContextRuleSetTests
{
	/// <summary>The context-aware rule set injected into every test and shared-testing project.</summary>
	static readonly string[] TestContextRules =
	[
		"CA1002",
		"CA1012",
		"CA1034",
		"CA1047",
		"CA1050",
		"CA1051",
		"CA1062",
		"CA1064",
		"CA1515",
		"CA1707",
	];

	/// <summary>
	/// Idiomatic test code: public types in an executable test assembly, a visible field, a public
	/// helper returning a generic list without validating its argument, an underscore-separated test
	/// name, and a nested public support type. Every one of those violates a production rule.
	/// </summary>
	const string TestIdiomsSource = """
		namespace Test.MyApp;

		public sealed class OrderServiceTests
		{
			public string Name = "order";

			public List<string> NamesOf(IEnumerable<string> values) => [.. values];

			public void GivenTenant_WhenCreated_ExpectsIdentifier() { }

			public sealed class NestedSupport
			{
				public string Description { get; set; } = "nested";
			}

			public static void Main() { }
		}
		""";

	[Test]
	public async Task TestProject_AppliesTheTestContextRuleSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyApp.UnitTests", cancellationToken: cancellationToken);

		var props = await h.GetPropertiesAsync(cancellationToken, "NoWarn", "PurviewPolicyExemptNoWarn");

		foreach (var rule in TestContextRules)
		{
			await Assert.That(props["NoWarn"]).Contains(rule);
			await Assert.That(props["PurviewPolicyExemptNoWarn"]).Contains(rule);
		}

		// The style contract still applies: accessibility modifiers, field naming and the rest are
		// never silenced for test projects.
		await Assert.That(props["NoWarn"]).DoesNotContain("IDE0040");
		await Assert.That(props["NoWarn"]).DoesNotContain("IDE1006");
	}

	[Test]
	public async Task SharedTestingProject_AppliesTheTestContextRuleSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("SharedTestingFramework", cancellationToken: cancellationToken);

		var props = await h.GetPropertiesAsync(cancellationToken, "NoWarn", "PurviewPolicyExemptNoWarn");

		foreach (var rule in TestContextRules)
			await Assert.That(props["NoWarn"]).Contains(rule);

		await Assert.That(props["NoWarn"]).DoesNotContain("IDE0040");
	}

	[Test]
	public async Task TestProject_CanOptIntoTheProductionRuleSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp.UnitTests",
			preImportProps: "<DisablePurviewTestContextRuleSet>true</DisablePurviewTestContextRuleSet>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "NoWarn", "PurviewPolicyExemptNoWarn");

		foreach (var rule in TestContextRules)
			await Assert.That(props["NoWarn"]).DoesNotContain(rule);

		await Assert.That(props["PurviewPolicyExemptNoWarn"]).DoesNotContain("CA1707");
	}

	[Test]
	public async Task TestProject_WithTestIdioms_BuildsCleanly(CancellationToken cancellationToken)
	{
		using var h = await CreateIdiomaticTestProjectAsync(disableTestContextRuleSet: false, cancellationToken);
		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		foreach (var rule in TestContextRules)
			await Assert.That(output + errors).DoesNotContain(rule);

		// The SDK-injected rule set must not be mistaken for a repository-authored NoWarn entry.
		await Assert.That(output + errors).DoesNotContain("PRSGD0008");
	}

	[Test]
	public async Task TestProject_WithTestIdioms_ReportsViolationsWhenOptedOut(CancellationToken cancellationToken)
	{
		using var h = await CreateIdiomaticTestProjectAsync(disableTestContextRuleSet: true, cancellationToken);
		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		// Proves the exemptions above are what keep idiomatic test code buildable.
		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("CA1707");
	}

	/// <summary>
	/// A test project needs no test packages for this probe: <c>TestingFramework</c> must be cleared
	/// before the SDK import (the SDK resolves the test packages while <c>Sdk.props</c> runs), and the
	/// name suffix alone still classifies the project as a test project, so the test-context rule set
	/// applies. The explicit <c>OutputType</c> supplies the executable assembly CA1515 targets.
	/// </summary>
	static async Task<ProjectHarness> CreateIdiomaticTestProjectAsync(
		bool disableTestContextRuleSet,
		CancellationToken cancellationToken
	)
	{
		var preImportProps =
			"<TestingFramework>None</TestingFramework><SubstituteFramework>None</SubstituteFramework>"
			+ "<TestDataFramework>None</TestDataFramework><OutputType>Exe</OutputType>"
			+ "<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>"
			+ (
				disableTestContextRuleSet
					? "<DisablePurviewTestContextRuleSet>true</DisablePurviewTestContextRuleSet>"
					: ""
			);

		var harness = await ProjectHarness.CreateAsync(
			"MyApp.UnitTests",
			preImportProps: preImportProps,
			cancellationToken: cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(harness.ProjectDirectory, "OrderServiceTests.cs"),
			TestIdiomsSource,
			cancellationToken
		);

		// The SDK bootstraps a physical .editorconfig during the first build, which is later than the
		// compiler's configuration discovery for that same build. A real repository has the file in
		// place before it builds, so place the shipped file first to exercise the real rule severities.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, ".editorconfig"),
			await File.ReadAllTextAsync(
				await harness.GetPropertyAsync("EditorConfigFilePath", cancellationToken),
				cancellationToken
			),
			cancellationToken
		);

		return harness;
	}
}
