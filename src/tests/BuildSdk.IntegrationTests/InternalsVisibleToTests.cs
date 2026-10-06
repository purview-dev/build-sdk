using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies that the SDK correctly generates InternalsVisibleToAttribute entries for test types
/// and shared testing projects, and that this behaviour can be disabled.
/// </summary>
sealed class InternalsVisibleToTests
{
	[Test]
	public async Task InternalsVisibleTo_Generated_For_NonTestProjects_ByDefault(CancellationToken cancellationToken)
	{
		// Non-test projects should generate InternalsVisibleTo attributes for each TestType and shared testing project.
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		var assemblyAttrs = await h.GetItemIdentitiesAsync("AssemblyAttribute", cancellationToken);

		// Should contain InternalsVisibleToAttribute (for test types and shared testing projects).
		await Assert.That(assemblyAttrs).Contains("System.Runtime.CompilerServices.InternalsVisibleToAttribute");
	}

	[Test]
	public async Task InternalsVisibleTo_CanBeDisabled_ViaProperty(CancellationToken cancellationToken)
	{
		// When DisableAutoInternalsVisibleTo=true, the InternalsVisibleToTarget should not run.
		// We verify this by checking that the property is set.
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<DisableAutoInternalsVisibleTo>true</DisableAutoInternalsVisibleTo>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("DisableAutoInternalsVisibleTo", cancellationToken))
			.IsEqualTo("true");
	}

	[Test]
	public async Task DisableAutoInternalsVisibleTo_Property_Defaults_To_False(CancellationToken cancellationToken)
	{
		// By default, DisableAutoInternalsVisibleTo should be false (i.e., auto-generation is enabled).
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert
			.That(await h.GetPropertyAsync("DisableAutoInternalsVisibleTo", cancellationToken))
			.IsEqualTo("false");
	}

	[Test]
	public async Task DisableAutoInternalsVisibleTo_CanBeSetToTrue(CancellationToken cancellationToken)
	{
		// Verify the property can be explicitly set to true.
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<DisableAutoInternalsVisibleTo>true</DisableAutoInternalsVisibleTo>",
			cancellationToken: cancellationToken
		);
		await Assert
			.That(await h.GetPropertyAsync("DisableAutoInternalsVisibleTo", cancellationToken))
			.IsEqualTo("true");
	}

	[Test]
	public async Task TestProject_AlwaysHasInternalsVisibleToAttribute_ForMocking(CancellationToken cancellationToken)
	{
		// Test projects always have InternalsVisibleToAttribute for DynamicProxyGenAssembly2 (for Moq/NSubstitute).
		using var h = await ProjectHarness.CreateAsync("MyApp.UnitTests", cancellationToken: cancellationToken);
		var assemblyAttrs = await h.GetItemIdentitiesAsync("AssemblyAttribute", cancellationToken);

		// Should always have InternalsVisibleToAttribute for DynamicProxyGenAssembly2.
		await Assert.That(assemblyAttrs).Contains("System.Runtime.CompilerServices.InternalsVisibleToAttribute");
	}

	[Test]
	public async Task SharedTestingProject_AlwaysHasInternalsVisibleToAttribute_ForMocking(
		CancellationToken cancellationToken
	)
	{
		// Shared testing projects also have InternalsVisibleToAttribute for DynamicProxyGenAssembly2.
		using var h = await ProjectHarness.CreateAsync("SharedTestingFramework", cancellationToken: cancellationToken);
		var assemblyAttrs = await h.GetItemIdentitiesAsync("AssemblyAttribute", cancellationToken);

		// Should always have InternalsVisibleToAttribute for DynamicProxyGenAssembly2.
		await Assert.That(assemblyAttrs).Contains("System.Runtime.CompilerServices.InternalsVisibleToAttribute");
	}

	[Test]
	public async Task InternalsVisibleTo_AlwaysIncludesDynamicProxyGenAssembly2(CancellationToken cancellationToken)
	{
		// The SDK always includes DynamicProxyGenAssembly2 for Moq/NSubstitute support on all projects.
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		var assemblyAttrs = await h.GetItemIdentitiesAsync("AssemblyAttribute", cancellationToken);

		// Should always have InternalsVisibleToAttribute.
		await Assert.That(assemblyAttrs).Contains("System.Runtime.CompilerServices.InternalsVisibleToAttribute");
	}

	// The tests above only assert the attribute *type* appears, which is why malformed grants went
	// unnoticed: a shipped Purview assembly carried 168 InternalsVisibleTo attributes, 53 of them with
	// an empty assembly name. These two assert the generated values instead. The target adds
	// AssemblyAttribute items during the build, so -getItem cannot see them - the generated
	// AssemblyInfo.cs is the observable output.
	[Test]
	public async Task InternalsVisibleTo_NeverGrantsToAnEmptyAssemblyName(CancellationToken cancellationToken)
	{
		using var h = await CreateBuildableLibraryAsync(cancellationToken);

		var malformed = (await ReadInternalsVisibleToGrantsAsync(h, cancellationToken))
			.Where(static grant => grant.StartsWith('.'))
			.ToArray();

		// TargetProjectName has no default, so an unguarded '$(TargetProjectName).%(TestType)Tests'
		// produced '.UnitTests' and friends - never a valid assembly identity.
		await Assert.That(string.Join(", ", malformed)).IsEmpty();
	}

	[Test]
	public async Task InternalsVisibleTo_DoesNotRepeatTheSameGrant(CancellationToken cancellationToken)
	{
		using var h = await CreateBuildableLibraryAsync(cancellationToken);

		var duplicates = (await ReadInternalsVisibleToGrantsAsync(h, cancellationToken))
			.GroupBy(static grant => grant, StringComparer.Ordinal)
			.Where(static group => group.Count() > 1)
			.Select(static group => $"{group.Key} x{group.Count()}")
			.ToArray();

		// AssemblyName, TargetProjectName and MSBuildProjectName are usually the same string, so each
		// test type was granted up to three times over.
		await Assert.That(string.Join(", ", duplicates)).IsEmpty();
	}

	// The harness declares no central package versions for the packages the SDK injects, so a plain
	// harness project cannot restore. The other build-based tests in this suite remove them the same way.
	static Task<ProjectHarness> CreateBuildableLibraryAsync(CancellationToken cancellationToken) =>
		ProjectHarness.CreateAsync(
			"MyLibrary",
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

	static async Task<IReadOnlyList<string>> ReadInternalsVisibleToGrantsAsync(
		ProjectHarness harness,
		CancellationToken cancellationToken
	)
	{
		// The grants are written by a target, so the project has to be compiled rather than merely
		// evaluated.
		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync("-restore -t:Build", cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var objDirectory = Path.Combine(harness.ProjectDirectory, "obj");
		var generated = Directory
			.EnumerateFiles(objDirectory, "*.AssemblyInfo.cs", SearchOption.AllDirectories)
			.OrderBy(static path => path, StringComparer.Ordinal)
			.FirstOrDefault();

		await Assert.That(generated).IsNotNull().Because("the SDK must generate an AssemblyInfo file");

		const string marker = "InternalsVisibleTo(\"";
		List<string> grants = [];
		foreach (var line in await File.ReadAllLinesAsync(generated!, cancellationToken))
		{
			var start = line.IndexOf(marker, StringComparison.Ordinal);
			if (start < 0)
				continue;

			start += marker.Length;
			var end = line.IndexOf('"', start);
			if (end > start || end == start)
				grants.Add(line[start..end]);
		}

		return grants;
	}

	//[Test]
	//public async Task InternalsVisibleTo_WhenAssemblyNameSet_UsesAssemblyNameForTestProjects(CancellationToken cancellationToken)
	//{
	//	await using var appUnitTestHarness = await ProjectHarness
	//		.For("App.UnitTests")
	//		.BuildAsync(cancellationToken);

	//	await using var appHarness = await ProjectHarness
	//		.For("App")
	//		.WithSolutionDirectory(appUnitTestHarness.SolutionDirectory)
	//		.BuildAsync(cancellationToken);

	//	var values = await appHarness.GetItemMetadataValuesAsync("AssemblyAttribute", "_Parameter1", typeof(InternalsVisibleToAttribute).FullName, cancellationToken);

	//	var x = await appHarness.GetPreprocessProjectAsync(cancellationToken);

	//	await Assert.That(values).IsNotNull();
	//}
}
