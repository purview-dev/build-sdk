using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins the Blazor WebAssembly, Razor class library, Blazor host and BFF detection signals, each opt-out,
/// and that a project which is none of them is left exactly as it was.
/// </summary>
sealed class BlazorProjectDetectionTests
{
	[Test]
	public async Task BlazorWebAssemblySdk_IsBlazorWebAssemblyProject(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(
			cancellationToken,
			"IsBlazorWebAssemblyProject",
			"IsRazorClassLibraryProject",
			"IsWebSdkProject",
			"IsBffProject",
			"IsBlazorHostProject"
		);

		await Assert.That(properties["IsBlazorWebAssemblyProject"]).IsEqualTo("true");
		await Assert.That(properties["IsRazorClassLibraryProject"]).IsEqualTo("false");
		await Assert.That(properties["IsWebSdkProject"]).IsEqualTo("false");
		await Assert.That(properties["IsBffProject"]).IsEqualTo("false");
		await Assert.That(properties["IsBlazorHostProject"]).IsEqualTo("false");
	}

	[Test]
	public async Task RazorSdk_IsRazorClassLibraryProject(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Components",
			BlazorFixtures
				.Library()
				.Replace("Microsoft.NET.Sdk\"", "Microsoft.NET.Sdk.Razor\"", StringComparison.Ordinal),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(
			cancellationToken,
			"IsRazorClassLibraryProject",
			"IsBlazorWebAssemblyProject"
		);

		await Assert.That(properties["IsRazorClassLibraryProject"]).IsEqualTo("true");
		await Assert.That(properties["IsBlazorWebAssemblyProject"]).IsEqualTo("false");
	}

	[Test]
	[Arguments("Microsoft.NET.Sdk", "Shop.Orders")]
	[Arguments("Microsoft.NET.Sdk.Web", "Shop.Api")]
	[Arguments("Microsoft.NET.Sdk.Worker", "Shop.Worker")]
	public async Task OtherProjects_AreNeitherBlazorNorBff(
		string sdk,
		string projectName,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.For(projectName).WithSdk(sdk).BuildAsync(cancellationToken);

		var properties = await h.GetPropertiesAsync(
			cancellationToken,
			"IsBlazorWebAssemblyProject",
			"IsRazorClassLibraryProject",
			"IsBlazorHostProject",
			"IsBffProject",
			"IsAotCompatible",
			"WarningsAsErrors"
		);

		await Assert.That(properties["IsBlazorWebAssemblyProject"]).IsEqualTo("false");
		await Assert.That(properties["IsRazorClassLibraryProject"]).IsEqualTo("false");
		await Assert.That(properties["IsBlazorHostProject"]).IsEqualTo("false");
		await Assert.That(properties["IsBffProject"]).IsEqualTo("false");
		await Assert.That(properties["IsAotCompatible"]).IsEmpty();
		await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("IL2026");
	}

	[Test]
	[Arguments("Shop.Bff")]
	[Arguments("ShopBff")]
	[Arguments("Bff")]
	public async Task WebProject_NamedWithBffSuffix_IsBffProject(
		string projectName,
		CancellationToken cancellationToken
	)
	{
		using var h = await BlazorFixtures.CreateAsync(
			projectName,
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsBffProject", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	[Arguments("Shop.BFF")]
	[Arguments("Shop.bff")]
	[Arguments("Shop.BffTools")]
	public async Task WebProject_WithoutExactBffSuffix_IsNotBffProject(
		string projectName,
		CancellationToken cancellationToken
	)
	{
		// Matched the way IsCLIProject matches 'CLI': case-sensitive, and only as a suffix.
		using var h = await BlazorFixtures.CreateAsync(
			projectName,
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsBffProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task LibraryProject_NamedWithBffSuffix_IsNotBffProject(CancellationToken cancellationToken)
	{
		// A BFF is a web host; a library that happens to end in Bff is not one.
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsBffProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task IsBffProjectFalse_OptsANamedBffOut(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Web("<IsBffProject>false</IsBffProject>"),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(cancellationToken, "IsBffProject", "InterceptorsNamespaces");

		await Assert.That(properties["IsBffProject"]).IsEqualTo("false");
		// No longer a BFF, so it is an ordinary web project again and keeps the OpenAPI interceptors.
		await Assert.That(properties["InterceptorsNamespaces"]).Contains("Microsoft.AspNetCore.OpenApi.Generated");
	}

	[Test]
	public async Task IsBffProjectTrue_OptsAWebProjectIn(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Gateway",
			BlazorFixtures.Web("<IsBffProject>true</IsBffProject>"),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(cancellationToken, "IsBffProject", "InterceptorsNamespaces");

		await Assert.That(properties["IsBffProject"]).IsEqualTo("true");
		await Assert
			.That(properties["InterceptorsNamespaces"])
			.DoesNotContain("Microsoft.AspNetCore.OpenApi.Generated");
	}

	[Test]
	public async Task IsBffProjectTrue_SetBeforeTheSdkImport_IsHonoured(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Shop.Gateway")
			.WithNamespacePrefix("Acme")
			.WithPreImportProperty("IsBffProject", "true")
			.WithProjectFileContent(BlazorFixtures.Web())
			.BuildAsync(cancellationToken);

		await Assert.That(await h.GetPropertyAsync("IsBffProject", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task Bff_WithoutAnApiOfItsOwn_HasNoOpenApiInterceptors(CancellationToken cancellationToken)
	{
		// A BFF that only forwards has no OpenAPI document to generate.
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(
			cancellationToken,
			"InterceptorsNamespaces",
			"PurviewBffExposesApi"
		);

		await Assert
			.That(properties["InterceptorsNamespaces"])
			.DoesNotContain("Microsoft.AspNetCore.OpenApi.Generated");
		await Assert.That(properties["PurviewBffExposesApi"]).IsEqualTo("false");
	}

	[Test]
	public async Task Bff_ReferencingOpenApi_KeepsOpenApiInterceptors(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Web(items: """<PackageReference Include="Microsoft.AspNetCore.OpenApi" />"""),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(
			cancellationToken,
			"InterceptorsNamespaces",
			"PurviewBffExposesApi"
		);

		await Assert.That(properties["InterceptorsNamespaces"]).Contains("Microsoft.AspNetCore.OpenApi.Generated");
		await Assert.That(properties["PurviewBffExposesApi"]).IsEqualTo("true");
	}

	[Test]
	public async Task Bff_PurviewBffExposesApiTrue_KeepsOpenApiInterceptors(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Web("<PurviewBffExposesApi>true</PurviewBffExposesApi>"),
			cancellationToken: cancellationToken
		);

		await Assert
			.That(await h.GetPropertyAsync("InterceptorsNamespaces", cancellationToken))
			.Contains("Microsoft.AspNetCore.OpenApi.Generated");
	}

	[Test]
	public async Task NonBffWebProject_KeepsOpenApiInterceptors(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Api",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		await Assert
			.That(await h.GetPropertyAsync("InterceptorsNamespaces", cancellationToken))
			.Contains("Microsoft.AspNetCore.OpenApi.Generated");
	}

	[Test]
	public async Task Bff_AddsNoPackages(CancellationToken cancellationToken)
	{
		// YARP, OpenID Connect and any BFF library (Duende.BFF is commercially licensed) are the application's
		// choice. A BFF gets exactly the packages any other web project gets.
		using var bff = await BlazorFixtures.CreateAsync(
			"Shop.Bff",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);
		using var api = await BlazorFixtures.AddSiblingAsync(
			bff,
			"Shop.Api",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		var bffPackages = await bff.GetItemIdentitiesAsync("PackageReference", cancellationToken);
		var apiPackages = await api.GetItemIdentitiesAsync("PackageReference", cancellationToken);

		await Assert.That(bffPackages).IsEquivalentTo(apiPackages);
	}

	[Test]
	public async Task Host_PairedWithClient_IsBlazorHostProject_AndReferencesIt(CancellationToken cancellationToken)
	{
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		var evaluation = await host.EvaluateAsync(
			["IsBlazorHostProject", "IsBffProject"],
			["ProjectReference"],
			cancellationToken
		);

		await Assert.That(evaluation.Properties["IsBlazorHostProject"]).IsEqualTo("true");
		// A host that keeps no tokens is not a BFF, so being a host must not make it one.
		await Assert.That(evaluation.Properties["IsBffProject"]).IsEqualTo("false");
		await Assert
			.That(evaluation.Items["ProjectReference"].Select(TestHelpers.NormalizePath))
			.IsEquivalentTo(["../Shop.Client/Shop.Client.csproj"]);
	}

	[Test]
	[Arguments("../Shop.Client/Shop.Client.csproj")]
	[Arguments(@"..\Shop.Client\Shop.Client.csproj")]
	public async Task Host_AlreadyReferencingItsClient_GetsNoSecondReference(
		string declaredReference,
		CancellationToken cancellationToken
	)
	{
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop",
			BlazorFixtures.Web(items: $"""<ProjectReference Include="{declaredReference}" />"""),
			cancellationToken: cancellationToken
		);

		var references = await host.GetProjectReferencesAsync(cancellationToken);

		// Normalised because MSBuild on Linux and macOS rewrites the backslashes in an item spec, so the
		// second case reads back with forward slashes there. What matters is that there is only one.
		await Assert
			.That(references.Select(TestHelpers.NormalizePath))
			.IsEquivalentTo([TestHelpers.NormalizePath(declaredReference)]);
	}

	[Test]
	public async Task Host_PurviewAutoBlazorClientReferenceFalse_AddsNoReference(CancellationToken cancellationToken)
	{
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop",
			BlazorFixtures.Web("<PurviewAutoBlazorClientReference>false</PurviewAutoBlazorClientReference>"),
			cancellationToken: cancellationToken
		);

		await Assert.That(await host.GetProjectReferencesAsync(cancellationToken)).IsEmpty();
	}

	[Test]
	public async Task Host_IsBlazorHostProjectFalse_IsHonoured(CancellationToken cancellationToken)
	{
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop",
			BlazorFixtures.Web("<IsBlazorHostProject>false</IsBlazorHostProject>"),
			cancellationToken: cancellationToken
		);

		await Assert.That(await host.GetPropertyAsync("IsBlazorHostProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task Host_SiblingNamedClientThatIsNotWebAssembly_IsNotPaired(CancellationToken cancellationToken)
	{
		using var library = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			library,
			"Shop",
			BlazorFixtures.Web(),
			cancellationToken: cancellationToken
		);

		var evaluation = await host.EvaluateAsync(["IsBlazorHostProject"], ["ProjectReference"], cancellationToken);

		await Assert.That(evaluation.Properties["IsBlazorHostProject"]).IsEqualTo("false");
		await Assert.That(evaluation.Items["ProjectReference"]).IsEmpty();
	}

	[Test]
	public async Task Host_ReferencingAClientUnderAnotherName_IsResolvedByTheBuild(CancellationToken cancellationToken)
	{
		// Not the <Name>/<Name>.Client pairing, so evaluation cannot know; the build asks the reference.
		using var client = await BlazorFixtures.CreateAsync(
			"Storefront",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var host = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop",
			BlazorFixtures.Web(items: """<ProjectReference Include="../Storefront/Storefront.csproj" />"""),
			cancellationToken: cancellationToken
		);

		await Assert.That(await host.GetPropertyAsync("IsBlazorHostProject", cancellationToken)).IsEqualTo("false");

		var (code, stdOut, stdErr) = await host.RunMSBuildAsync(
			"-noconlog -t:ResolvePurviewBlazorHostProject -getProperty:IsBlazorHostProject",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut.Trim()).IsEqualTo("true");
	}

	[Test]
	public async Task Client_IsNotPackable_AndHasNoContainerDefaults(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness
			.For("Shop.Client")
			.WithNamespacePrefix("Acme")
			.WithProjectFileContent(BlazorFixtures.Client())
			.WithDockerfile()
			.BuildAsync(cancellationToken);

		var evaluation = await h.EvaluateAsync(
			["IsPackable", "IsContainerProject", "PublishAot", "DockerDefaultTargetOS"],
			["PackageReference"],
			cancellationToken
		);

		await Assert.That(evaluation.Properties["IsPackable"]).IsEqualTo("false");
		await Assert.That(evaluation.Properties["IsContainerProject"]).IsEqualTo("false");
		// PublishAot on a WebAssembly client is a publish that fails; the container group must not apply.
		await Assert.That(evaluation.Properties["PublishAot"]).IsNotEqualTo("true");
		await Assert.That(evaluation.Properties["DockerDefaultTargetOS"]).IsNotEqualTo("Linux");
		await Assert
			.That(evaluation.Items["PackageReference"])
			.DoesNotContain("Microsoft.VisualStudio.Azure.Containers.Tools.Targets");
	}

	[Test]
	public async Task Client_IsPackableTrueInTheProject_StillWins(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client("<IsPackable>true</IsPackable>"),
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("IsPackable", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task Client_LeavesTheBlazorSdkSettingsAlone(CancellationToken cancellationToken)
	{
		// Globalisation, trimming mode and the runtime settings belong to the Blazor SDK. A second default here
		// would compete with it, so every one of them must read exactly what the Blazor SDK alone produces.
		string[] blazorOwned =
		[
			"InvariantGlobalization",
			"BlazorWebAssemblyLoadAllGlobalizationData",
			"TrimMode",
			"PublishTrimmed",
			"RunAOTCompilation",
		];

		using var withSdk = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		var actual = await withSdk.GetPropertiesAsync(cancellationToken, blazorOwned);

		var bareDirectory = Path.Combine(withSdk.SolutionDirectory, "bare");
		Directory.CreateDirectory(bareDirectory);
		var bareProject = Path.Combine(bareDirectory, "Bare.csproj");
		await File.WriteAllTextAsync(
			Path.Combine(bareDirectory, "Directory.Build.props"),
			"<Project />",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(bareDirectory, "Directory.Build.targets"),
			"<Project />",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			bareProject,
			"""
			<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
				</PropertyGroup>
			</Project>
			""",
			cancellationToken
		);

		var (code, stdOut, stdErr) = await withSdk.RunProcessAsync(
			"dotnet",
			$"msbuild \"{bareProject}\" -nologo -noconlog -getProperty:{string.Join(",", blazorOwned)}",
			bareDirectory,
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		foreach (var property in blazorOwned)
		{
			await Assert.That(stdOut).Contains($"\"{property}\": \"{actual[property]}\"").Because(property);
		}
	}

	[Test]
	public async Task Client_DoesNotAutoReferenceShared(CancellationToken cancellationToken)
	{
		// Every assembly a client references is downloaded to the browser; a server-side Shared library must
		// not join that download because of a folder name.
		using var shared = await BlazorFixtures.CreateAsync(
			"Shared",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await BlazorFixtures.AddSiblingAsync(
			shared,
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var library = await BlazorFixtures.AddSiblingAsync(
			shared,
			"Shop.Orders",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);

		await Assert.That(await client.GetProjectReferencesAsync(cancellationToken)).IsEmpty();
		await Assert
			.That((await library.GetProjectReferencesAsync(cancellationToken)).Select(TestHelpers.NormalizePath))
			.Contains("../Shared/Shared.csproj");
	}

	[Test]
	public async Task Client_DisablePurviewBlazorWebAssemblyDefaults_RestoresTheGenericDefaults(
		CancellationToken cancellationToken
	)
	{
		using var shared = await BlazorFixtures.CreateAsync(
			"Shared",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await ProjectHarness
			.For("Shop.Client")
			.WithNamespacePrefix("Acme")
			.WithProjectFileContent(BlazorFixtures.Client())
			.WithDockerfile()
			.WithSolutionDirectory(shared.SolutionDirectory)
			.BuildAsync(cancellationToken);

		// The opt-out is read where the defaults are, before the project body, so it is set as a global
		// property here - the same position Directory.Build.props would give it.
		var (code, stdOut, stdErr) = await client.RunMSBuildAsync(
			"-noconlog -p:DisablePurviewBlazorWebAssemblyDefaults=true -getProperty:IsContainerProject -getItem:ProjectReference",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut).Contains("\"IsContainerProject\": \"true\"");
		await Assert.That(stdOut).Contains("Shared.csproj");
	}
}
