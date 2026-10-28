using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins the namespace and assembly identities the naming rules produce for a Blazor Web App's projects, and
/// that a client's test project resolves its target.
/// </summary>
sealed class BlazorProjectIdentityTests
{
	[Test]
	[Arguments("Shop.Client", "Microsoft.NET.Sdk.BlazorWebAssembly", "Acme.Shop.Client", "Acme.Shop.Client")]
	[Arguments("Shop.Bff", "Microsoft.NET.Sdk.Web", "Acme.Shop.Bff", "Acme.Shop.Bff")]
	[Arguments("Shop", "Microsoft.NET.Sdk.Web", "Acme.Shop", "Acme.Shop")]
	// ClientShared is a stripped segment, so the namespace collapses onto the product while the assembly
	// keeps the full logical name and stays distinct from the host's.
	[Arguments("Shop.ClientShared", "Microsoft.NET.Sdk", "Acme.Shop", "Acme.Shop.ClientShared")]
	[Arguments("ClientShared", "Microsoft.NET.Sdk", "Acme", "Acme.ClientShared")]
	public async Task BlazorWebAppProjects_HaveSensibleIdentities(
		string projectName,
		string sdk,
		string expectedRootNamespace,
		string expectedAssemblyName,
		CancellationToken cancellationToken
	)
	{
		var content = BlazorFixtures
			.Library()
			.Replace("Sdk=\"Microsoft.NET.Sdk\"", $"Sdk=\"{sdk}\"", StringComparison.Ordinal);
		using var h = await BlazorFixtures.CreateAsync(projectName, content, cancellationToken: cancellationToken);

		var properties = await h.GetPropertiesAsync(cancellationToken, "RootNamespace", "AssemblyName", "PackageId");

		await Assert.That(properties["RootNamespace"]).IsEqualTo(expectedRootNamespace);
		await Assert.That(properties["AssemblyName"]).IsEqualTo(expectedAssemblyName);
		await Assert.That(properties["PackageId"]).IsEqualTo(expectedAssemblyName);
	}

	[Test]
	public async Task ClientTestProject_ResolvesTheClientAsItsTarget(CancellationToken cancellationToken)
	{
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		using var tests = await BlazorFixtures.AddSiblingAsync(
			client,
			"Shop.Client.UnitTests",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);

		var evaluation = await tests.EvaluateAsync(
			["TargetProjectName", "RootNamespace", "AssemblyName", "IsTestProject"],
			["ProjectReference"],
			cancellationToken
		);

		await Assert.That(evaluation.Properties["IsTestProject"]).IsEqualTo("true");
		await Assert.That(evaluation.Properties["TargetProjectName"]).IsEqualTo("Shop.Client");
		await Assert.That(evaluation.Properties["RootNamespace"]).IsEqualTo("Acme.Shop.Client");
		await Assert.That(evaluation.Properties["AssemblyName"]).IsEqualTo("Acme.Shop.Client.UnitTests");
		await Assert
			.That(evaluation.Items["ProjectReference"].Select(TestHelpers.NormalizePath))
			.Contains("../Shop.Client/Shop.Client.csproj");
	}
}
