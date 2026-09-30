using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the shipped field-naming policy: private instance fields are named '_camelCase', never
/// 'camelCase', so field access never needs the noisy 'this.' qualifier and fields stay
/// distinguishable from locals and parameters.
/// </summary>
sealed class FieldNamingPolicyTests
{
	// The harness only declares SourceLink centrally, so the automatically injected telemetry package
	// references are removed like the other harness-based tests do.
	const string RemoveTelemetryItems = """
		<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
		<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
		""";

	[Test]
	public async Task PrivateInstanceField_WithoutUnderscorePrefix_IsReported(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);
		await ApplyShippedStylePolicyAsync(h, cancellationToken);
		await WriteProbeAsync(h, "counter", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("IDE1006");
		await Assert.That(output + errors).Contains("Missing prefix: '_'");
	}

	[Test]
	public async Task PrivateInstanceField_WithUnderscorePrefix_IsNotReported(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);
		await ApplyShippedStylePolicyAsync(h, cancellationToken);
		await WriteProbeAsync(h, "_counter", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("IDE1006");
	}

	/// <summary>
	/// The SDK bootstraps a physical .editorconfig during the first build, which is later than the
	/// compiler's configuration discovery for that same build. A real repository has the file in place
	/// before it builds, so the test places the shipped file first to exercise the real rules.
	/// </summary>
	static async Task ApplyShippedStylePolicyAsync(ProjectHarness harness, CancellationToken cancellationToken)
	{
		var shippedEditorConfig = await harness.GetPropertyAsync("EditorConfigFilePath", cancellationToken);

		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, ".editorconfig"),
			await File.ReadAllTextAsync(shippedEditorConfig, cancellationToken),
			cancellationToken
		);
	}

	static async Task WriteProbeAsync(ProjectHarness harness, string fieldName, CancellationToken cancellationToken)
	{
		var source = $$"""
			namespace Test.MyApp;

			sealed class Probe
			{
				int {{fieldName}};

				public int Read() => {{fieldName}};
			}
			""";

		await File.WriteAllTextAsync(Path.Combine(harness.ProjectDirectory, "Probe.cs"), source, cancellationToken);
	}
}
