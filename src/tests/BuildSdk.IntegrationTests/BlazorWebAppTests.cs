using System.ComponentModel;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// End-to-end Blazor Web App scenarios: a host, its WebAssembly client and a browser-safe Contracts library
/// build with no warnings, and the packages the SDK adds automatically actually run inside the WebAssembly
/// runtime rather than merely compiling.
/// </summary>
sealed class BlazorWebAppTests
{
	const string ContractsSource = """
		namespace Acme;

		/// <summary>An order placed through the storefront.</summary>
		/// <param name="Id">The order identifier.</param>
		/// <param name="Total">The order total.</param>
		public sealed record Order(string Id, decimal Total);
		""";

	[Test]
	public async Task BlazorWebApp_WithBrowserSafeContracts_BuildsWithNoWarnings(CancellationToken cancellationToken)
	{
		using var contracts = await BlazorFixtures.CreateAsync(
			"Contracts",
			BlazorFixtures.Library("<PurviewBrowserSafe>true</PurviewBrowserSafe>"),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WritePackageVersionsAsync(contracts, cancellationToken);
		await BlazorFixtures.WriteSourceAsync(contracts, "Order.cs", ContractsSource, cancellationToken);

		using var client = await BlazorFixtures.AddSiblingAsync(
			contracts,
			"Shop.Client",
			BlazorFixtures.Client(items: """<ProjectReference Include="../Contracts/Contracts.csproj" />"""),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WriteSourceAsync(client, "Program.cs", BlazorFixtures.ClientProgram, cancellationToken);

		// The host declares no reference to its client: the <Name>/<Name>.Client pairing supplies it.
		using var host = await BlazorFixtures.AddSiblingAsync(
			contracts,
			"Shop",
			BlazorFixtures.Web(
				items: """<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" />"""
			),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WriteSourceAsync(host, "Program.cs", BlazorFixtures.HostProgram, cancellationToken);

		var (success, output, errors) = await host.BuildAsync(restore: true, cancellationToken: cancellationToken);

		await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));
		var warnings = output
			.Split('\n')
			.Where(line => line.Contains(": warning ", StringComparison.Ordinal))
			.Distinct()
			.ToArray();
		await Assert.That(warnings).IsEmpty().Because(string.Join('\n', warnings));

		// The client's framework files reached the host, so the pairing reference really was used.
		await Assert
			.That(
				Directory.Exists(
					Path.Combine(client.ProjectDirectory, "bin", "Debug", "net10.0", "wwwroot", "_framework")
				)
			)
			.IsTrue();
	}

	[Test]
	public async Task AutomaticTelemetryPackages_RunInsideTheWebAssemblyRuntime(CancellationToken cancellationToken)
	{
		// The SDK adds Purview.Telemetry.SourceGenerator (analyzer-only) and
		// Microsoft.Extensions.Telemetry.Abstractions to every non-test project, a WebAssembly client included.
		// Compiling is not evidence that they work in the browser, so this publishes a client that exercises
		// both - generated activity, log and metric code, and a Telemetry.Abstractions type - and runs it on
		// the published WebAssembly runtime, booted under Node.
		using var client = await BlazorFixtures.CreateAsync(
			"Shop.Client",
			BlazorFixtures.Client(),
			cancellationToken: cancellationToken
		);
		await BlazorFixtures.WritePackageVersionsAsync(client, cancellationToken);
		await BlazorFixtures.WriteSourceAsync(client, "Program.cs", TelemetryProbeProgram, cancellationToken);

		var publishDirectory = Path.Combine(client.SolutionDirectory, "publish");
		var (publishCode, publishOutput, publishErrors) = await client.RunProcessAsync(
			"dotnet",
			$"publish \"{client.ProjectFilePath}\" -nologo -v:q -p:IsPublishable=true -o \"{publishDirectory}\"",
			cancellationToken: cancellationToken
		);

		await Assert.That(publishCode).IsZero().Because(TestHelpers.GenerateError(publishOutput, publishErrors));
		// Trimming is where a package that is not browser-safe shows itself first.
		await Assert.That(publishOutput).DoesNotContain("warning IL");

		var framework = Path.Combine(publishDirectory, "wwwroot", "_framework");
		await Assert.That(File.Exists(Path.Combine(framework, "dotnet.js"))).IsTrue();

		// The runner lives outside the published output, which is only ever passed in as an argument.
		var runnerDirectory = Path.Combine(client.SolutionDirectory, "runner");
		Directory.CreateDirectory(runnerDirectory);
		var runner = Path.Combine(runnerDirectory, "run-wasm.mjs");
		await File.WriteAllTextAsync(runner, NodeRunner, cancellationToken);

		(int Code, string StdOut, string StdErr) run;
		try
		{
			run = await client.RunProcessAsync(
				"node",
				$"\"{runner}\" \"{framework}\"",
				runnerDirectory,
				cancellationToken
			);
		}
		catch (Win32Exception exception)
		{
			Skip.Test($"Node is not on PATH, so the WebAssembly runtime cannot be booted: {exception.Message}");
			return;
		}

		await Assert.That(run.Code).IsZero().Because(TestHelpers.GenerateError(run.StdOut, run.StdErr));
		await Assert.That(run.StdOut).Contains("PROBE-OK activity=PlacingOrder request=GetOrder");
	}

	const string TelemetryProbeProgram = """
		using System.Diagnostics;
		using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
		using Microsoft.Extensions.DependencyInjection;
		using Microsoft.Extensions.Http.Diagnostics;
		using Purview.Telemetry;

		if (args.Contains("--probe"))
		{
			using var listener = new ActivityListener
			{
				ShouldListenTo = _ => true,
				Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			};
			ActivitySource.AddActivityListener(listener);

			ServiceCollection services = new();
			services.AddLogging();
			services.AddMetrics();
			services.AddShopTelemetry();
			using var provider = services.BuildServiceProvider();

			var telemetry = provider.GetRequiredService<IShopTelemetry>();
			using var activity = telemetry.PlacingOrder("order-1");
			telemetry.OrderPlaced("order-1");
			telemetry.RecordOrderValue(42);

			RequestMetadata metadata = new("GET", "/orders/{id}") { RequestName = "GetOrder" };
			Console.WriteLine($"PROBE-OK activity={activity?.OperationName} request={metadata.RequestName}");
			return;
		}

		var builder = WebAssemblyHostBuilder.CreateDefault(args);
		await builder.Build().RunAsync().ConfigureAwait(false);

		[ActivitySource]
		[Logger]
		[Meter]
		interface IShopTelemetry
		{
			[Activity]
			Activity? PlacingOrder(string orderId);

			[Info]
			void OrderPlaced(string orderId);

			[Histogram]
			void RecordOrderValue(int value);
		}
		""";

	const string NodeRunner = """
		// Boots a published Blazor WebAssembly client's runtime under Node and runs its Main with --probe.
		import { pathToFileURL } from 'node:url';
		import path from 'node:path';

		const { dotnet } = await import(pathToFileURL(path.join(process.argv[2], 'dotnet.js')).href);
		const runtime = await dotnet.withApplicationArguments('--probe').create();
		process.exit(await runtime.runMain());
		""";
}
