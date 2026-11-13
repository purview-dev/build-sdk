using System.IO.Compression;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies where <c>PurviewAutoSdkPack</c> puts each file under a packable project's <c>Sdk/</c> folder,
/// and that a file <em>linked</em> beneath <c>Sdk/</c> lands exactly where a physical file at the same
/// path would.
/// </summary>
/// <remarks>
/// The two paths are implemented separately - physical files by static <c>None</c> items in
/// <c>Sdk.props</c>, linked files by the <c>IncludeLinkedSdkFiles</c> target - so they can disagree. They
/// did: a linked file had its leading <c>Sdk/</c> stripped unconditionally, so <c>Sdk/Props/X.props</c>
/// packed as <c>Props/X.props</c> while a physical file at that path packed as <c>Sdk/Props/X.props</c>.
/// One pack exercises both, side by side, so a rule that changes for one and not the other fails here.
/// </remarks>
sealed class SdkFolderPackingTests
{
	/// <summary>
	/// Each <c>Sdk/</c>-relative path, and where it must land in the package. The rules are the
	/// <c>PurviewAutoSdkPack</c> table: the four MSBuild folders and <c>.github/</c> move to the package
	/// root, so do top-level documents and images, and everything else - nested documents included -
	/// stays under <c>Sdk/</c>.
	/// </summary>
	static readonly (string SdkRelativePath, string PackagePath)[] Layout =
	[
		("build/{0}.props", "build/{0}.props"),
		("buildTransitive/{0}.targets", "buildTransitive/{0}.targets"),
		("buildMultiTargeting/{0}.props", "buildMultiTargeting/{0}.props"),
		(".github/{0}.yml", ".github/{0}.yml"),
		("{0}.md", "{0}.md"),
		("{0}.svg", "{0}.svg"),
		("Props/{0}.props", "Sdk/Props/{0}.props"),
		("docs/{0}.md", "Sdk/docs/{0}.md"),
		("{0}.json", "Sdk/{0}.json"),
	];

	/// <summary>Fills the <c>{0}</c> placeholder in a layout path.</summary>
	static string Named(string template, string name) => template.Replace("{0}", name, StringComparison.Ordinal);

	[Test]
	public async Task Pack_GivenPhysicalAndLinkedSdkFiles_PacksBothToTheSamePaths(CancellationToken cancellationToken)
	{
		// Arrange - every path in the layout twice: once as a real file under the project's Sdk/ folder,
		// and once as a file outside the project linked to the same Sdk/ path under another name.
		var repoRoot = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));
		var linkedSourceDirectory = Path.Combine(repoRoot, "shared");
		Directory.CreateDirectory(linkedSourceDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(repoRoot, "package.json"),
			/*lang=json,strict*/"""{"name": "test-repo", "version": "0.0.0-test"}""",
			cancellationToken
		);

		var links = string.Join(
			Environment.NewLine,
			Layout.Select(
				(entry, index) =>
				{
					var source = Path.Combine(
						linkedSourceDirectory,
						$"linked-{index}{Path.GetExtension(entry.SdkRelativePath)}"
					);
					File.WriteAllText(source, "linked");
					return $"""<None Include="{source}" Link="Sdk/{Named(entry.SdkRelativePath, "Linked")}" />""";
				}
			)
		);

		using var harness = await ProjectHarness
			.For("Kit")
			.WithSolutionDirectory(Path.Combine(repoRoot, "src"))
			.WithProjectFileContent(
				$"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<IsPackable>true</IsPackable>
						<!-- Not what is under test, and the harness's Directory.Packages.props has no version for them. -->
						<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
					</PropertyGroup>
					<ItemGroup>
						{links}
					</ItemGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		foreach (var (sdkRelativePath, _) in Layout)
		{
			var physical = Path.Combine(
				harness.ProjectDirectory,
				"Sdk",
				Named(sdkRelativePath, "Physical").Replace('/', Path.DirectorySeparatorChar)
			);
			Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
			await File.WriteAllTextAsync(physical, "physical", cancellationToken);
		}

		var packageDirectory = Path.Combine(harness.SolutionDirectory, "packages");
		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

		// Act
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			$"-restore -t:Pack -p:PackageOutputPath=\"{packageDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion} -p:RepoRoot=\"{repoRoot}\"",
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var nupkg = Directory
			.GetFiles(packageDirectory, "*.nupkg")
			.Single(path => !path.EndsWith(".snupkg", StringComparison.Ordinal));
		using var package = await ZipFile.OpenReadAsync(nupkg, cancellationToken);
		var entries = package.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);

		foreach (var (sdkRelativePath, packagePath) in Layout)
		{
			foreach (var kind in (string[])["Physical", "Linked"])
			{
				var expected = Named(packagePath, kind);
				await Assert
					.That(entries.Contains(expected))
					.IsTrue()
					.Because(
						$"{kind} Sdk/{Named(sdkRelativePath, kind)} must pack to {expected}; "
							+ $"the package holds: {string.Join(", ", entries.Order(StringComparer.Ordinal))}"
					);
			}
		}
	}
}
