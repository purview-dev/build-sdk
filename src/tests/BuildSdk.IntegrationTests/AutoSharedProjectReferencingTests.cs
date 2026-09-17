using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies that Shared*.csproj projects are automatically referenced by all non-test
/// and non-shared projects. This tests the SDK's auto-discovery and auto-referencing
/// feature that allows projects to transparently depend on shared libraries without
/// explicit ProjectReference declarations.
/// </summary>
public sealed class AutoSharedProjectReferencingTests
{
	[Test]
	public async Task AspireHostProject_WithSharedProject_HasIsAspireProjectResourceFalse(
		CancellationToken cancellationToken
	)
	{
		using var sharedHarness = await ProjectHarness
			.For("Shared")
			.WithTargetFramework("net10.0")
			.BuildAsync(cancellationToken);

		using var appHostHarness = await ProjectHarness
			.For("Acme.AppHost")
			.WithProjectFileContent(
				$"""
				<Project>
					<Import Project="{SdkPaths.SdkDirectory}/Sdk.props" />
					<PropertyGroup>
						<NamespacePrefix>Acme</NamespacePrefix>
						<DisableNamespacePrefixCheck>true</DisableNamespacePrefixCheck>
						<TargetFramework>net10.0</TargetFramework>
					</PropertyGroup>
					<!--
					  <Project Sdk="Aspire.Sdk.Host" />
					-->
				</Project>
				"""
			)
			.WithSolutionDirectory(sharedHarness.SolutionDirectory)
			.BuildAsync(cancellationToken);

		var projectReferences = await appHostHarness.GetProjectReferencesAsync(cancellationToken);
		var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();

		await Assert.That(normalized).Contains("../Shared/Shared.csproj");

		var aspireResourceFlags = await appHostHarness.GetItemMetadataValuesAsync(
			"ProjectReference",
			"IsAspireProjectResource",
			cancellationToken
		);

		await Assert.That(aspireResourceFlags).Contains("false");
	}

	[Test]
	public async Task NonTestProject_AutoReferences_SharedProject_InSiblingDirectory(
		CancellationToken cancellationToken
	)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "Shared");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "Shared.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).Contains("../Shared/Shared.csproj");
		}
	}

	[Test]
	public async Task NonTestProject_AutoReferences_SharedFramework_Project(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "SharedFramework");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "SharedFramework.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).Contains("../SharedFramework/SharedFramework.csproj");
		}
	}

	[Test]
	public async Task NonTestProject_AutoReferences_Multiple_SharedProjects(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				// Create Shared project
				var sharedDir = Path.Combine(workDir, "Shared");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "Shared.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);

				// Create SharedUtils project
				var sharedUtilsDir = Path.Combine(workDir, "SharedUtils");
				Directory.CreateDirectory(sharedUtilsDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedUtilsDir, "SharedUtils.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).Contains("../Shared/Shared.csproj");
			await Assert.That(normalized).Contains("../SharedUtils/SharedUtils.csproj");
		}
	}

	[Test]
	public async Task NonTestProject_AutoReferences_SharedInfrastructure_Project(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "SharedInfrastructure");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "SharedInfrastructure.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).Contains("../SharedInfrastructure/SharedInfrastructure.csproj");
		}
	}

	[Test]
	public async Task NonTestProject_DoesNotAutoReference_SharedTestingProject(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "SharedTestingFramework");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "SharedTestingFramework.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// SharedTesting* projects should be excluded from non-test projects
			await Assert.That(normalized).DoesNotContain("../SharedTestingFramework/SharedTestingFramework.csproj");
		}
	}

	[Test]
	public async Task TestProject_DoesNotAutoReference_SharedProject(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary.UnitTests",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "Shared");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "Shared.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// Test projects should NOT auto-reference regular Shared*.csproj projects
			await Assert.That(normalized).DoesNotContain("../Shared/Shared.csproj");
		}
	}

	[Test]
	public async Task TestProject_AutoReferences_SharedTestingProject(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary.UnitTests",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, "SharedTestingFramework");
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, "SharedTestingFramework.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// Test projects SHOULD auto-reference SharedTesting* projects
			await Assert.That(normalized).Contains("../SharedTestingFramework/SharedTestingFramework.csproj");
		}
	}

	[Test]
	public async Task TestProject_AutoReferences_TargetProject_InSiblingDirectory(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary.UnitTests",
			async workDir =>
			{
				var targetDir = Path.Combine(workDir, "MyLibrary");
				Directory.CreateDirectory(targetDir);
				await File.WriteAllTextAsync(
					Path.Combine(targetDir, "MyLibrary.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// Test projects should auto-reference their target project (MyLibrary in this case)
			await Assert.That(normalized).Contains("../MyLibrary/MyLibrary.csproj");
		}
	}

	[Test]
	public async Task SharedProject_DoesNotAutoReference_SharedProjects(CancellationToken cancellationToken)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"Shared",
			async workDir =>
			{
				var anotherSharedDir = Path.Combine(workDir, "SharedUtils");
				Directory.CreateDirectory(anotherSharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(anotherSharedDir, "SharedUtils.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// Shared projects should NOT auto-reference other shared projects
			await Assert.That(normalized).DoesNotContain("../SharedUtils/SharedUtils.csproj");
		}
	}

	[Test]
	[Arguments("SharedLibrary")]
	[Arguments("SharedLib")]
	[Arguments("SharedHelpers")]
	[Arguments("SharedUtilities")]
	[Arguments("SharedUtils")]
	[Arguments("SharedInfra")]
	[Arguments("SharedInfrastructure")]
	public async Task NonTestProject_AutoReferences_AllWellKnownSharedProjectNames(
		string sharedProjectName,
		CancellationToken cancellationToken
	)
	{
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"MyLibrary",
			async workDir =>
			{
				var sharedDir = Path.Combine(workDir, sharedProjectName);
				Directory.CreateDirectory(sharedDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedDir, $"{sharedProjectName}.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).Contains($"../{sharedProjectName}/{sharedProjectName}.csproj");
		}
	}

	[Test]
	public async Task SharedTestingProject_DoesNotAutoReference_SharedPrefixedTestProject(
		CancellationToken cancellationToken
	)
	{
		// Reproduces the cyclic ProjectReference bug (MSB4006): a SharedTestingFramework
		// project placed next to a Shared.UnitTests project must NOT gain an automatic
		// ProjectReference to Shared.UnitTests. Shared-testing projects manage their own
		// explicit ProjectReferences and must never participate in the ../Shared*/Shared*.csproj
		// library glob, otherwise SharedTestingFramework -> Shared.UnitTests and
		// Shared.UnitTests -> SharedTestingFramework form a cycle.
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"SharedTestingFramework",
			async workDir =>
			{
				var sharedUnitTestsDir = Path.Combine(workDir, "Shared.UnitTests");
				Directory.CreateDirectory(sharedUnitTestsDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedUnitTestsDir, "Shared.UnitTests.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// SharedTestingFramework must NOT auto-reference the Shared.UnitTests sibling
			// (the ../Shared*/Shared*.csproj glob must not fire for shared-testing projects).
			await Assert.That(normalized).DoesNotContain("../Shared.UnitTests/Shared.UnitTests.csproj");
		}
	}

	[Test]
	public async Task SharedTestingProject_WithForcedIsTestProjectFalse_DoesNotReferenceSharedTestProject(
		CancellationToken cancellationToken
	)
	{
		// Mirrors the restore-phase proof from the bug report. During NuGet restore's
		// _GenerateRestoreProjectPathWalk, IsTestProject is false for SharedTestingFramework
		// (dynamic test-package detection does not run), so the ../Shared*/Shared*.csproj
		// glob would otherwise pull in the Shared.UnitTests sibling. Force that condition
		// with -p:IsTestProject=false and confirm the glob still does not fire.
		var (harness, _) = await TestHelpers.CreateProjectStructureAsync(
			"SharedTestingFramework",
			async workDir =>
			{
				var sharedUnitTestsDir = Path.Combine(workDir, "Shared.UnitTests");
				Directory.CreateDirectory(sharedUnitTestsDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedUnitTestsDir, "Shared.UnitTests.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var references = await harness.GetItemIdentitiesAsync(
				"ProjectReference",
				"-p:IsTestProject=false",
				cancellationToken
			);
			var normalized = references.Select(TestHelpers.NormalizePath).ToList();
			await Assert.That(normalized).DoesNotContain("../Shared.UnitTests/Shared.UnitTests.csproj");
		}
	}

	[Test]
	public async Task SharedPrefixedTestProject_StillAutoReferences_SharedTestingProject(
		CancellationToken cancellationToken
	)
	{
		// Non-regression: a Shared-prefixed test project (Shared.UnitTests) must still
		// auto-reference its sibling SharedTestingFramework via the test-project glob,
		// so the fix removes only the reverse edge of the cycle, not the legitimate one.
		var (harness, projectReferences) = await TestHelpers.CreateProjectStructureAsync(
			"Shared.UnitTests",
			async workDir =>
			{
				var sharedTestingDir = Path.Combine(workDir, "SharedTestingFramework");
				Directory.CreateDirectory(sharedTestingDir);
				await File.WriteAllTextAsync(
					Path.Combine(sharedTestingDir, "SharedTestingFramework.csproj"),
					"""
					<Project Sdk="Microsoft.NET.Sdk">
						<PropertyGroup>
							<TargetFramework>net10.0</TargetFramework>
						</PropertyGroup>
					</Project>
					""",
					cancellationToken
				);
			},
			null,
			cancellationToken
		);

		using (harness)
		{
			var normalized = projectReferences.Select(TestHelpers.NormalizePath).ToList();
			// Test projects SHOULD auto-reference SharedTesting* projects, even when the
			// test project itself is Shared-prefixed (Shared.UnitTests).
			await Assert.That(normalized).Contains("../SharedTestingFramework/SharedTestingFramework.csproj");
		}
	}
}
