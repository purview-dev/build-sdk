using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Aspire hosts and CLI/console apps are executables whose nested public options types are mandated
/// by the framework (Spectre.Console command settings, Aspire resource-kit options bound by
/// [ZodSchema], ...). CA1515 is therefore exempt for those shapes, the exemption is recorded so
/// ValidatePurviewStylePolicy (PRSGD0008) can tell it apart from a repository-authored NoWarn entry,
/// and the rest of the accessibility policy stays enforced.
/// </summary>
sealed class AccessibilityPolicyExemptionTests
{
	// The harness only declares SourceLink centrally, so the automatically injected telemetry package
	// references are removed like the other harness-based build tests do.
	const string RemoveTelemetryItems = """
		<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
		<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
		""";

	/// <summary>
	/// The Aspire host is authored as a bare project (no Sdk attribute) so the Aspire SDK is not
	/// loaded in tests; the marker inside a comment still classifies the project (see
	/// <see cref="ProjectClassificationTests"/>).
	/// </summary>
	[Test]
	public async Task AspireHostProject_ExemptsCa1515_ButKeepsTheRestOfTheAccessibilityPolicy(
		CancellationToken cancellationToken
	)
	{
		var content = $"""
			<Project>
				<Import Project="{SdkPaths.SdkDirectory}/Sdk.props" />
				<PropertyGroup>
					<NamespacePrefix>Test</NamespacePrefix>
					<DisableNamespacePrefixCheck>true</DisableNamespacePrefixCheck>
					<TargetFramework>net10.0</TargetFramework>
				</PropertyGroup>
				<!--
				  <Project Sdk="Aspire.Sdk.Host" />
				-->
			</Project>
			""";

		using var h = await ProjectHarness.CreateWithContentAsync(
			"Acme.AppHost",
			content,
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "NoWarn", "PurviewPolicyExemptNoWarn");

		await Assert.That(props["NoWarn"]).Contains("CA1515");
		await Assert.That(props["PurviewPolicyExemptNoWarn"]).Contains("CA1515");

		// The modifier policy and the api-surface rules stay enforced: unlike framework-mandated
		// nested options types, they are fixable in host code.
		await Assert.That(props["NoWarn"]).DoesNotContain("CA1062");
		await Assert.That(props["NoWarn"]).DoesNotContain("CA1707");
		await Assert.That(props["NoWarn"]).DoesNotContain("IDE0040");
	}

	/// <summary>
	/// The SDK-injected exemption must not be mistaken for a repository-authored NoWarn entry:
	/// PRSGD0008 fails the build when a repository silences a policy rule itself, so a CLI project
	/// has to build cleanly even though its NoWarn contains CA1515.
	/// </summary>
	[Test]
	public async Task CliProject_WithPublicType_BuildsWithoutPrsgd0008(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyCLI",
			extraProps: "<OutputType>Exe</OutputType>",
			extraItems: RemoveTelemetryItems,
			cancellationToken: cancellationToken
		);

		const string source = """
			namespace Test.MyCLI;

			public sealed class Program
			{
				public static void Main() { }
			}
			""";

		await File.WriteAllTextAsync(Path.Combine(h.ProjectDirectory, "Program.cs"), source, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output + errors).DoesNotContain("PRSGD0008");

		// CA1515 is exempt for CLI apps, so the public Program type is not reported here either.
		await Assert.That(output + errors).DoesNotContain("CA1515");
	}
}
