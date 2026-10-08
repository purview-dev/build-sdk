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

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");
		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net10.0");
		// The list is only honoured while TargetFramework stays empty; see
		// ExplicitTargetFrameworks_DoesNotAlsoGetTheSingleTargetFrameworkFloor.
		await Assert.That(properties["TargetFramework"]).IsEmpty();
	}

	/// <summary>
	/// A project declaring <c>TargetFrameworks</c> in its body opts out of the curated set resolution,
	/// which left the single-TFM floor further down Sdk.props to fire: at that point the body has not been
	/// evaluated, so both TFM properties still read empty. The project then ended up with
	/// <c>TargetFramework</c> set to the Current set *and* its own <c>TargetFrameworks</c> list, and
	/// MSBuild honours the singular one — so the project silently built a single TFM and any multi-targeting
	/// consumer failed with "Project 'X' targets 'net10.0'. It cannot be referenced by a project that
	/// targets '.NETCoreApp,Version=v9.0'". The floor must therefore stand down for the same declaration
	/// the set resolution stands down for.
	/// </summary>
	[Test]
	public async Task ExplicitTargetFrameworks_DoesNotAlsoGetTheSingleTargetFrameworkFloor(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");

		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net8.0;net9.0;net10.0");
		await Assert.That(properties["TargetFramework"]).IsEmpty();
	}

	/// <summary>
	/// The shape that surfaced the defect: a repository selects a set repository-wide and a project spells
	/// the same list out through the set's property. Redundant, but legal, and it must still multi-target.
	/// </summary>
	[Test]
	public async Task ExplicitTargetFrameworksFromASetProperty_StillMultiTargets(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithPreImportProperty("PurviewTargetFrameworkSet", "All")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFrameworks>$(PurviewTargetFrameworksAll)</TargetFrameworks>
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
	/// The declaration the floor stands down for is read from the project text, so it is seen in an inner
	/// build too. There, <c>TargetFramework</c> arrives as a global property and must survive untouched
	/// while <c>TargetFrameworks</c> reports the same list as the outer evaluation.
	/// </summary>
	[Test]
	public async Task ExplicitTargetFrameworks_ResolvesIdenticallyUnderAnInnerBuildTargetFramework(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var globals = new Dictionary<string, string>(StringComparer.Ordinal) { ["TargetFramework"] = "net9.0" };

		var properties = await h.GetPropertiesAsync(
			globals,
			cancellationToken,
			"TargetFramework",
			"TargetFrameworks",
			"TargetPath"
		);

		await Assert.That(properties["TargetFramework"]).IsEqualTo("net9.0");
		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net8.0;net9.0;net10.0");
		await Assert.That(properties["TargetPath"]).IsNotEmpty();
	}

	/// <summary>
	/// A project declaring a single <c>TargetFramework</c> still has to end up with exactly that one, and
	/// no <c>TargetFrameworks</c> list from the floor or from a repository-wide set.
	/// </summary>
	[Test]
	public async Task ExplicitSingleTargetFramework_IsNotWidenedOrOverridden(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithPreImportProperty("PurviewTargetFrameworkSet", "All")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFramework>net8.0</TargetFramework>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");

		await Assert.That(properties["TargetFramework"]).IsEqualTo("net8.0");
		await Assert.That(properties["TargetFrameworks"]).IsEmpty();
	}

	/// <summary>
	/// A project narrowing a repository-wide selection by naming a different set in its own body. The set
	/// is read from the project text precisely so this works despite the body being evaluated after the
	/// SDK import, and the narrower declaration is documented to win.
	/// </summary>
	[Test]
	public async Task AProjectSet_WinsOverARepositoryWideSet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Library")
			.WithPreImportProperty("PurviewTargetFrameworkSet", "All")
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

		var properties = await h.GetPropertiesAsync(cancellationToken, "TargetFramework", "TargetFrameworks");

		await Assert.That(properties["TargetFrameworks"]).IsEqualTo("net10.0;net11.0");
		await Assert.That(properties["TargetFramework"]).IsEmpty();
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
