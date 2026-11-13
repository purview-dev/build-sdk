using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins PRBLZ0002 and the WebAssembly rule of the managed <c>Directory.Build.rsp</c>. A <c>net11.0</c>
/// WebAssembly client cannot build under MSBuild's multithreaded mode: the WebAssembly SDK's
/// <c>ReadWasmProps</c> task comes from a custom task factory that cannot run in the out-of-process task
/// host <c>-mt</c> moves it to, and fails with <c>MSB4060</c>, an error that names nothing a consumer can
/// trace back to this SDK. So a client writes the response file's <c>-mt:false</c> form, and a client
/// built multithreaded anyway stops with PRBLZ0002 before the WebAssembly SDK's targets run.
///
/// Every other harness test opts out of the response file, which is why the Blazor tests never saw
/// this; these opt back in. They target <c>net11.0</c> because a <c>net10.0</c> client builds under
/// <c>-mt</c>: the failure is in the 11.0 WebAssembly SDK.
/// </summary>
sealed class BlazorWebAssemblyMultiThreadedBuildTests
{
	const string ClientName = "Shop.Client";

	// The WebAssembly SDK's own error. PRBLZ0002 names ReadWasmProps too, so match the task factory.
	const string WebAssemblyFailure = "JsonToItemsTaskFactory";
	const string ManagedMultiThreadedRsp = "# Purview.BuildSdk - managed file\n-mt\n-nr:false\n";

	[Test]
	public async Task ForcedMultiThreadedBuild_StopsWithPRBLZ0002_BeforeTheWebAssemblySdk(
		CancellationToken cancellationToken
	)
	{
		using var client = await CreateClientAsync(preImportProperties: null, cancellationToken);

		var (code, output, errors) = await client.RunMSBuildAsync("-restore -t:Build -mt -nr:false", cancellationToken);

		await Assert.That(code).IsNotZero().Because(output + errors);
		await Assert.That(output + errors).Contains("error PRBLZ0002");
		await Assert.That(output + errors).Contains("command line");
		await Assert.That(output + errors).Contains("<PurviewMultiThreadedBuild>false</PurviewMultiThreadedBuild>");
		await Assert.That(output + errors).Contains("-mt:false");
		await Assert.That(output + errors).DoesNotContain(WebAssemblyFailure);
	}

	/// <summary>
	/// The check is scoped to the failure it replaces: a <c>net10.0</c> client builds under <c>-mt</c>, so
	/// stopping it would break a build that works.
	/// </summary>
	[Test]
	public async Task ForcedMultiThreadedBuild_OfANet10Client_StillBuilds(CancellationToken cancellationToken)
	{
		using var client = await CreateClientAsync(
			preImportProperties: null,
			cancellationToken,
			targetFramework: "net10.0",
			webAssemblyVersion: "10.0.*"
		);

		var (code, output, errors) = await client.RunMSBuildAsync("-restore -t:Build -mt -nr:false", cancellationToken);

		await Assert.That(code).IsZero().Because(output + errors);
		await Assert.That(output + errors).DoesNotContain("PRBLZ0002");
	}

	[Test]
	public async Task MultiThreadedBuildOptOut_Builds(CancellationToken cancellationToken)
	{
		using var client = await CreateClientAsync(
			"<BootstrapMultiThreadedBuildRspToRepoRoot>true</BootstrapMultiThreadedBuildRspToRepoRoot><PurviewMultiThreadedBuild>false</PurviewMultiThreadedBuild>",
			cancellationToken
		);

		var (success, output, errors) = await client.BuildAsync(restore: true, cancellationToken: cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		// The second build is the one that reads the response file the first wrote.
		var (secondSuccess, secondOutput, secondErrors) = await client.BuildAsync(
			restore: false,
			cancellationToken: cancellationToken
		);
		await Assert.That(secondSuccess).IsTrue().Because(TestHelpers.GenerateError(secondOutput, secondErrors));
		await Assert.That(await ReadRspLinesAsync(client, cancellationToken)).Contains("-mt:false");
	}

	/// <summary>
	/// A fresh repository with a WebAssembly client builds with no extra settings: the client's
	/// bootstrap writes the non-multithreaded form, naming itself so the repository's other projects
	/// leave it alone.
	/// </summary>
	[Test]
	public async Task Bootstrap_FromAWebAssemblyClient_WritesTheNonMultithreadedForm(
		CancellationToken cancellationToken
	)
	{
		using var client = await CreateClientAsync(BootstrapOn, cancellationToken);

		var (success, output, errors) = await client.BuildAsync(restore: true, cancellationToken: cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var lines = await ReadRspLinesAsync(client, cancellationToken);
		await Assert.That(lines[0]).StartsWith("# Purview.BuildSdk - managed file");
		await Assert.That(lines).Contains("-mt:false");
		await Assert.That(lines).DoesNotContain("-mt");
		await Assert.That(lines).Contains(ClientReasonLine(client));

		var (secondSuccess, secondOutput, secondErrors) = await client.BuildAsync(
			restore: false,
			cancellationToken: cancellationToken
		);
		await Assert.That(secondSuccess).IsTrue().Because(TestHelpers.GenerateError(secondOutput, secondErrors));
		await Assert.That(secondOutput + secondErrors).DoesNotContain(WebAssemblyFailure);
	}

	/// <summary>
	/// A repository upgrading from 2.0.x already has a managed <c>-mt</c> file, and the build that finds it
	/// is already multithreaded. It cannot be rescued, so it is rewritten and stopped with an error that
	/// says so; the next build is not multithreaded.
	/// </summary>
	[Test]
	public async Task Bootstrap_ManagedMultithreadedFile_IsRewrittenAndTheBuildStopsClearly(
		CancellationToken cancellationToken
	)
	{
		using var client = await CreateClientAsync(BootstrapOn, cancellationToken);
		var (restoreCode, restoreOutput, restoreErrors) = await client.RunMSBuildAsync("-t:Restore", cancellationToken);
		await Assert.That(restoreCode).IsZero().Because(restoreOutput + restoreErrors);

		await File.WriteAllTextAsync(RspPath(client), ManagedMultiThreadedRsp, cancellationToken);

		var (success, output, errors) = await client.BuildAsync(restore: false, cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("error PRBLZ0002");
		await Assert.That(output + errors).Contains("rewritten to -mt:false");
		await Assert.That(output + errors).DoesNotContain(WebAssemblyFailure);

		var lines = await ReadRspLinesAsync(client, cancellationToken);
		await Assert.That(lines).Contains("-mt:false");
		await Assert.That(lines).DoesNotContain("-mt");

		var (secondSuccess, secondOutput, secondErrors) = await client.BuildAsync(
			restore: false,
			cancellationToken: cancellationToken
		);
		await Assert.That(secondSuccess).IsTrue().Because(TestHelpers.GenerateError(secondOutput, secondErrors));
	}

	/// <summary>
	/// A response file without the marker line is the developer's. It is never rewritten, even when it
	/// is what makes the client's build fail, and the error points at it.
	/// </summary>
	[Test]
	public async Task UserOwnedResponseFile_IsNeverRewritten(CancellationToken cancellationToken)
	{
		using var client = await CreateClientAsync(BootstrapOn, cancellationToken);
		var (restoreCode, restoreOutput, restoreErrors) = await client.RunMSBuildAsync("-t:Restore", cancellationToken);
		await Assert.That(restoreCode).IsZero().Because(restoreOutput + restoreErrors);

		const string owned = "# my own switches\n-mt\n-nr:false\n";
		await File.WriteAllTextAsync(RspPath(client), owned, cancellationToken);

		var (success, output, errors) = await client.BuildAsync(restore: false, cancellationToken: cancellationToken);

		await Assert.That(success).IsFalse();
		await Assert.That(output + errors).Contains("error PRBLZ0002");
		await Assert.That(output + errors).Contains("Directory.Build.rsp");
		await Assert.That(output + errors).DoesNotContain(WebAssemblyFailure);
		await Assert.That(await File.ReadAllTextAsync(RspPath(client), cancellationToken)).IsEqualTo(owned);
	}

	/// <summary>An explicit setting is the user's decision and wins over the WebAssembly rule.</summary>
	[Test]
	public async Task ExplicitMultiThreadedBuild_OnAWebAssemblyClient_WritesMt(CancellationToken cancellationToken)
	{
		using var client = await CreateClientAsync(
			BootstrapOn + "<PurviewMultiThreadedBuild>true</PurviewMultiThreadedBuild>",
			cancellationToken
		);

		// This build is not multithreaded yet - the host read its switches before the file existed.
		var (success, output, errors) = await client.BuildAsync(restore: true, cancellationToken: cancellationToken);
		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

		var lines = await ReadRspLinesAsync(client, cancellationToken);
		await Assert.That(lines).Contains("-mt");
		await Assert.That(lines).DoesNotContain("-mt:false");
	}

	/// <summary>
	/// The response file is per repository, so a library built on its own must not turn the mode back
	/// on while the client it names is still there.
	/// </summary>
	[Test]
	public async Task Bootstrap_FromALibrary_KeepsAFileWrittenForAnExistingClient(CancellationToken cancellationToken)
	{
		using var library = await CreateLibraryAsync(cancellationToken);
		var clientPath = Path.Combine(library.SolutionDirectory, ClientName, ClientName + ".csproj");
		Directory.CreateDirectory(Path.GetDirectoryName(clientPath)!);
		await File.WriteAllTextAsync(clientPath, "<Project />", cancellationToken);

		var written = ClientRsp($"{ClientName}/{ClientName}.csproj");
		await File.WriteAllTextAsync(RspPath(library), written, cancellationToken);

		var (success, output, errors) = await library.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		await Assert.That(await File.ReadAllTextAsync(RspPath(library), cancellationToken)).IsEqualTo(written);
	}

	/// <summary>Once the client the file names is gone, the repository is back on <c>-mt</c>.</summary>
	[Test]
	public async Task Bootstrap_FromALibrary_RestoresMtWhenTheNamedClientIsGone(CancellationToken cancellationToken)
	{
		using var library = await CreateLibraryAsync(cancellationToken);
		await File.WriteAllTextAsync(
			RspPath(library),
			ClientRsp($"{ClientName}/{ClientName}.csproj"),
			cancellationToken
		);

		var (success, output, errors) = await library.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		var lines = await ReadRspLinesAsync(library, cancellationToken);
		await Assert.That(lines).Contains("-mt");
		await Assert.That(lines).DoesNotContain("-mt:false");
	}

	const string BootstrapOn =
		"<BootstrapMultiThreadedBuildRspToRepoRoot>true</BootstrapMultiThreadedBuildRspToRepoRoot>";

	static string ReasonLine(string relativeProjectPath) =>
		$"# -mt:false: the Blazor WebAssembly client '{relativeProjectPath}' cannot build multithreaded (PRBLZ0002).";

	static string ClientReasonLine(ProjectHarness client) =>
		ReasonLine(Path.GetRelativePath(client.SolutionDirectory, client.ProjectFilePath).Replace('\\', '/'));

	static string ClientRsp(string relativeProjectPath) =>
		$"# Purview.BuildSdk - managed file\n# Managed by Purview.BuildSdk. Delete the marker line to take ownership of this file.\n{ReasonLine(relativeProjectPath)}\n-mt:false\n";

	static string RspPath(ProjectHarness harness) => Path.Combine(harness.SolutionDirectory, "Directory.Build.rsp");

	static Task<string[]> ReadRspLinesAsync(ProjectHarness harness, CancellationToken cancellationToken) =>
		File.ReadAllLinesAsync(RspPath(harness), cancellationToken);

	static async Task<ProjectHarness> CreateClientAsync(
		string? preImportProperties,
		CancellationToken cancellationToken,
		string targetFramework = "net11.0",
		string webAssemblyVersion = "11.0.*"
	)
	{
		var builder = ProjectHarness
			.For(ClientName)
			.WithNamespacePrefix("Acme")
			.WithProjectFileContent(BlazorFixtures.Client(targetFramework: targetFramework));
		if (preImportProperties is not null)
			builder.WithPreImportPropertiesRaw(preImportProperties);

		var client = await builder.BuildAsync(cancellationToken);
		await BlazorFixtures.WritePackageVersionsAsync(client, cancellationToken, webAssemblyVersion);
		await BlazorFixtures.WriteSourceAsync(client, "Program.cs", BlazorFixtures.ClientProgram, cancellationToken);
		return client;
	}

	static Task<ProjectHarness> CreateLibraryAsync(CancellationToken cancellationToken) =>
		ProjectHarness
			.For("Contracts")
			.WithNamespacePrefix("Acme")
			.WithProjectFileContent(BlazorFixtures.Library(items: BlazorFixtures.RemoveAutomaticPackages))
			.WithPreImportPropertiesRaw(BootstrapOn)
			.BuildAsync(cancellationToken);
}
