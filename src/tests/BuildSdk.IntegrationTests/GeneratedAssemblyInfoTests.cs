using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the auto-generated AssemblyInfo class handles property values containing
/// MSBuild item-list separators (semicolons) without producing broken C#.
/// </summary>
public sealed class GeneratedAssemblyInfoTests
{
	[Test]
	public async Task GeneratedAssemblyInfo_WithSemicolonInProperties_BuildsAndPreservesValues(
		CancellationToken cancellationToken
	)
	{
		var extraItems = """
			<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
			<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
			<PackageReference Remove="Microsoft.SourceLink.GitHub" />
			""";

		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: """
			<Company>ZodSharp Contributors;Purview-Dev Contributors</Company>
			<AssemblyTitle>Example;Title</AssemblyTitle>
			<AssemblyProduct>Example;Product</AssemblyProduct>
			<Copyright>Copyright (c) 2026; Example</Copyright>
			""",
			extraItems: extraItems,
			cancellationToken: cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var generatedFiles = Directory.GetFiles(
			h.ProjectDirectory,
			"GeneratedAssemblyInfo.cs",
			SearchOption.AllDirectories
		);
		await Assert.That(generatedFiles.Length).IsEqualTo(1);

		var content = await File.ReadAllTextAsync(generatedFiles[0], cancellationToken);

		await Assert
			.That(content)
			.Contains("public const string Company = \"ZodSharp Contributors;Purview-Dev Contributors\";");
		await Assert.That(content).Contains("public const string AssemblyTitle = \"Example;Title\";");
		await Assert.That(content).Contains("public const string AssemblyProduct = \"Example;Product\";");
		await Assert.That(content).Contains("public const string Copyright = \"Copyright (c) 2026; Example\";");
	}
}
