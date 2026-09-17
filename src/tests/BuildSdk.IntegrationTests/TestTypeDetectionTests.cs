using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

public class TestTypeDetectionTests
{
	[Test]
	[MethodDataSource(nameof(TestTypes))]
	public async Task TestProject_TestingTypeInName_IsTestTypeAndRootNamespace(
		string testType,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			$"MyApp.{testType}Tests",
			namespacePrefix: "MyApp",
			cancellationToken: cancellationToken
		);

		var eval = await h.EvaluateAsync(["TestingType", "RootNamespace"], cancellationToken: cancellationToken);

		await Assert.That(eval.Properties["TestingType"]).IsEqualTo(testType);
		await Assert.That(eval.Properties["RootNamespace"]).IsEqualTo("MyApp");
	}

	[Test]
	[MethodDataSource(nameof(TestTypes))]
	public async Task TestProject_TestingTypeInDeepName_RootNamespace(
		string testType,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			$"MyApp.Nested.Namespaces.{testType}Tests",
			namespacePrefix: "MyApp",
			cancellationToken: cancellationToken
		);

		var eval = await h.EvaluateAsync(["TestingType", "RootNamespace"], cancellationToken: cancellationToken);

		await Assert.That(eval.Properties["TestingType"]).IsEqualTo(testType);
		await Assert.That(eval.Properties["RootNamespace"]).IsEqualTo("MyApp.Nested.Namespaces");
	}

	public static IEnumerable<Func<string>> TestTypes()
	{
		yield return () => "Accessibility";
		yield return () => "Acceptance";
		yield return () => "Architecture";
		yield return () => "BlackBox";
		yield return () => "Chaos";
		yield return () => "Contract";
		yield return () => "E2E";
		yield return () => "EndToEnd";
		yield return () => "Environment";
		yield return () => "Functional";
		yield return () => "Interactive";
		yield return () => "Integration";
		yield return () => "Load";
		yield return () => "Performance";
		yield return () => "Regression";
		yield return () => "Security";
		yield return () => "Scenario";
		yield return () => "Smoke";
		yield return () => "Stress";
		yield return () => "System";
		yield return () => "Threat";
		yield return () => "Unit";
		yield return () => "WhiteBox";
	}
}
