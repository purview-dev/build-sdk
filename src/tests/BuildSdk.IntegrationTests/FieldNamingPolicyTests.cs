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
		await WriteProbeAsync(h, "int counter", "counter", cancellationToken);

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
		await WriteProbeAsync(h, "int _counter", "_counter", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("IDE1006");
	}

	/// <summary>
	/// Private static fields are type-level state and stay PascalCase, so a '_'-prefixed one is
	/// reported by the private static field rule (not the '_camelCase' instance rule).
	/// </summary>
	[Test]
	public async Task PrivateStaticReadonlyField_WithUnderscorePrefix_IsReported(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);
		await ApplyShippedStylePolicyAsync(h, cancellationToken);
		await WriteProbeAsync(h, "static readonly int _counter = 1", "_counter", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).Contains("IDE1006");
		await Assert.That(output + errors).Contains("Prefix '_' is not expected");
	}

	/// <summary>
	/// A private static field must be covered by exactly one naming rule: renaming it to PascalCase
	/// must not then trip the '_camelCase' private instance rule.
	/// </summary>
	[Test]
	public async Task PrivateStaticReadonlyField_PascalCase_IsNotReported(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);
		await ApplyShippedStylePolicyAsync(h, cancellationToken);
		await WriteProbeAsync(h, "static readonly int Counter = 1", "Counter", cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, verbose: true, cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("IDE1006");
	}

	/// <summary>
	/// Private constants are static fields too, so they belong to the private static field rule
	/// (PascalCase) rather than the StyleCop constant rule that only covers non-private fields.
	/// </summary>
	[Test]
	public async Task PrivateConstField_PascalCase_IsNotReported(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyApp",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);
		await ApplyShippedStylePolicyAsync(h, cancellationToken);
		await WriteProbeAsync(h, "const int Counter = 1", "Counter", cancellationToken);

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

	/// <summary>
	/// Writes a probe type containing <paramref name="fieldDeclaration"/> plus a member that reads
	/// <paramref name="fieldName"/>, so the naming rules see a used field of the requested shape.
	/// </summary>
	static async Task WriteProbeAsync(
		ProjectHarness harness,
		string fieldDeclaration,
		string fieldName,
		CancellationToken cancellationToken
	)
	{
		var source = $$"""
			namespace Test.MyApp;

			sealed class Probe
			{
				{{fieldDeclaration}};

				public int Read() => {{fieldName}};
			}
			""";

		await File.WriteAllTextAsync(Path.Combine(harness.ProjectDirectory, "Probe.cs"), source, cancellationToken);
	}
}
