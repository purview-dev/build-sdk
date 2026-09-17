using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies SDK-controlled package and packaging behaviour — IsPackable defaults,
/// SourceLink opt-in/opt-out, and telemetry exclusion flags.
/// </summary>
public sealed class PackagebehaviourTests
{
	[Test]
	public async Task Library_IsPackable_False_ByDefault(CancellationToken cancellationToken)
	{
		// The SDK sets IsPackable=false by default; projects must opt in explicitly.
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsPackable", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task Library_IsPackable_True_WhenExplicitlySet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<IsPackable>true</IsPackable>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("IsPackable", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task TestProject_IsPackable_False(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyApp.UnitTests", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsPackable", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task SharedTestingProject_IsPackable_False(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("SharedTestingFramework", cancellationToken: cancellationToken);
		await Assert.That(await h.GetPropertyAsync("IsPackable", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task ExcludePurviewTelemetry_CanBeSetTrue(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("ExcludePurviewTelemetry", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task ExcludeMSTelemetryExtension_CanBeSetTrue(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			extraProps: "<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>",
			cancellationToken: cancellationToken
		);
		await Assert.That(await h.GetPropertyAsync("ExcludeMSTelemetryExtension", cancellationToken)).IsEqualTo("true");
	}
}
