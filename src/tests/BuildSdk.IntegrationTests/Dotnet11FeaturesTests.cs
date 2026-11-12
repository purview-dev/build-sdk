using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Pins the .NET 11 compiler feature switches the SDK applies to a <c>net11.0</c> (or later) target:
/// Runtime Async and the C# 15 updated memory-safety rules.
/// </summary>
/// <remarks>
/// The default target set is <c>net10.0;net11.0</c>, so the switches have to be per-TFM: applying
/// them in <c>Sdk.props</c> would enable them for the <c>net10.0</c> leg as well, where
/// runtime-async still needs <c>EnablePreviewFeatures</c>. <c>AllowUnsafeBlocks</c> is deliberately
/// part of the contract: the updated model is most useful with unsafe code still disallowed.
/// </remarks>
sealed class Dotnet11FeaturesTests
{
	[Test]
	public async Task Net11Target_EnablesUpdatedMemorySafetyRulesButNotRuntimeAsync(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			cancellationToken: cancellationToken
		);

		var eval = await h.EvaluateAsync(
			["Features", "MemorySafetyRules", "AllowUnsafeBlocks", "LangVersion"],
			cancellationToken: cancellationToken
		);

		await Assert
			.That(eval.Properties["Features"])
			.DoesNotContain("runtime-async=on")
			.Because("Runtime Async is opt-in: it changes how every await compiles");
		await Assert
			.That(eval.Properties["Features"])
			.Contains("updated-memory-safety-rules")
			.Because("MemorySafetyRules defaults to tier 2");
		await Assert.That(eval.Properties["MemorySafetyRules"]).IsEqualTo("2");
		await Assert
			.That(eval.Properties["AllowUnsafeBlocks"])
			.IsEqualTo("false")
			.Because("the updated rules must not quietly turn unsafe code on");
		await Assert
			.That(eval.Properties["LangVersion"])
			.IsEqualTo("latest")
			.Because("tier 2 is a feature switch and does not require the preview language version");
	}

	[Test]
	public async Task Net10Target_DoesNotReceiveTheNet11Switches(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net10.0",
			cancellationToken: cancellationToken
		);

		var features = await h.GetPropertyAsync("Features", cancellationToken);

		await Assert.That(features).DoesNotContain("runtime-async=on");
		await Assert.That(features).DoesNotContain("updated-memory-safety-rules");
	}

	[Test]
	public async Task Net11Target_KeepsFeaturesTheProjectAlreadySet(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<Features>$(Features);project-feature=on</Features>",
			cancellationToken: cancellationToken
		);

		var features = await h.GetPropertyAsync("Features", cancellationToken);

		await Assert.That(features).Contains("project-feature=on");
		await Assert.That(features).Contains("updated-memory-safety-rules");
	}

	[Test]
	public async Task RuntimeAsync_IsOptedInByPurviewRuntimeAsync(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<PurviewRuntimeAsync>true</PurviewRuntimeAsync>",
			cancellationToken: cancellationToken
		);

		var features = await h.GetPropertyAsync("Features", cancellationToken);

		await Assert.That(features).Contains("runtime-async=on");
		await Assert.That(features).Contains("updated-memory-safety-rules");
	}

	/// <summary>
	/// The .NET team's property name opts in as well, so a repository can use the name it already
	/// knows rather than learn the SDK's own switch.
	/// </summary>
	[Test]
	public async Task RuntimeAsync_IsOptedInByUseRuntimeAsync(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<UseRuntimeAsync>true</UseRuntimeAsync>",
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("Features", cancellationToken)).Contains("runtime-async=on");
	}

	/// <summary>
	/// An explicit opt-out under either name wins over the other name's opt-in, so a repository can
	/// disable Runtime Async without finding every place that enabled it.
	/// </summary>
	[Test]
	public async Task RuntimeAsync_ExplicitOptOutWinsOverAnOptIn(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<PurviewRuntimeAsync>true</PurviewRuntimeAsync><UseRuntimeAsync>false</UseRuntimeAsync>",
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("Features", cancellationToken)).DoesNotContain("runtime-async=on");
	}

	[Test]
	public async Task MemorySafetyTierZero_DisablesTheUpdatedRules(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<MemorySafetyRules>0</MemorySafetyRules>",
			cancellationToken: cancellationToken
		);

		var features = await h.GetPropertyAsync("Features", cancellationToken);

		await Assert.That(features).DoesNotContain("updated-memory-safety-rules");
		await Assert.That(features).DoesNotContain("runtime-async=on");
	}

	/// <summary>
	/// Tier 1 is the preview syntax, which is a language-version change rather than a feature switch.
	/// </summary>
	[Test]
	public async Task MemorySafetyTierOne_EnablesPreviewSyntax(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<MemorySafetyRules>1</MemorySafetyRules>",
			cancellationToken: cancellationToken
		);

		var eval = await h.EvaluateAsync(["Features", "LangVersion"], cancellationToken: cancellationToken);

		await Assert.That(eval.Properties["LangVersion"]).IsEqualTo("preview");
		await Assert.That(eval.Properties["Features"]).DoesNotContain("updated-memory-safety-rules");
	}

	[Test]
	public async Task MemorySafetyTierOne_KeepsAnExplicitLanguageVersion(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<MemorySafetyRules>1</MemorySafetyRules><LangVersion>15.0</LangVersion>",
			cancellationToken: cancellationToken
		);

		await Assert.That(await h.GetPropertyAsync("LangVersion", cancellationToken)).IsEqualTo("15.0");
	}

	[Test]
	public async Task UnrecognisedMemorySafetyTier_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			targetFramework: "net11.0",
			extraProps: "<MemorySafetyRules>3</MemorySafetyRules>",
			cancellationToken: cancellationToken
		);

		// The validation target, rather than a full build: the failure under test is evaluation-time and
		// a full build would also need the harness's telemetry package references restored.
		var (code, output, errors) = await h.RunMSBuildAsync(
			"-t:ValidatePurviewModernFeatureSettings",
			cancellationToken
		);

		await Assert.That(code).IsNotZero();
		await Assert.That(output + errors).Contains("PRCFG0001");
	}
}
