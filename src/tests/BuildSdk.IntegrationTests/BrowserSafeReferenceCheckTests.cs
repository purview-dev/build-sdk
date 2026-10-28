using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins PRBLZ0001: a WebAssembly client (or a browser-safe library) asks each of its project references
/// whether it sets <c>PurviewBrowserSafe</c>, so no library joins the browser download without anyone
/// deciding it was safe there.
/// </summary>
/// <remarks>
/// One test runs a full build to prove the check is wired into it; the rest invoke the target by name, which
/// asks the same question without restoring the WebAssembly packages.
/// </remarks>
sealed class BrowserSafeReferenceCheckTests
{
	const string CheckTarget = "-t:PurviewValidateBrowserSafeReferences";

	[Test]
	public async Task Client_ReferencingAnUndeclaredLibrary_WarnsDuringTheBuild(CancellationToken cancellationToken)
	{
		using var contracts = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(items: BlazorFixtures.RemoveAutomaticPackages),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WritePackageVersionsAsync(contracts, cancellationToken);
		using var client = await BlazorFixtures.AddSiblingAsync(
			contracts,
			"Shop.Client",
			BlazorFixtures.Client(
				items: """<ProjectReference Include="../Contracts/Contracts.csproj" />"""
					+ BlazorFixtures.RemoveAutomaticPackages
			),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WriteSourceAsync(client, "Program.cs", BlazorFixtures.ClientProgram, cancellationToken);

		var (success, output, errors) = await client.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("warning PRBLZ0001");
		await Assert.That(output).Contains("'Contracts'");
		await Assert.That(output).Contains("<PurviewBrowserSafe>true</PurviewBrowserSafe>");
	}

	[Test]
	public async Task Client_ReferencingABrowserSafeLibrary_DoesNotWarn(CancellationToken cancellationToken)
	{
		using var library = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library("<PurviewBrowserSafe>true</PurviewBrowserSafe>"),
			cancellationToken: cancellationToken
		);
		using var client = await AddClientAsync(library, cancellationToken: cancellationToken);

		var (code, output, errors) = await client.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRBLZ0001");
	}

	[Test]
	public async Task Client_ReferenceMetadataDeclaresItSafe_DoesNotWarn(CancellationToken cancellationToken)
	{
		// The route for a project that does not use this SDK, and so cannot answer the question itself.
		using var library = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await AddClientAsync(
			library,
			referenceMetadata: """ PurviewBrowserSafe="true" """,
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await client.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRBLZ0001");
	}

	[Test]
	public async Task Client_ReferenceThatIsNotAnAssembly_IsNotChecked(CancellationToken cancellationToken)
	{
		using var library = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await AddClientAsync(
			library,
			referenceMetadata: """ ReferenceOutputAssembly="false" """,
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await client.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRBLZ0001");
	}

	[Test]
	public async Task Client_CheckAsError_FailsOnAnUndeclaredLibrary(CancellationToken cancellationToken)
	{
		using var library = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await AddClientAsync(
			library,
			clientProperties: "<PurviewBrowserSafeReferenceCheckAsError>true</PurviewBrowserSafeReferenceCheckAsError>",
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await client.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsNotZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("error PRBLZ0001");
	}

	[Test]
	public async Task Client_CheckDisabled_DoesNotWarn(CancellationToken cancellationToken)
	{
		using var library = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var client = await AddClientAsync(
			library,
			clientProperties: "<DisablePurviewBrowserSafeReferenceCheck>true</DisablePurviewBrowserSafeReferenceCheck>",
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await client.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRBLZ0001");
	}

	[Test]
	public async Task BrowserSafeLibrary_ReferencingAnUndeclaredLibrary_Warns(CancellationToken cancellationToken)
	{
		// A browser-safe library is downloaded with the client, and so is everything it references: checking
		// each browser-safe library's own references is what makes the guarantee hold for the whole download.
		using var core = await BlazorFixtures.CreateAsync(
			"Core",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var contracts = await BlazorFixtures.AddSiblingAsync(
			core,
			"Contracts",
			BlazorFixtures.Library(
				"<PurviewBrowserSafe>true</PurviewBrowserSafe>",
				"""<ProjectReference Include="../Core/Core.csproj" />"""
			),
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await contracts.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("warning PRBLZ0001");
		await Assert.That(output).Contains("'Core'");
		await Assert.That(output).Contains("browser-safe library");
	}

	[Test]
	public async Task OrdinaryLibrary_ReferencingAnUndeclaredLibrary_IsNotChecked(CancellationToken cancellationToken)
	{
		using var core = await BlazorFixtures.CreateAsync(
			"Core",
			BlazorFixtures.Library(),
			cancellationToken: cancellationToken
		);
		using var orders = await BlazorFixtures.AddSiblingAsync(
			core,
			"Orders",
			BlazorFixtures.Library(items: """<ProjectReference Include="../Core/Core.csproj" />"""),
			cancellationToken: cancellationToken
		);

		var (code, output, errors) = await orders.RunMSBuildAsync(CheckTarget, cancellationToken);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRBLZ0001");
	}

	/// <summary>Adds a WebAssembly client that references <paramref name="library"/>.</summary>
	static Task<ProjectHarness> AddClientAsync(
		ProjectHarness library,
		string clientProperties = "",
		string referenceMetadata = "",
		CancellationToken cancellationToken = default
	) =>
		BlazorFixtures.AddSiblingAsync(
			library,
			"Shop.Client",
			BlazorFixtures.Client(
				clientProperties,
				$"""<ProjectReference Include="../{library.ProjectName}/{library.ProjectName}.csproj"{referenceMetadata}/>"""
			),
			cancellationToken: cancellationToken
		);
}
