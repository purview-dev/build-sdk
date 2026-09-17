using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies RootNamespace-derived naming: AssemblyName and PackageId default to the fully
/// evaluated RootNamespace, explicit overrides always win, and no literal $(...) expression
/// survives into either name.
/// </summary>
public sealed class RootNamespaceNamingTests
{
	[Test]
	public async Task PackableLibrary_AssemblyNameAndPackageId_DefaultToRootNamespace(
		CancellationToken cancellationToken
	)
	{
		// Project filename (ZodSharp.SystemTextJson) equals its root namespace.
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["PackageId"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["AssemblyName"]).DoesNotContain("$(");
		await Assert.That(props["PackageId"]).DoesNotContain("$(");
	}

	[Test]
	public async Task PackableLibrary_AssemblyNameAndPackageId_DefaultToRootNamespace_WhenFilenameDiffers(
		CancellationToken cancellationToken
	)
	{
		// Project filename (JsonLib) differs from RootNamespace (ZodSharp.JsonLib).
		using var h = await ProjectHarness.CreateAsync(
			"JsonLib",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.JsonLib");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ZodSharp.JsonLib");
		await Assert.That(props["PackageId"]).IsEqualTo("ZodSharp.JsonLib");
	}

	[Test]
	public async Task ComposedRootNamespace_FromPropertyExpression_IsFullyEvaluated(CancellationToken cancellationToken)
	{
		// RootNamespace is composed from $(ProductPrefix) before the SDK import; AssemblyName and
		// PackageId must follow the composed value, not the NamespacePrefix-derived default.
		using var h = await ProjectHarness
			.For("SystemTextJson")
			.WithNamespacePrefix("Contoso")
			.WithPreImportPropertiesRaw(
				"<ProductPrefix>ZodSharp</ProductPrefix><RootNamespace>$(ProductPrefix).SystemTextJson</RootNamespace>"
			)
			.AddPropertyRaw("<IsPackable>true</IsPackable>")
			.BuildAsync(cancellationToken);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["PackageId"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["AssemblyName"]).DoesNotContain("$(");
		await Assert.That(props["PackageId"]).DoesNotContain("$(");
	}

	[Test]
	public async Task ExplicitAssemblyName_Override_Wins_AndPackageIdFollowsRootNamespace(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable><AssemblyName>Custom.Binary</AssemblyName>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["AssemblyName"]).IsEqualTo("Custom.Binary");
		await Assert.That(props["PackageId"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
	}

	[Test]
	public async Task ExplicitPackageId_Override_Wins_AndAssemblyNameFollowsRootNamespace(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable><PackageId>Custom.Package</PackageId>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["AssemblyName"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["PackageId"]).IsEqualTo("Custom.Package");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
	}

	[Test]
	public async Task ExplicitAssemblyName_And_PackageId_Overrides_AllWin(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable><AssemblyName>Custom.Binary</AssemblyName><PackageId>Custom.Package</PackageId>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["AssemblyName"]).IsEqualTo("Custom.Binary");
		await Assert.That(props["PackageId"]).IsEqualTo("Custom.Package");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
	}

	[Test]
	public async Task TestProject_AssemblyName_RetainsTestSuffix(CancellationToken cancellationToken)
	{
		// RootNamespace strips the test suffix; the test assembly must stay distinct from the source.
		using var h = await ProjectHarness.CreateAsync(
			"Api.UnitTests",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject.Api");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.Api.UnitTests");
		await Assert.That(props["PackageId"]).IsEqualTo("ExampleProject.Api.UnitTests");
	}

	[Test]
	public async Task MultiTargetedProject_AssemblyNameAndPackageId_AreConsistent(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable><TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks><TargetFramework></TargetFramework>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ZodSharp.SystemTextJson");
		await Assert.That(props["PackageId"]).IsEqualTo("ZodSharp.SystemTextJson");
	}

	[Test]
	public async Task EnableAssemblyNameGeneration_False_OptsOut_ToProjectName(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SystemTextJson",
			namespacePrefix: "ZodSharp",
			preImportProps: "<EnableAssemblyNameGeneration>false</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId");

		// Opting out restores standard .NET behaviour (project name).
		await Assert.That(props["AssemblyName"]).IsEqualTo("SystemTextJson");
		await Assert.That(props["PackageId"]).IsEqualTo("SystemTextJson");
	}

	[Test]
	public async Task SharedAndServiceDefaultsProjects_GetDistinctAssemblyNames(CancellationToken cancellationToken)
	{
		// Regression: a Shared.csproj and a ServiceDefaults.csproj in the same repo previously both
		// resolved to the stripped RootNamespace (Purview.ChangeOps). Each must keep its full logical
		// project name as the assembly/package identity.
		using var shared = await ProjectHarness.CreateAsync(
			"Shared",
			namespacePrefix: "Purview.ChangeOps",
			cancellationToken: cancellationToken
		);
		using var serviceDefaults = await ProjectHarness.CreateAsync(
			"ServiceDefaults",
			namespacePrefix: "Purview.ChangeOps",
			cancellationToken: cancellationToken
		);

		var sharedProps = await shared.GetPropertiesAsync(
			cancellationToken,
			"AssemblyName",
			"PackageId",
			"RootNamespace"
		);
		var serviceDefaultsProps = await serviceDefaults.GetPropertiesAsync(
			cancellationToken,
			"AssemblyName",
			"PackageId",
			"RootNamespace"
		);

		await Assert.That(sharedProps["RootNamespace"]).IsEqualTo("Purview.ChangeOps");
		await Assert.That(sharedProps["AssemblyName"]).IsEqualTo("Purview.ChangeOps.Shared");
		await Assert.That(sharedProps["PackageId"]).IsEqualTo("Purview.ChangeOps.Shared");

		await Assert.That(serviceDefaultsProps["RootNamespace"]).IsEqualTo("Purview.ChangeOps");
		await Assert.That(serviceDefaultsProps["AssemblyName"]).IsEqualTo("Purview.ChangeOps.ServiceDefaults");
		await Assert.That(serviceDefaultsProps["PackageId"]).IsEqualTo("Purview.ChangeOps.ServiceDefaults");
	}

	[Test]
	public async Task MidNameSuffix_AssemblyKeepsFullLogicalName(CancellationToken cancellationToken)
	{
		// Generalised rule: any stripped suffix (mid-name here) keeps the full logical project name.
		using var h = await ProjectHarness.CreateAsync(
			"Core.Infrastructure",
			namespacePrefix: "Acme",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");

		await Assert.That(props["RootNamespace"]).IsEqualTo("Acme.Infrastructure");
		await Assert.That(props["AssemblyName"]).IsEqualTo("Acme.Core.Infrastructure");
	}

	[Test]
	public async Task ExplicitRootNamespace_StillWins_ForAssemblyIdentity(CancellationToken cancellationToken)
	{
		// When RootNamespace is explicitly overridden before the SDK import, AssemblyName/PackageId
		// follow that override rather than the stripped-suffix logical name.
		using var h = await ProjectHarness
			.For("Shared")
			.WithNamespacePrefix("Purview.ChangeOps")
			.WithPreImportProperty("RootNamespace", "Custom.RootNamespace")
			.BuildAsync(cancellationToken);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace", "PackageId");

		await Assert.That(props["RootNamespace"]).IsEqualTo("Custom.RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("Custom.RootNamespace");
		await Assert.That(props["PackageId"]).IsEqualTo("Custom.RootNamespace");
	}
}
