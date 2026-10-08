using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies the curated <c>PurviewTargetFrameworkSet</c> selection: a repository names a set instead of
/// hard-coding a TFM list in every project, so a lifecycle change is one SDK bump rather than an edit in
/// every consuming repository. The feature is opt-in, and every narrower declaration still wins.
/// </summary>
sealed class TargetFrameworkSetTests
{
	[Test]
	public async Task NoSetSelected_KeepsTheSingleTargetFrameworkDefault(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		// An SDK upgrade must never change a package's targets on its own.
		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");
		await Assert.That(properties["TargetFramework"]).IsEqualTo("net10.0");
		await Assert.That(properties["TargetFrameworks"]).IsEmpty();
	}

	[Test]
	[Arguments("Supported", "net10.0;net11.0")]
	[Arguments("Broad", "net8.0;net9.0;net10.0")]
	[Arguments("All", "net8.0;net9.0;net10.0;net11.0")]
	public async Task MultiTargetSet_ResolvesToTargetFrameworks(
		string set,
		string expected,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				$"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>{set}</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");
		await Assert.That(properties["TargetFrameworks"]).IsEqualTo(expected);
	}

	[Test]
	[Arguments("Current", "net10.0")]
	[Arguments("Latest", "net11.0")]
	public async Task SingleTargetSet_ResolvesToTargetFramework(
		string set,
		string expected,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				$"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>{set}</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		// A one-entry set must not pay for an outer multi-targeting build.
		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");
		await Assert.That(properties["TargetFramework"]).IsEqualTo(expected);
		await Assert.That(properties["TargetFrameworks"]).IsEmpty();
	}

	[Test]
	public async Task ExplicitTargetFrameworks_WinsOverASelectedSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>Broad</PurviewTargetFrameworkSet>
						<TargetFrameworks>net10.0</TargetFrameworks>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFrameworks");
		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net10.0");
	}

	[Test]
	public async Task ARepositoryCanRedefineASet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			// Set before the SDK is imported, which is how a repository pins a set while it is not
			// ready to follow the SDK's definition.
			.WithPreImportProperty("PurviewTargetFrameworksSupported", "net9.0;net10.0")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>Supported</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFrameworks");
		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net9.0;net10.0");
	}

	[Test]
	public async Task RoslynComponent_KeepsNetStandardAndWarns(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("SourceGeneration")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<IsRoslynComponent>true</IsRoslynComponent>
						<PurviewTargetFrameworkSet>Supported</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		// A component must load in every compiler host, so netstandard2.0 wins over any set.
		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");
		await Assert.That(properties["TargetFramework"]).IsEqualTo("netstandard2.0");
		await Assert.That(properties["TargetFrameworks"]).IsEmpty();

		var (_, stdOut, _) = await h.RunMSBuildAsync("-t:Build", cancellationToken);
		await Assert.That(stdOut).Contains("PurviewTargetFrameworkSetIgnored");
	}

	/// <summary>
	/// Selecting a set repository-wide in Directory.Build.props is the normal arrangement, and every
	/// project inherits it - including any Roslyn components. Warning about that would fire on every
	/// well-configured repository, so only a component that declares a set itself is told.
	/// </summary>
	[Test]
	public async Task RoslynComponent_InheritingARepositoryWideSet_DoesNotWarn(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("SourceGeneration")
			.WithPreImportProperty("PurviewTargetFrameworkSet", "All")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<IsRoslynComponent>true</IsRoslynComponent>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework");
		await Assert.That(properties["TargetFramework"]).IsEqualTo("netstandard2.0");

		var (_, stdOut, stdErr) = await h.RunMSBuildAsync("-t:Build", cancellationToken);
		await Assert.That(stdOut + stdErr).DoesNotContain("PurviewTargetFrameworkSetIgnored");
	}

	[Test]
	public async Task UnrecognisedSet_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>LTS</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		// A typo must not quietly fall through to the single-TFM default and drop every target asked for.
		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-t:Build", cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).Contains("PurviewInvalidTargetFrameworkSet");
	}

	/// <summary>
	/// TargetFrameworks has to evaluate to the same list whatever TargetFramework is, because an inner
	/// build receives TargetFramework as a global property. Visual Studio's project system evaluates a
	/// project once with no TargetFramework to discover its TFM dimensions and again per dimension with
	/// TargetFramework set; when the second evaluation loses TargetFrameworks, the configured project
	/// CPS builds the language-service context from has no TFM, TargetPath is never defined, and the
	/// project loads with "Property 'TargetPath' is required to be an absolute path, but the value is ''".
	/// Neither dotnet nor msbuild reproduces it - both only evaluate the outer build.
	/// </summary>
	[Test]
	[Arguments("Supported", "net10.0;net11.0", "net10.0")]
	[Arguments("All", "net8.0;net9.0;net10.0;net11.0", "net8.0")]
	public async Task MultiTargetSet_ResolvesIdenticallyUnderAnInnerBuildTargetFramework(
		string set,
		string expected,
		string innerTargetFramework,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				$"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>{set}</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var globals = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["TargetFramework"] = innerTargetFramework,
		};

		var properties = await h.GetPropertiesAsync(globals, cancellationToken, "TargetFrameworks", "TargetPath");

		await Assert.That(properties["TargetFrameworks"]).IsEqualTo(expected);
		// The reason the inconsistency is fatal rather than cosmetic: without a TFM the project never
		// reaches Microsoft.Common.CurrentVersion.targets, so it has no output path to report.
		await Assert.That(properties["TargetPath"]).IsNotEmpty();
	}

	/// <summary>
	/// The same invariant for the outer evaluation, which is the one CPS reads the TFM dimensions from.
	/// Paired with the test above, this pins both halves: the list must be identical in each.
	/// </summary>
	[Test]
	public async Task MultiTargetSet_OuterEvaluationHasNoTargetFrameworkAndNoTargetPath(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<PurviewTargetFrameworkSet>All</PurviewTargetFrameworkSet>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");

		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net8.0;net9.0;net10.0;net11.0");
		await Assert.That(properties["TargetFramework"]).IsEmpty();
	}

	/// <summary>
	/// A project declaring its own TargetFramework must not also receive a repository-wide set's list
	/// underneath it. The declaration is detected from the project text rather than from an evaluated
	/// TargetFramework, so this holds even though the project body is evaluated after the SDK import.
	/// </summary>
	[Test]
	public async Task ExplicitTargetFramework_WinsOverASelectedSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithPreImportProperty("PurviewTargetFrameworkSet", "All")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFramework>net10.0</TargetFramework>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");

		await Assert.That(properties["TargetFramework"]).IsEqualTo("net10.0");
		await Assert.That(properties["TargetFrameworks"]).IsEmpty();
	}
}
