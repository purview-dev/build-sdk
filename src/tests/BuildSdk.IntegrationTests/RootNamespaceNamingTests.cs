using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies RootNamespace-derived naming: AssemblyName and PackageId default to the fully
/// evaluated RootNamespace, explicit overrides always win, and no literal $(...) expression
/// survives into either name.
/// </summary>
sealed class RootNamespaceNamingTests
{
	// Suffix stripping is a convention for a RootNamespace this SDK derived, not licence to rewrite one
	// the author wrote down. FixRootNamespaceTarget runs after the project body and cannot tell the two
	// apart on its own, so without a guard an explicit 'Acme.CodeFixers' became 'Acme' - and because the
	// stripped value is what reaches the compiler as build_property.RootNamespace, every file in that
	// namespace then failed IDE0130 against a namespace nobody asked for.
	//
	// The generated MSBuildEditorConfig is asserted rather than the MSBuild property, because that file
	// is what the analyzer actually reads and it is written after the target has run.
	/// <summary>
	/// A Roslyn component's suffix is part of its identity, not noise: a generator, its analyzers and its
	/// code fixes are separate assemblies that each need their own namespace. Stripping these collapsed
	/// them onto the product namespace and produced IDE0130 across every Roslyn-component repository
	/// consuming this SDK, because each one declares the suffix in its sources.
	/// </summary>
	[Test]
	[Arguments("Acme.SourceGenerator")]
	[Arguments("Acme.SourceGenerators")]
	[Arguments("Acme.SourceGeneration")]
	[Arguments("Acme.Generators")]
	[Arguments("Acme.Analyzers")]
	[Arguments("Acme.CodeFixers")]
	[Arguments("Acme.CodeFixes")]
	public async Task RoslynComponentSuffixes_AreNotStripped(string projectName, CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			projectName,
			namespacePrefix: "Acme",
			extraProps: """
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			""",
			extraItems: """
			<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
			<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
			""",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-restore -t:Build", cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var editorConfig = Directory
			.EnumerateFiles(
				Path.Combine(h.ProjectDirectory, "obj"),
				"*.GeneratedMSBuildEditorConfig.editorconfig",
				SearchOption.AllDirectories
			)
			.First();

		var content = await File.ReadAllTextAsync(editorConfig, cancellationToken);
		await Assert.That(content).Contains($"build_property.RootNamespace = {projectName}");
	}

	[Test]
	public async Task ExplicitRootNamespace_IsNotSuffixStripped(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Acme.CodeFixers",
			namespacePrefix: "Acme",
			extraProps: """
			<RootNamespace>Acme.CodeFixers</RootNamespace>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			""",
			// The harness declares no central versions for the packages the SDK injects.
			extraItems: """
			<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
			<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
			""",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-restore -t:Build", cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var editorConfig = Directory
			.EnumerateFiles(
				Path.Combine(h.ProjectDirectory, "obj"),
				"*.GeneratedMSBuildEditorConfig.editorconfig",
				SearchOption.AllDirectories
			)
			.First();

		var content = await File.ReadAllTextAsync(editorConfig, cancellationToken);
		await Assert.That(content).Contains("build_property.RootNamespace = Acme.CodeFixers");
	}

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
