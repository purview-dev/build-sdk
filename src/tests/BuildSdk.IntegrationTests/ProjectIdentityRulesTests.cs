using System.Diagnostics;
using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

public sealed class ProjectIdentityRulesTests
{
	[Test]
	public async Task RootProject_UsesNamespacePrefixAsIs(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ExampleProject",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject");
	}

	[Test]
	public async Task ShortChildProject_IsPrefixed(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.SourceGenerator");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject.SourceGenerator");
	}

	[Test]
	public async Task FullyQualifiedChildProject_IsNotDoublePrefixed(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ExampleProject.SourceGenerator",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.SourceGenerator");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject.SourceGenerator");
	}

	[Test]
	public async Task PartialPrefix_IsNotTreatedAsQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ExampleProjectExtra",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.ExampleProjectExtra");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject.ExampleProjectExtra");
	}

	[Test]
	public async Task AssemblyName_DefaultsToFullyQualifiedLogicalName(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("AssemblyName", cancellationToken)).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task RootNamespace_DefaultsToFullyQualifiedLogicalName(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("RootNamespace", cancellationToken)).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task ProjectName_MatchingNamespacePrefixTail_DoesNotDuplicateSegment(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ResourceIsolation",
			namespacePrefix: "Purview.Aspire.ResourceIsolation",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace", "PackageId");

		await Assert.That(props["AssemblyName"]).IsEqualTo("Purview.Aspire.ResourceIsolation");
		await Assert.That(props["RootNamespace"]).IsEqualTo("Purview.Aspire.ResourceIsolation");
		await Assert.That(props["PackageId"]).IsEqualTo("Purview.Aspire.ResourceIsolation");
	}

	[Test]
	[Arguments("ServiceDefaults")]
	[Arguments("ResourceIsolation.ServiceDefaults")]
	[Arguments("Purview.Aspire.ResourceIsolation.ServiceDefaults")]
	public async Task NamespaceTailDedup_StripsRootNamespace_ButKeepsFullAssemblyIdentity(
		string projectName,
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			projectName,
			namespacePrefix: "Purview.Aspire.ResourceIsolation",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace", "PackageId");

		// RootNamespace still strips the ServiceDefaults suffix (all three names map to the same
		// namespace), but the assembly/package identity keeps the full logical project name so
		// ServiceDefaults projects stay distinct from their parent.
		await Assert.That(props["AssemblyName"]).IsEqualTo("Purview.Aspire.ResourceIsolation.ServiceDefaults");
		await Assert.That(props["RootNamespace"]).IsEqualTo("Purview.Aspire.ResourceIsolation");
		await Assert.That(props["PackageId"]).IsEqualTo("Purview.Aspire.ResourceIsolation.ServiceDefaults");
	}

	[Test]
	public async Task ExplicitAssemblyName_IsRespected(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			extraProps: "<AssemblyName>Custom.Assembly</AssemblyName>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("AssemblyName", cancellationToken)).IsEqualTo("Custom.Assembly");
	}

	[Test]
	public async Task ExplicitRootNamespace_IsRespected(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			extraProps: "<RootNamespace>Custom.Namespace</RootNamespace>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("RootNamespace", cancellationToken)).IsEqualTo("Custom.Namespace");
	}

	[Test]
	public async Task PackageId_DefaultsToAssemblyName(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId");

		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.Api");
		await Assert.That(props["PackageId"]).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task ChildUnitTests_AreFullyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator.UnitTests",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.SourceGenerator.UnitTests");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject.SourceGenerator");
	}

	[Test]
	public async Task RootUnitTests_RemainCorrectlyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ExampleProject.UnitTests",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "RootNamespace");
		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.UnitTests");
		await Assert.That(props["RootNamespace"]).IsEqualTo("ExampleProject");
	}

	[Test]
	public async Task IntegrationTests_AreFullyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api.IntegrationTests",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("AssemblyName", cancellationToken))
			.IsEqualTo("ExampleProject.Api.IntegrationTests");
	}

	[Test]
	public async Task ArchitectureTests_AreFullyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Core.ArchitectureTests",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("AssemblyName", cancellationToken))
			.IsEqualTo("ExampleProject.Core.ArchitectureTests");
	}

	[Test]
	public async Task ContractTests_AreFullyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Core.ContractTests",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("AssemblyName", cancellationToken))
			.IsEqualTo("ExampleProject.Core.ContractTests");
	}

	[Test]
	public async Task FunctionalTests_AreFullyQualified(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Core.FunctionalTests",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("AssemblyName", cancellationToken))
			.IsEqualTo("ExampleProject.Core.FunctionalTests");
	}

	[Test]
	public async Task InternalsVisibleTo_UsesFullyQualifiedTestAssemblyNames(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync(
			"-t:InternalsVisibleToTarget -noconlog -getItem:AssemblyAttribute",
			cancellationToken
		);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		var friendAssemblies = ExtractItemMetadataValues(stdOut, "AssemblyAttribute", "_Parameter1");

		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.UnitTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.IntegrationTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.ArchitectureTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.ContractTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.FunctionalTests");
		// SharedTesting projects should also be prefixed when EnableAssemblyNameGeneration=true
		await Assert.That(friendAssemblies).Contains("ExampleProject.SharedTestingFramework");
	}

	[Test]
	public async Task InternalsVisibleTo_UsesFullyQualifiedTestAssemblyNames_ByDefault(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync(
			"-t:InternalsVisibleToTarget -noconlog -getItem:AssemblyAttribute",
			cancellationToken
		);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		var friendAssemblies = ExtractItemMetadataValues(stdOut, "AssemblyAttribute", "_Parameter1");

		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.UnitTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.IntegrationTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.ArchitectureTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.ContractTests");
		await Assert.That(friendAssemblies).Contains("ExampleProject.SourceGenerator.FunctionalTests");
		// SharedTesting projects are prefixed by default (EnableAssemblyNameGeneration defaults to true)
		await Assert.That(friendAssemblies).Contains("ExampleProject.SharedTestingFramework");
	}

	[Test]
	public async Task InternalsVisibleTo_UsesExplicitAssemblyName(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			extraProps: "<AssemblyName>Custom.Assembly</AssemblyName>",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync(
			"-t:InternalsVisibleToTarget -noconlog -getItem:AssemblyAttribute",
			cancellationToken
		);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		var friendAssemblies = ExtractItemMetadataValues(stdOut, "AssemblyAttribute", "_Parameter1");

		await Assert.That(friendAssemblies).Contains("Custom.Assembly.UnitTests");
		await Assert.That(friendAssemblies).Contains("Custom.Assembly.IntegrationTests");
		await Assert.That(friendAssemblies).Contains("Custom.Assembly.ArchitectureTests");
		await Assert.That(friendAssemblies).Contains("Custom.Assembly.ContractTests");
		await Assert.That(friendAssemblies).Contains("Custom.Assembly.FunctionalTests");
		// SharedTesting projects use the prefixed name by default (EnableAssemblyNameGeneration defaults to true)
		await Assert.That(friendAssemblies).Contains("ExampleProject.SharedTestingFramework");
	}

	[Test]
	public async Task AssemblyName_DefaultsToRootNamespace(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("AssemblyName", cancellationToken)).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task AssemblyName_DefaultsToRootNamespace_ForShortChildProject(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"SourceGenerator",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("AssemblyName", cancellationToken))
			.IsEqualTo("ExampleProject.SourceGenerator");
	}

	[Test]
	public async Task PackageId_DefaultsToRootNamespace(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(cancellationToken, "AssemblyName", "PackageId");

		await Assert.That(props["AssemblyName"]).IsEqualTo("ExampleProject.Api");
		await Assert.That(props["PackageId"]).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task EnableAssemblyNameGeneration_DefaultsToTrue(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert
			.That(await h.GetPropertyAsync("EnableAssemblyNameGeneration", cancellationToken))
			.IsEqualTo("true");
	}

	[Test]
	public async Task EnableAssemblyNameGeneration_CanBeSetToFalse_ToOptOut(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			namespacePrefix: "ExampleProject",
			preImportProps: "<EnableAssemblyNameGeneration>false</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("EnableAssemblyNameGeneration", cancellationToken))
			.IsEqualTo("false");
		// Opting out restores the standard .NET behaviour (project name).
		await Assert.That(await h.GetPropertyAsync("AssemblyName", cancellationToken)).IsEqualTo("MyLibrary");
	}

	[Test]
	public async Task EnableAssemblyNameGeneration_CanBeSetToTrue(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("EnableAssemblyNameGeneration", cancellationToken))
			.IsEqualTo("true");
	}

	[Test]
	public async Task EnableAssemblyNameGeneration_CanBeSetViaDirectoryBuildProps(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"Api",
			namespacePrefix: "ExampleProject",
			preImportProps: "<EnableAssemblyNameGeneration>true</EnableAssemblyNameGeneration>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("AssemblyName", cancellationToken)).IsEqualTo("ExampleProject.Api");
	}

	[Test]
	public async Task ProjectFileNamingConvention_AcceptsShortProjectFilenames(CancellationToken cancellationToken)
	{
		var (exitCode, output) = await ValidateProjectFileNamingAsync(
			"SourceGenerator",
			"SourceGenerator.csproj",
			cancellationToken
		);
		await Assert.That(exitCode).IsEqualTo(0).Because(output);
	}

	[Test]
	public async Task ProjectFileNamingConvention_RejectsRedundantFullyQualifiedFileNames(
		CancellationToken cancellationToken
	)
	{
		var (exitCode, output) = await ValidateProjectFileNamingAsync(
			"SourceGenerator.UnitTests",
			"ExampleProject.SourceGenerator.UnitTests.csproj",
			cancellationToken
		);
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(output).Contains("PurviewProjectFileNameMismatch");
	}

	[Test]
	public async Task ProjectDirectoryAndProjectFileName_MustMatch(CancellationToken cancellationToken)
	{
		var (exitCode, output) = await ValidateProjectFileNamingAsync(
			"SourceGenerator.UnitTests",
			"SourceGenerator.ArchitectureTests.csproj",
			cancellationToken
		);
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(output).Contains("PurviewProjectFileNameMismatch");
	}

	static async Task<(int ExitCode, string Output)> ValidateProjectFileNamingAsync(
		string projectDirectoryName,
		string projectFileName,
		CancellationToken cancellationToken
	)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));
		var projectDirectory = Path.Combine(tempRoot, projectDirectoryName);
		var projectFilePath = Path.Combine(projectDirectory, projectFileName);

		Directory.CreateDirectory(projectDirectory);

		try
		{
			await File.WriteAllTextAsync(
				Path.Combine(projectDirectory, "Directory.Build.props"),
				$"""
				<Project>
					<PropertyGroup>
						<NamespacePrefix>ExampleProject</NamespacePrefix>
					</PropertyGroup>
					<Import Project="{SdkPaths.SdkDirectory}/Sdk.props" />
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(projectDirectory, "Directory.Build.targets"),
				$"""
				<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
					<Import Project="{SdkPaths.SdkDirectory}/Sdk.targets" />
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(projectDirectory, "Directory.Packages.props"),
				"""
				<Project>
					<PropertyGroup>
						<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
					</PropertyGroup>
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				projectFilePath,
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFramework>net10.0</TargetFramework>
					</PropertyGroup>
				</Project>
				""",
				cancellationToken
			);

			var (exitCode, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"msbuild \"{projectFilePath}\" -nologo -t:ValidateProjectFileNamingConventionTarget",
				projectDirectory,
				cancellationToken
			);

			return (exitCode, stdOut + stdErr);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}

	static List<string> ExtractItemMetadataValues(string msbuildOutput, string itemType, string metadataName)
	{
		var jsonStart = msbuildOutput.IndexOf('{', StringComparison.Ordinal);
		if (jsonStart < 0)
			return [];

		try
		{
			using var doc = JsonDocument.Parse(msbuildOutput[jsonStart..]);
			if (
				doc.RootElement.TryGetProperty("Items", out var itemsEl)
				&& itemsEl.TryGetProperty(itemType, out var typeEl)
			)
			{
				var values = new List<string>();
				foreach (var item in typeEl.EnumerateArray())
				{
					if (item.TryGetProperty(metadataName, out var metadataValue))
						values.Add(metadataValue.GetString() ?? string.Empty);
				}

				return values;
			}
		}
		catch (JsonException)
		{
			return [];
		}

		return [];
	}

	static async Task<(int Code, string StdOut, string StdErr)> RunProcessAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken
	)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		process.Start();
		var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdOutTask, await stdErrTask);
	}
}
