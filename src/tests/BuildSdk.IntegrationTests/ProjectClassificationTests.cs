using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies that Sdk.props correctly classifies project types from file names, Sdk attributes,
/// and the presence of a Dockerfile — all without triggering a build.
/// </summary>
sealed class ProjectClassificationTests
{
	[Test]
	public async Task CSharpProject_IsCSharpProject_True(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsCSharpProject", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	[Arguments("MyApp.UnitTests", "Unit", true)]
	[Arguments("MyApp.IntegrationTests", "Integration", true)]
	[Arguments("MyApp.AcceptanceTests", "Acceptance", true)]
	[Arguments("MyApp.SmokeTests", "Smoke", true)]
	[Arguments("MyApp.PerformanceTests", "Performance", true)]
	[Arguments("MyApp.FunctionalTest", "Functional", true)] // singular "Test"
	[Arguments("MyApp", "", false)]
	[Arguments("MyApp.Core", "", false)]
	public async Task TestProject_Detection_ByNamingConvention(
		string projectName,
		string expectedTestingType,
		bool expectedIsTest,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(projectName, cancellationToken: cancellationToken);
		var props = await h.GetPropertiesAsync(cancellationToken, "IsTestProject", "TestingType");

		await Assert.That(props["IsTestProject"]).IsEqualTo(expectedIsTest ? "true" : "false");
		await Assert.That(props["TestingType"]).IsEqualTo(expectedTestingType);
	}

	[Test]
	[Arguments("SharedTestingFramework", true)]
	[Arguments("SharedTestingInfrastructure", true)]
	[Arguments("SharedTestingInfra", true)]
	[Arguments("SharedTestingUtilities", true)]
	[Arguments("SharedTestingLibrary", true)]
	[Arguments("SharedTestingLib", true)]
	[Arguments("SharedTestingHelpers", true)]
	[Arguments("MyRegularLib", false)]
	public async Task SharedTestingProject_Detection_ByWellKnownNames(
		string projectName,
		bool expectedIsShared,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(projectName, cancellationToken: cancellationToken);
		await Assert
			.That(await h.GetPropertyAsync("IsSharedTestingProject", cancellationToken))
			.IsEqualTo(expectedIsShared ? "true" : "false");
	}

	[Test]
	public async Task ContainerProject_DetectedByDockerfile(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyService",
			withDockerfile: true,
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("IsContainerProject", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task ContainerProject_NotDetected_WithoutDockerfile(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyService", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsContainerProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task WebSdkProject_DetectedBySdkAttribute(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyWebApp",
			sdk: "Microsoft.NET.Sdk.Web",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(
			cancellationToken,
			"IsSdkProject",
			"IsWebSdkProject",
			"IsWorkerSdkProject"
		);

		await Assert.That(props["IsSdkProject"]).IsEqualTo("true");
		await Assert.That(props["IsWebSdkProject"]).IsEqualTo("true");
		await Assert.That(props["IsWorkerSdkProject"]).IsEqualTo("false");
	}

	[Test]
	public async Task WorkerSdkProject_DetectedBySdkAttribute(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyWorker",
			sdk: "Microsoft.NET.Sdk.Worker",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(
			cancellationToken,
			"IsSdkProject",
			"IsWebSdkProject",
			"IsWorkerSdkProject"
		);

		await Assert.That(props["IsSdkProject"]).IsEqualTo("true");
		await Assert.That(props["IsWebSdkProject"]).IsEqualTo("false");
		await Assert.That(props["IsWorkerSdkProject"]).IsEqualTo("true");
	}

	/// <summary>
	/// The SDK reads the raw .csproj file content with a regex to detect the Sdk attribute.
	/// For an Aspire host project that uses a bare &lt;Project&gt; (no Sdk attribute, so that
	/// the Aspire SDK is not accidentally loaded in tests), we can detect IsAspireHostProject
	/// by embedding the marker in a comment with two whitespace chars before &lt;Project.
	/// The project must also explicitly import Sdk.props since bare projects don't get
	/// Directory.Build.props auto-imported by MSBuild.
	/// </summary>
	[Test]
	public async Task AspireHostProject_DetectedByFileContentMarker(CancellationToken cancellationToken)
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
		await Assert.That(await h.GetPropertyAsync("IsAspireHostProject", cancellationToken)).IsEqualTo("true");
	}

	/// <summary>
	/// Aspire 17 replaced the <c>Aspire.AppHost.Sdk</c> MSBuild SDK with <c>Microsoft.NET.Sdk</c> plus an
	/// <c>Aspire.Hosting.AppHost</c> package reference. The package's own <c>IsAspireHost</c> property is
	/// imported after this SDK (NuGet package props follow <c>Directory.Build.props</c>), so the project
	/// file is scanned for the reference instead.
	/// </summary>
	[Test]
	[Arguments("""<PackageReference Include="Aspire.Hosting.AppHost" Version="17.0.0" />""")]
	[Arguments("""<PackageReference Include="Aspire.Hosting.AppHost" />""")]
	[Arguments("""<PackageReference Version="17.0.0" Include="Aspire.Hosting.AppHost" />""")]
	public async Task AspireHostProject_DetectedByAppHostPackageReference(
		string packageReference,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Acme.AppHost",
			extraItems: packageReference,
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(
			cancellationToken,
			"IsAspireHostProject",
			"OutputType",
			"NoWarn",
			"PurviewPolicyExemptNoWarn"
		);

		await Assert.That(props["IsAspireHostProject"]).IsEqualTo("true");
		await Assert.That(props["OutputType"]).IsEqualTo("Exe");
		await Assert.That(props["NoWarn"]).Contains("CA1515");
		await Assert.That(props["PurviewPolicyExemptNoWarn"]).Contains("CA1515");
	}

	[Test]
	[Arguments("""<PackageReference Include="Aspire.Hosting" Version="17.0.0" />""")]
	[Arguments("""<PackageReference Include="Aspire.Hosting.Azure.Storage" Version="17.0.0" />""")]
	public async Task AspireHostProject_NotDetectedForHostingIntegrationOnly(
		string packageReference,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Acme.AppHost",
			extraItems: packageReference,
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsAspireHostProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task AspireHostProject_NotDetectedWhenAppHostPackageReferenceIsCommentedOut(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Acme.AppHost",
			extraItems: """<!-- <PackageReference Include="Aspire.Hosting.AppHost" Version="17.0.0" /> -->""",
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsAspireHostProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task AspireHostProject_ExplicitOptInBeforeSdkImportGetsHostDefaults(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Acme.AppHost",
			preImportProps: "<IsAspireHostProject>true</IsAspireHostProject>",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "IsAspireHostProject", "OutputType");

		await Assert.That(props["IsAspireHostProject"]).IsEqualTo("true");
		await Assert.That(props["OutputType"]).IsEqualTo("Exe");
	}

	[Test]
	public async Task RegularProject_IsNotAspireHostProject(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("Acme.AppHost", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsAspireHostProject", cancellationToken)).IsEqualTo("false");
	}
}
