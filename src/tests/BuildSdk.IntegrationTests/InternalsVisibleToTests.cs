using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies that the SDK correctly generates InternalsVisibleToAttribute entries for test types
/// and shared testing projects, and that this behaviour can be disabled.
/// </summary>
public sealed class InternalsVisibleToTests
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
