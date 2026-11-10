using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins <c>ServerShared</c>: a Shared-style project that is referenced automatically only by projects
/// that cannot be downloaded to a browser, plus the checks that stop it reaching one anyway.
/// </summary>
/// <remarks>
/// Detection is tested by evaluation and the reference check by invoking its target, matching the
/// browser-safe suite: both ask the same question without restoring WebAssembly packages.
/// </remarks>
sealed class ServerSharedTests
{
	static Task<ProjectHarness> CreateAsync(string projectName, string content, CancellationToken cancellationToken) =>
		BlazorFixtures.CreateAsync(projectName, content, cancellationToken: cancellationToken);

	static Task<ProjectHarness> AddSiblingAsync(
		ProjectHarness owner,
		string projectName,
		string content,
		CancellationToken cancellationToken
	) => BlazorFixtures.AddSiblingAsync(owner, projectName, content, cancellationToken: cancellationToken);

	static async Task<IReadOnlyList<string>> ReferencesAsync(
		ProjectHarness project,
		CancellationToken cancellationToken
	) =>
		(await project.EvaluateAsync(itemTypes: ["ProjectReference"], cancellationToken: cancellationToken)).Items[
			"ProjectReference"
		];

	static async Task<ProjectHarness> CreateWithServerSharedSiblingAsync(
		string projectName,
		string projectProperties,
		CancellationToken cancellationToken
	)
	{
		var project = await CreateAsync(projectName, BlazorFixtures.Library(projectProperties), cancellationToken);

		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		return project;
	}

	[Test]
	public async Task ServerProject_AutomaticallyReferencesServerShared(CancellationToken cancellationToken)
	{
		using var project = await CreateWithServerSharedSiblingAsync("Api", string.Empty, cancellationToken);

		var references = await ReferencesAsync(project, cancellationToken);

		await Assert.That(references.Any(path => path.Contains("ServerShared", StringComparison.Ordinal))).IsTrue();
		await Assert.That(await project.GetPropertyAsync("IsClientSideProject", cancellationToken)).IsEqualTo("false");
	}

	[Test]
	public async Task ServerSharedProject_IsDetectedByName(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync("ServerShared", BlazorFixtures.Library(), cancellationToken);

		await Assert.That(await project.GetPropertyAsync("IsServerSharedProject", cancellationToken)).IsEqualTo("true");
	}

	[Test]
	public async Task ServerSharedProject_DoesNotReferenceItself(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync("ServerShared", BlazorFixtures.Library(), cancellationToken);

		var references = await ReferencesAsync(project, cancellationToken);

		await Assert.That(references).IsEmpty();
	}

	[Test]
	public async Task WebAssemblyClient_DoesNotReferenceServerShared(CancellationToken cancellationToken)
	{
		using var client = await CreateWithServerSharedSiblingAsync("Shop.Client", string.Empty, cancellationToken);

		await Assert.That(await client.GetPropertyAsync("IsClientSideProject", cancellationToken)).IsEqualTo("true");
		await Assert
			.That(
				(await ReferencesAsync(client, cancellationToken)).Any(path =>
					path.Contains("ServerShared", StringComparison.Ordinal)
				)
			)
			.IsFalse();
	}

	[Test]
	public async Task BrowserSafeLibrary_DoesNotReferenceServerShared(CancellationToken cancellationToken)
	{
		using var project = await CreateWithServerSharedSiblingAsync(
			"Contracts",
			"<PurviewBrowserSafe>true</PurviewBrowserSafe>",
			cancellationToken
		);

		await Assert.That(await project.GetPropertyAsync("IsClientSideProject", cancellationToken)).IsEqualTo("true");
		await Assert
			.That(
				(await ReferencesAsync(project, cancellationToken)).Any(path =>
					path.Contains("ServerShared", StringComparison.Ordinal)
				)
			)
			.IsFalse();
	}

	[Test]
	public async Task RazorClassLibrary_DoesNotReferenceServerShared(CancellationToken cancellationToken)
	{
		var rcl = await BlazorFixtures.CreateAsync(
			"Widgets",
			"""
			<Project Sdk="Microsoft.NET.Sdk.Razor">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
					<DisableSourceLink>true</DisableSourceLink>
				</PropertyGroup>
			</Project>
			""",
			namespacePrefix: "Acme",
			cancellationToken: cancellationToken
		);

		using (rcl)
		{
			using var serverShared = await AddSiblingAsync(
				rcl,
				"ServerShared",
				BlazorFixtures.Library(),
				cancellationToken
			);

			await Assert.That(await rcl.GetPropertyAsync("IsClientSideProject", cancellationToken)).IsEqualTo("true");
			await Assert
				.That(
					(await ReferencesAsync(rcl, cancellationToken)).Any(path =>
						path.Contains("ServerShared", StringComparison.Ordinal)
					)
				)
				.IsFalse();
		}
	}

	/// <summary>
	/// The conservative Razor-class-library classification is a default, not a verdict: a repository
	/// that knows the library is server-only can say so and get the automatic reference back.
	/// </summary>
	[Test]
	public async Task ExplicitNotClientSide_RestoresTheAutomaticReference(CancellationToken cancellationToken)
	{
		var rcl = await BlazorFixtures.CreateAsync(
			"Widgets",
			"""
			<Project Sdk="Microsoft.NET.Sdk.Razor">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
					<DisableSourceLink>true</DisableSourceLink>
					<IsClientSideProject>false</IsClientSideProject>
				</PropertyGroup>
			</Project>
			""",
			namespacePrefix: "Acme",
			cancellationToken: cancellationToken
		);

		using (rcl)
		{
			using var serverShared = await AddSiblingAsync(
				rcl,
				"ServerShared",
				BlazorFixtures.Library(),
				cancellationToken
			);

			await Assert
				.That(
					(await ReferencesAsync(rcl, cancellationToken)).Any(path =>
						path.Contains("ServerShared", StringComparison.Ordinal)
					)
				)
				.IsTrue();
		}
	}

	/// <summary>
	/// A Shared project is what a browser-bound project most often pulls in, so it must never inherit a
	/// server-only reference automatically.
	/// </summary>
	[Test]
	public async Task SharedProject_DoesNotReferenceServerShared(CancellationToken cancellationToken)
	{
		using var shared = await CreateWithServerSharedSiblingAsync("Shared", string.Empty, cancellationToken);

		await Assert
			.That(
				(await ReferencesAsync(shared, cancellationToken)).Any(path =>
					path.Contains("ServerShared", StringComparison.Ordinal)
				)
			)
			.IsFalse();
	}

	[Test]
	public async Task AutomaticReference_CanBeDisabled(CancellationToken cancellationToken)
	{
		using var project = await CreateWithServerSharedSiblingAsync(
			"Api",
			"<PurviewAutoServerSharedReference>false</PurviewAutoServerSharedReference>",
			cancellationToken
		);

		await Assert
			.That(
				(await ReferencesAsync(project, cancellationToken)).Any(path =>
					path.Contains("ServerShared", StringComparison.Ordinal)
				)
			)
			.IsFalse();
	}

	[Test]
	public async Task ClientReferencingServerShared_Warns(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync(
			"Shop.Client",
			BlazorFixtures.Library(items: """<ProjectReference Include="../ServerShared/ServerShared.csproj" />"""),
			cancellationToken
		);
		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		var (code, output, errors) = await project.RunMSBuildAsync(
			"-t:PurviewValidateServerSharedReferences",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("warning PRSHR0001");
		await Assert.That(output).Contains("'ServerShared'");
	}

	[Test]
	public async Task SharedReferencingServerShared_Warns(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync(
			"Shared",
			BlazorFixtures.Library(items: """<ProjectReference Include="../ServerShared/ServerShared.csproj" />"""),
			cancellationToken
		);
		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		var (code, output, errors) = await project.RunMSBuildAsync(
			"-t:PurviewValidateServerSharedReferences",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).Contains("warning PRSHR0002");
	}

	[Test]
	public async Task ServerSharedReferenceCheck_AsError_Fails(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync(
			"Shop.Client",
			BlazorFixtures.Library(
				"<PurviewServerSharedReferenceCheckAsError>true</PurviewServerSharedReferenceCheckAsError>",
				items: """<ProjectReference Include="../ServerShared/ServerShared.csproj" />"""
			),
			cancellationToken
		);
		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		var (code, output, errors) = await project.RunMSBuildAsync(
			"-t:PurviewValidateServerSharedReferences",
			cancellationToken
		);

		await Assert.That(code).IsNotZero();
		await Assert.That(output).Contains("error PRSHR0001");
	}

	/// <summary>
	/// The documented escape hatch is the existing browser-safe acceptance: a reference that says it was
	/// judged safe is not a decision this check gets to second-guess.
	/// </summary>
	[Test]
	public async Task ServerSharedReference_DeclaredBrowserSafe_DoesNotWarn(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync(
			"Shop.Client",
			BlazorFixtures.Library(
				items: """<ProjectReference Include="../ServerShared/ServerShared.csproj" PurviewBrowserSafe="true" />"""
			),
			cancellationToken
		);
		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		var (code, output, errors) = await project.RunMSBuildAsync(
			"-t:PurviewValidateServerSharedReferences",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRSHR0001");
		await Assert.That(output).DoesNotContain("PRSHR0002");
	}

	[Test]
	public async Task Check_CanBeDisabled(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync(
			"Shop.Client",
			BlazorFixtures.Library(
				"<DisablePurviewServerSharedReferenceCheck>true</DisablePurviewServerSharedReferenceCheck>",
				items: """<ProjectReference Include="../ServerShared/ServerShared.csproj" />"""
			),
			cancellationToken
		);
		using var serverShared = await AddSiblingAsync(
			project,
			"ServerShared",
			BlazorFixtures.Library(),
			cancellationToken
		);

		var (code, output, errors) = await project.RunMSBuildAsync(
			"-t:PurviewValidateServerSharedReferences",
			cancellationToken
		);

		await Assert.That(code).IsZero().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(output).DoesNotContain("PRSHR0001");
	}

	[Test]
	public async Task RootNamespace_StripsTheServerSharedSuffix(CancellationToken cancellationToken)
	{
		using var project = await CreateAsync("ServerShared", BlazorFixtures.Library(), cancellationToken);

		await Assert.That(await project.GetPropertyAsync("RootNamespace", cancellationToken)).IsEqualTo("Acme");
	}
}
