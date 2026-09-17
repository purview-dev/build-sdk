using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies packable-project defaults and final package artifacts: DLL output matches the
/// evaluated AssemblyName, the NuGet package filename and nuspec id match PackageId, portable
/// PDBs are delivered via the .snupkg (not the normal .nupkg), XML docs are produced, README
/// auto-inclusion works when present, and solution-wide packs skip non-packable projects without
/// warnings unless explicitly opted in.
/// </summary>
public sealed class PackableProjectDefaultsTests
{
	const string OfflinePackProps =
		"<IsPackable>true</IsPackable>"
		+ "<DisableSourceLink>true</DisableSourceLink>"
		+ "<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>"
		+ "<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>";

	[Test]
	public async Task PackableProject_Defaults_Applied_WhenNotSupplied(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps,
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(
			cancellationToken,
			"GenerateDocumentationFile",
			"IncludeSymbols",
			"SymbolPackageFormat",
			"PublishRepositoryUrl",
			"EmbedUntrackedSources",
			"DebugType",
			"AllowedOutputExtensionsInPackageBuildOutputFolder"
		);

		await Assert.That(props["GenerateDocumentationFile"]).IsEqualTo("true");
		await Assert.That(props["IncludeSymbols"]).IsEqualTo("true");
		await Assert.That(props["SymbolPackageFormat"]).IsEqualTo("snupkg");
		await Assert.That(props["PublishRepositoryUrl"]).IsEqualTo("true");
		await Assert.That(props["EmbedUntrackedSources"]).IsEqualTo("true");
		await Assert.That(props["DebugType"]).IsEqualTo("portable");
		await Assert
			.That(props["AllowedOutputExtensionsInPackageBuildOutputFolder"])
			.DoesNotContain(".pdb")
			.Because("Portable PDBs must be delivered via the .snupkg, not the normal .nupkg.");
	}

	[Test]
	public async Task PackableProject_ExplicitValues_ArePreserved(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: "<IsPackable>true</IsPackable>"
				+ "<GenerateDocumentationFile>false</GenerateDocumentationFile>"
				+ "<IncludeSymbols>false</IncludeSymbols>"
				+ "<SymbolPackageFormat>symbols.nupkg</SymbolPackageFormat>"
				+ "<PublishRepositoryUrl>false</PublishRepositoryUrl>"
				+ "<EmbedUntrackedSources>false</EmbedUntrackedSources>"
				+ "<DebugType>embedded</DebugType>"
				+ "<IncludeSource>false</IncludeSource>",
			cancellationToken: cancellationToken
		);

		var props = await h.GetPropertiesAsync(
			cancellationToken,
			"GenerateDocumentationFile",
			"IncludeSymbols",
			"SymbolPackageFormat",
			"PublishRepositoryUrl",
			"EmbedUntrackedSources",
			"DebugType",
			"IncludeSource"
		);

		await Assert.That(props["GenerateDocumentationFile"]).IsEqualTo("false");
		await Assert.That(props["IncludeSymbols"]).IsEqualTo("false");
		await Assert.That(props["SymbolPackageFormat"]).IsEqualTo("symbols.nupkg");
		await Assert.That(props["PublishRepositoryUrl"]).IsEqualTo("false");
		await Assert.That(props["EmbedUntrackedSources"]).IsEqualTo("false");
		await Assert.That(props["DebugType"]).IsEqualTo("embedded");
		await Assert.That(props["IncludeSource"]).IsEqualTo("false");
	}

	[Test]
	public async Task PackableLibrary_NupkgAndSnupkg_Contents(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps,
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		// Compiled DLL filename matches the evaluated AssemblyName.
		var dllPath = Path.Combine(h.ProjectDirectory, "bin", "Release", "net10.0", "ZodSharp.SystemTextJson.dll");
		await Assert.That(File.Exists(dllPath)).IsTrue().Because($"Expected DLL not found: {dllPath}");

		var packageVersion = ExtractPackVersion(stdOut, "ZodSharp.SystemTextJson");
		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		var nupkgPath = Path.Combine(feedDirectory, $"ZodSharp.SystemTextJson.{packageVersion}.nupkg");
		var snupkgPath = Path.Combine(feedDirectory, $"ZodSharp.SystemTextJson.{packageVersion}.snupkg");

		await Assert.That(File.Exists(nupkgPath)).IsTrue();
		await Assert.That(File.Exists(snupkgPath)).IsTrue();

		using (var nupkg = await ZipFile.OpenReadAsync(nupkgPath, cancellationToken))
		{
			var entries = nupkg.Entries.Select(entry => entry.FullName).ToList();
			await Assert.That(entries).Contains("lib/net10.0/ZodSharp.SystemTextJson.dll");
			await Assert.That(entries).Contains("lib/net10.0/ZodSharp.SystemTextJson.xml");
			await Assert
				.That(entries.Any(entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
				.IsFalse()
				.Because("The normal package must not contain PDB files by default.");

			var nuspecEntry = nupkg.Entries.Single(entry =>
				entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
			);
			await using var nuspecStream = await nuspecEntry.OpenAsync(cancellationToken);
			var nuspec = XDocument.Load(nuspecStream);
			await Assert.That(nuspec.Root!.Name.LocalName).IsEqualTo("package");
			await Assert
				.That(nuspec.Descendants().First(e => e.Name.LocalName == "id").Value)
				.IsEqualTo("ZodSharp.SystemTextJson");
		}

		using (var snupkg = await ZipFile.OpenReadAsync(snupkgPath, cancellationToken))
		{
			var entries = snupkg.Entries.Select(entry => entry.FullName).ToList();
			await Assert
				.That(entries)
				.Contains("lib/net10.0/ZodSharp.SystemTextJson.pdb")
				.Because(
					$"The symbol package must contain the portable PDB.{Environment.NewLine}Entries: {string.Join(", ", entries)}"
				);
			await Assert
				.That(entries.Any(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
				.IsFalse()
				.Because("The symbol package must not contain compiled assemblies.");
		}
	}

	[Test]
	public async Task PackableLibrary_FilenameDiffersFromRootNamespace_PackageUsesRootNamespace(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"JsonLib",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps,
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		await Assert
			.That(Directory.GetFiles(feedDirectory, "ZodSharp.JsonLib.*.nupkg"))
			.IsNotEmpty()
			.Because("The package must be named after RootNamespace, not the project filename.");

		var dllPath = Path.Combine(h.ProjectDirectory, "bin", "Release", "net10.0", "ZodSharp.JsonLib.dll");
		await Assert.That(File.Exists(dllPath)).IsTrue();
	}

	[Test]
	public async Task MultiTargeted_PackableLibrary_ProducesBothTfmFolders(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps
				+ "<TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks><TargetFramework></TargetFramework>",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packageVersion = ExtractPackVersion(stdOut, "ZodSharp.SystemTextJson");
		var nupkgPath = Path.Combine(h.SolutionDirectory, "feed", $"ZodSharp.SystemTextJson.{packageVersion}.nupkg");
		using var nupkg = await ZipFile.OpenReadAsync(nupkgPath, cancellationToken);
		var entries = nupkg.Entries.Select(entry => entry.FullName).ToList();

		await Assert.That(entries).Contains("lib/net10.0/ZodSharp.SystemTextJson.dll");
		await Assert.That(entries).Contains("lib/netstandard2.0/ZodSharp.SystemTextJson.dll");
	}

	[Test]
	public async Task ExplicitAssemblyName_ProducesMatchingDll_AndPackageNamedByRootNamespace(
		CancellationToken cancellationToken
	)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps + "<AssemblyName>Custom.Binary</AssemblyName>",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var dllPath = Path.Combine(h.ProjectDirectory, "bin", "Release", "net10.0", "Custom.Binary.dll");
		await Assert.That(File.Exists(dllPath)).IsTrue();

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		await Assert
			.That(Directory.GetFiles(feedDirectory, "ZodSharp.SystemTextJson.*.nupkg"))
			.IsNotEmpty()
			.Because("PackageId still defaults to RootNamespace when only AssemblyName is overridden.");
	}

	[Test]
	public async Task ExplicitPackageId_ProducesPackageNamedByOverride(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps + "<PackageId>Custom.Package</PackageId>",
			cancellationToken: cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var dllPath = Path.Combine(h.ProjectDirectory, "bin", "Release", "net10.0", "ZodSharp.SystemTextJson.dll");
		await Assert.That(File.Exists(dllPath)).IsTrue();

		var feedDirectory = Path.Combine(h.SolutionDirectory, "feed");
		await Assert.That(Directory.GetFiles(feedDirectory, "Custom.Package.*.nupkg")).IsNotEmpty();
	}

	[Test]
	public async Task Readme_Included_WhenPresent_AndPackageReadmeFileUnset(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps
				+ "<EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		// Make the repo root discoverable and place a README there. A package.json is required
		// alongside the .git marker so version detection succeeds.
		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "zodsharp-systemtextjson", "version": "1.0.0"}""",
			cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "README.md"),
			"# ZodSharp.SystemTextJson\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packageVersion = ExtractPackVersion(stdOut, "ZodSharp.SystemTextJson");
		var nupkgPath = Path.Combine(h.SolutionDirectory, "feed", $"ZodSharp.SystemTextJson.{packageVersion}.nupkg");
		using var nupkg = await ZipFile.OpenReadAsync(nupkgPath, cancellationToken);
		var entries = nupkg.Entries.Select(entry => entry.FullName).ToList();

		await Assert.That(entries).Contains("README.md");

		var nuspecEntry = nupkg.Entries.Single(entry =>
			entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
		);
		await using var nuspecStream = await nuspecEntry.OpenAsync(cancellationToken);
		var nuspec = XDocument.Load(nuspecStream);
		var readmeElement = nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == "readme");
		await Assert
			.That(readmeElement?.Value)
			.IsEqualTo("README.md")
			.Because("PackageReadmeFile must be registered automatically when unset.");
	}

	[Test]
	public async Task Readme_Absent_NoFailure_AndNoReadmeInPackage(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"ZodSharp.SystemTextJson",
			namespacePrefix: "ZodSharp",
			extraProps: OfflinePackProps
				+ "<EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>",
			cancellationToken: cancellationToken
		);

		// Repo root discoverable, but no README present.
		await File.WriteAllTextAsync(Path.Combine(h.SolutionDirectory, ".git"), string.Empty, cancellationToken);
		await File.WriteAllTextAsync(
			Path.Combine(h.SolutionDirectory, "package.json"),
			/*lang=json,strict*/
			"""{"name": "zodsharp-systemtextjson", "version": "1.0.0"}""",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await PackAsync(h, cancellationToken);
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var packageVersion = ExtractPackVersion(stdOut, "ZodSharp.SystemTextJson");
		var nupkgPath = Path.Combine(h.SolutionDirectory, "feed", $"ZodSharp.SystemTextJson.{packageVersion}.nupkg");
		using var nupkg = await ZipFile.OpenReadAsync(nupkgPath, cancellationToken);
		await Assert.That(nupkg.Entries.Select(entry => entry.FullName)).DoesNotContain("README.md");
	}

	[Test]
	public async Task NonPackable_WebProject_SolutionWidePack_NoWarning_ByDefault(CancellationToken cancellationToken)
	{
		var sharedDir = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));

		try
		{
			using var lib = await ProjectHarness
				.For("ZodSharp.SystemTextJson")
				.WithSolutionDirectory(sharedDir)
				.WithNamespacePrefix("ZodSharp")
				.AddPropertyRaw(OfflinePackProps)
				.BuildAsync(cancellationToken);

			using var web = await ProjectHarness
				.For("ZodSharp.Web")
				.WithSolutionDirectory(sharedDir)
				.WithNamespacePrefix("ZodSharp")
				.WithSdk("Microsoft.NET.Sdk.Web")
				.AddPropertyRaw(
					"<DisableSourceLink>true</DisableSourceLink><ExcludePurviewTelemetry>true</ExcludePurviewTelemetry><ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>"
				)
				.BuildAsync(cancellationToken);

			await File.WriteAllTextAsync(
				Path.Combine(web.ProjectDirectory, "Program.cs"),
				"public class Program { public static void Main() { } }\n",
				cancellationToken
			);
			await WriteValidSolutionAsync(sharedDir, cancellationToken);

			var feedDirectory = Path.Combine(sharedDir, "feed");
			var solutionPath = Path.Combine(sharedDir, "TestingSolution.slnx");
			var (exitCode, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"pack \"{solutionPath}\" -c Release -o \"{feedDirectory}\"",
				sharedDir,
				cancellationToken
			);

			await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
			await Assert
				.That(stdOut + stdErr)
				.DoesNotContain("cannot be packaged because packaging has been disabled")
				.Because("Non-packable projects must not warn by default during solution-wide pack.");
			await Assert.That(Directory.GetFiles(feedDirectory, "ZodSharp.SystemTextJson.*.nupkg")).IsNotEmpty();
			await Assert
				.That(Directory.GetFiles(feedDirectory, "ZodSharp.Web.*"))
				.IsEmpty()
				.Because("The non-packable web project must not produce a package.");
		}
		finally
		{
			if (Directory.Exists(sharedDir))
				Directory.Delete(sharedDir, recursive: true);
		}
	}

	[Test]
	public async Task NonPackable_WebProject_ExplicitWarnOnPackingNonPackableProject_EmitsWarning(
		CancellationToken cancellationToken
	)
	{
		var sharedDir = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));

		try
		{
			using var lib = await ProjectHarness
				.For("ZodSharp.SystemTextJson")
				.WithSolutionDirectory(sharedDir)
				.WithNamespacePrefix("ZodSharp")
				.AddPropertyRaw(OfflinePackProps)
				.BuildAsync(cancellationToken);

			using var web = await ProjectHarness
				.For("ZodSharp.Web")
				.WithSolutionDirectory(sharedDir)
				.WithNamespacePrefix("ZodSharp")
				.WithSdk("Microsoft.NET.Sdk.Web")
				.AddPropertyRaw(
					"<WarnOnPackingNonPackableProject>true</WarnOnPackingNonPackableProject><DisableSourceLink>true</DisableSourceLink><ExcludePurviewTelemetry>true</ExcludePurviewTelemetry><ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>"
				)
				.BuildAsync(cancellationToken);

			await File.WriteAllTextAsync(
				Path.Combine(web.ProjectDirectory, "Program.cs"),
				"public class Program { public static void Main() { } }\n",
				cancellationToken
			);
			await WriteValidSolutionAsync(sharedDir, cancellationToken);

			var feedDirectory = Path.Combine(sharedDir, "feed");
			var solutionPath = Path.Combine(sharedDir, "TestingSolution.slnx");
			var (exitCode, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"pack \"{solutionPath}\" -c Release -o \"{feedDirectory}\"",
				sharedDir,
				cancellationToken
			);

			await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
			await Assert
				.That(stdOut + stdErr)
				.Contains("cannot be packaged because packaging has been disabled")
				.Because("Explicit opt-in to WarnOnPackingNonPackableProject must surface the warning.");
		}
		finally
		{
			if (Directory.Exists(sharedDir))
				Directory.Delete(sharedDir, recursive: true);
		}
	}

	/// <summary>
	/// Writes a valid .slnx containing the two projects. The harness's own solution file uses a
	/// bare &lt;File&gt; element that solution tooling does not recognise for build/pack operations.
	/// </summary>
	static async Task WriteValidSolutionAsync(string solutionDirectory, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(
			Path.Combine(solutionDirectory, "TestingSolution.slnx"),
			"""
			<Solution>
				<Folder Name="/src/">
					<Project Path="ZodSharp.SystemTextJson/ZodSharp.SystemTextJson.csproj" />
					<Project Path="ZodSharp.Web/ZodSharp.Web.csproj" />
				</Folder>
			</Solution>
			""",
			cancellationToken
		);
	}

	static async Task<(int Code, string StdOut, string StdErr)> PackAsync(
		ProjectHarness harness,
		CancellationToken cancellationToken
	)
	{
		var feedDirectory = Path.Combine(harness.SolutionDirectory, "feed");
		return await RunProcessAsync(
			"dotnet",
			$"pack \"{harness.ProjectFilePath}\" -c Release -o \"{feedDirectory}\"",
			harness.SolutionDirectory,
			cancellationToken
		);
	}

	static string ExtractPackVersion(string stdOut, string packageId)
	{
		var match = System.Text.RegularExpressions.Regex.Match(
			stdOut,
			$"Successfully created package '[^']*{System.Text.RegularExpressions.Regex.Escape(packageId)}\\.([^']+)\\.nupkg'"
		);

		return match.Success ? match.Groups[1].Value : string.Empty;
	}

	static async Task<(int Code, string StdOut, string StdErr)> RunProcessAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken
	)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		ProjectHarness.IsolateFromHostEnvironment(
			process.StartInfo.Environment,
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		);

		process.Start();
		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdoutTask, await stderrTask);
	}
}
