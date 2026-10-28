using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins what <c>PurviewBrowserSafe=true</c> applies to a class library, that it never overrides what the
/// project set, and that it turns a browser runtime failure - reflection the trimmer cannot follow, an API
/// the browser does not support - into a build failure.
/// </summary>
sealed class BrowserSafeLibraryTests
{
	const string ReflectionSource = """
		namespace Acme;

		/// <summary>Creates a type by name.</summary>
		public static class Factory
		{
			/// <summary>Creates an instance of the named type.</summary>
			/// <param name="typeName">The assembly-qualified type name.</param>
			/// <returns>The instance.</returns>
			public static object? Create(string typeName) => Activator.CreateInstance(Type.GetType(typeName)!);
		}
		""";

	const string ProcessSource = """
		using System.Diagnostics;

		namespace Acme;

		/// <summary>Starts a process.</summary>
		public static class Launcher
		{
			/// <summary>Starts the tool.</summary>
			public static void Launch() => Process.Start("tool")?.Dispose();
		}
		""";

	[Test]
	public async Task BrowserSafe_AppliesTheBrowserSafeDefaults(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library("<PurviewBrowserSafe>true</PurviewBrowserSafe>"),
			cancellationToken: cancellationToken
		);

		var evaluation = await h.EvaluateAsync(
			[
				"IsAotCompatible",
				"IsTrimmable",
				"EnableTrimAnalyzer",
				"EnableAotAnalyzer",
				"WarningsAsErrors",
				"TreatWarningsAsErrors",
			],
			["SupportedPlatform"],
			cancellationToken
		);

		await Assert.That(evaluation.Properties["IsAotCompatible"]).IsEqualTo("true");
		await Assert.That(evaluation.Properties["IsTrimmable"]).IsEqualTo("true");
		await Assert.That(evaluation.Properties["EnableTrimAnalyzer"]).IsEqualTo("true");
		await Assert.That(evaluation.Properties["EnableAotAnalyzer"]).IsEqualTo("true");
		await Assert.That(evaluation.Items["SupportedPlatform"]).Contains("browser");

		var warningsAsErrors = evaluation
			.Properties["WarningsAsErrors"]
			.Split(';', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(warningsAsErrors).Contains("CA1416");
		await Assert.That(warningsAsErrors).Contains("IL2026");
		await Assert.That(warningsAsErrors).Contains("IL2057");
		await Assert.That(warningsAsErrors).Contains("IL3050");
		// The aim is to make *these* failures fatal, not to change the project's whole warning policy.
		await Assert.That(evaluation.Properties["TreatWarningsAsErrors"]).IsNotEqualTo("true");
	}

	[Test]
	public async Task BrowserSafe_KeepsWhatTheProjectSet(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(
				"""
				<PurviewBrowserSafe>true</PurviewBrowserSafe>
				<IsAotCompatible>false</IsAotCompatible>
				<WarningsAsErrors>CS0168</WarningsAsErrors>
				""",
				"""<SupportedPlatform Include="browser" />"""
			),
			cancellationToken: cancellationToken
		);

		var evaluation = await h.EvaluateAsync(
			["IsAotCompatible", "WarningsAsErrors"],
			["SupportedPlatform"],
			cancellationToken
		);

		await Assert.That(evaluation.Properties["IsAotCompatible"]).IsEqualTo("false");
		await Assert.That(evaluation.Properties["WarningsAsErrors"]).StartsWith("CS0168;");
		await Assert.That(evaluation.Items["SupportedPlatform"].Count(p => p == "browser")).IsEqualTo(1);
	}

	[Test]
	public async Task BrowserSafeWarningsAsErrorsFalse_LeavesWarningsAsErrorsAlone(CancellationToken cancellationToken)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(
				"""
				<PurviewBrowserSafe>true</PurviewBrowserSafe>
				<PurviewBrowserSafeWarningsAsErrors>false</PurviewBrowserSafeWarningsAsErrors>
				"""
			),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(cancellationToken, "IsAotCompatible", "WarningsAsErrors");

		await Assert.That(properties["IsAotCompatible"]).IsEqualTo("true");
		await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("IL2026");
		await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("CA1416");
	}

	[Test]
	public async Task BrowserSafe_OnAnExecutable_IsNotApplied(CancellationToken cancellationToken)
	{
		// Only a library is downloaded as a dependency of a client.
		using var h = await BlazorFixtures.CreateAsync(
			"Shop.Tool",
			BlazorFixtures.Library("<PurviewBrowserSafe>true</PurviewBrowserSafe><OutputType>Exe</OutputType>"),
			cancellationToken: cancellationToken
		);

		var properties = await h.GetPropertiesAsync(cancellationToken, "IsAotCompatible", "WarningsAsErrors");

		await Assert.That(properties["IsAotCompatible"]).IsEmpty();
		await Assert.That(properties["WarningsAsErrors"]).DoesNotContain("IL2026");
	}

	[Test]
	public async Task BrowserSafe_MultiTargeted_SetsIsAotCompatibleOnlyWhereItIsSupported(
		CancellationToken cancellationToken
	)
	{
		using var h = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures
				.Library("<PurviewBrowserSafe>true</PurviewBrowserSafe>")
				.Replace(
					"<TargetFramework>net10.0</TargetFramework>",
					"<TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>",
					StringComparison.Ordinal
				),
			cancellationToken: cancellationToken
		);

		var modern = await h.GetPropertiesAsync(
			new Dictionary<string, string> { ["TargetFramework"] = "net10.0" },
			cancellationToken,
			"IsAotCompatible"
		);
		var legacy = await h.GetPropertiesAsync(
			new Dictionary<string, string> { ["TargetFramework"] = "netstandard2.0" },
			cancellationToken,
			"IsAotCompatible"
		);

		await Assert.That(modern["IsAotCompatible"]).IsEqualTo("true");
		// The .NET SDK warns (NETSDK1210) for IsAotCompatible on a TFM that cannot honour it.
		await Assert.That(legacy["IsAotCompatible"]).IsEmpty();
	}

	[Test]
	public async Task BrowserSafe_ReflectionTheTrimmerCannotFollow_FailsTheBuild(CancellationToken cancellationToken)
	{
		using var h = await CreateLibraryAsync(
			"<PurviewBrowserSafe>true</PurviewBrowserSafe>",
			ReflectionSource,
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("error IL2057");
	}

	[Test]
	public async Task BrowserSafe_ProcessStart_FailsTheBuild(CancellationToken cancellationToken)
	{
		// The shipped .editorconfig sets CA1416 to suggestion; /warnaserror+:CA1416 still has to make a
		// browser-unsupported call fatal. If the compiler ever stops letting WarningsAsErrors outrank an
		// .editorconfig severity, this is the test that says so.
		using var h = await CreateLibraryAsync(
			"<PurviewBrowserSafe>true</PurviewBrowserSafe>",
			ProcessSource,
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("error CA1416");
		await Assert.That(output).Contains("'browser'");
	}

	[Test]
	public async Task BrowserSafeWarningsAsErrorsFalse_ReflectionIsAWarning(CancellationToken cancellationToken)
	{
		using var h = await CreateLibraryAsync(
			"<PurviewBrowserSafe>true</PurviewBrowserSafe><PurviewBrowserSafeWarningsAsErrors>false</PurviewBrowserSafeWarningsAsErrors>",
			ReflectionSource,
			cancellationToken
		);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("warning IL2057");
	}

	[Test]
	public async Task NotBrowserSafe_TheSameCalls_BuildAsBefore(CancellationToken cancellationToken)
	{
		using var h = await CreateLibraryAsync("", ReflectionSource, cancellationToken);
		await BlazorFixtures.WriteSourceAsync(h, "Launcher.cs", ProcessSource, cancellationToken);

		var (success, output, errors) = await h.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("IL2057");
		await Assert.That(output).DoesNotContain("CA1416");
	}

	static async Task<ProjectHarness> CreateLibraryAsync(
		string properties,
		string source,
		CancellationToken cancellationToken
	)
	{
		var h = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(properties, BlazorFixtures.RemoveAutomaticPackages),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WriteSourceAsync(h, "Source.cs", source, cancellationToken);
		return h;
	}
}
