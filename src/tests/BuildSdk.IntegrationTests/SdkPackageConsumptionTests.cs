using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Proves the packed SDK can be consumed from a local NuGet feed and that its .editorconfig
/// is resolved and applied as an EditorConfigFiles entry in a fresh consumer project.
/// </summary>
public sealed class SdkPackageConsumptionTests
{
	// Both tests in this class build the shared BuildSdk/Analyzers project in place via `dotnet pack`;
	// serialize them to avoid concurrent CSC file-lock conflicts on the same obj/bin outputs.
	[Test]
	[NotInParallel]
	public async Task PackedSdk_Exposes_EditorConfig_To_NewConsumerProject(CancellationToken cancellationToken)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), $"PurviewSdkPackageConsumption-{Guid.NewGuid():N}");
		var feedDirectory = Path.Combine(tempRoot, "feed");
		var consumerDirectory = Path.Combine(tempRoot, "consumer");
		var consumerSrcDirectory = Path.Combine(consumerDirectory, "src");

		Directory.CreateDirectory(feedDirectory);
		Directory.CreateDirectory(consumerDirectory);
		Directory.CreateDirectory(consumerSrcDirectory);

		try
		{
			var packageVersion = await PackSdkAsync(feedDirectory, cancellationToken);
			await VerifyPackageContainsEditorConfigAsync(feedDirectory, packageVersion, cancellationToken);
			await SetupConsumerProjectAsync(consumerDirectory, consumerSrcDirectory, cancellationToken);
			await WriteConfigurationFilesAsync(
				consumerDirectory,
				consumerSrcDirectory,
				feedDirectory,
				packageVersion,
				cancellationToken
			);
			await VerifyEditorConfigIntegrationAsync(consumerDirectory, cancellationToken);
			await VerifyAdditionalFeaturesAsync(consumerDirectory, cancellationToken);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}

	[Test]
	[NotInParallel]
	public async Task PackedSdk_CopiesBundledAgentFoldersFromOtherReferencedPackages(
		CancellationToken cancellationToken
	)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), $"PurviewSdkAgentFolderRelay-{Guid.NewGuid():N}");
		var feedDirectory = Path.Combine(tempRoot, "feed");
		var consumerDirectory = Path.Combine(tempRoot, "consumer");
		var consumerSrcDirectory = Path.Combine(consumerDirectory, "src");

		Directory.CreateDirectory(feedDirectory);
		Directory.CreateDirectory(consumerDirectory);
		Directory.CreateDirectory(consumerSrcDirectory);

		try
		{
			var sdkPackageVersion = await PackSdkAsync(feedDirectory, cancellationToken);
			var otherPackageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";

			var (code, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				"new sln -n Proof",
				consumerDirectory,
				cancellationToken
			);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			(code, stdOut, stdErr) = await RunProcessAsync("git", "init", consumerDirectory, cancellationToken);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			(code, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"new classlib -n Proof.OtherPackage -o \"{Path.Combine(consumerSrcDirectory, "Proof.OtherPackage")}\" -f net10.0",
				consumerDirectory,
				cancellationToken
			);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			(code, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"new classlib -n Proof.LibTest -o \"{Path.Combine(consumerSrcDirectory, "Proof.LibTest")}\" -f net10.0",
				consumerDirectory,
				cancellationToken
			);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			var solutionPath = Directory
				.GetFiles(consumerDirectory, "Proof.sln*", SearchOption.TopDirectoryOnly)
				.First();

			foreach (var projectName in new[] { "Proof.OtherPackage", "Proof.LibTest" })
			{
				(code, stdOut, stdErr) = await RunProcessAsync(
					"dotnet",
					$"sln \"{solutionPath}\" add \"{Path.Combine(consumerSrcDirectory, projectName, $"{projectName}.csproj")}\"",
					consumerDirectory,
					cancellationToken
				);
				await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
			}

			await File.WriteAllTextAsync(
				Path.Combine(consumerDirectory, "package.json"), /*lang=json,strict*/
				"""{"name": "proof-consumer", "version": "1.0.0"}""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(consumerDirectory, "Directory.Packages.props"),
				$"""
				<Project>
					<PropertyGroup>
						<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
					</PropertyGroup>
					<ItemGroup>
						<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
						<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
						<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />					<PackageVersion Include="TUnit" Version="*" />
					<PackageVersion Include="TUnit.Mocks" Version="*" />
					<PackageVersion Include="Bogus" Version="*" />						<PackageVersion Include="Proof.OtherPackage" Version="{otherPackageVersion}" />
					</ItemGroup>
				</Project>
				""",
				cancellationToken
			);

			var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");
			await File.WriteAllTextAsync(
				nugetConfigPath,
				$"""
				<?xml version="1.0" encoding="utf-8"?>
				<configuration>
					<packageSources>
						<clear />
						<add key="local" value="{feedDirectory.Replace('\\', '/')}" />
						<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
					</packageSources>
					<packageSourceMapping>
						<clear />
						<packageSource key="local">
							<package pattern="Purview.BuildSdk" />
							<package pattern="Proof.OtherPackage" />
						</packageSource>
						<packageSource key="nuget.org">
							<package pattern="*" />
						</packageSource>
					</packageSourceMapping>
				</configuration>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(consumerSrcDirectory, "Directory.Build.props"),
				$"""
				<Project>
					<PropertyGroup>
						<NamespacePrefix>Proof</NamespacePrefix>
					</PropertyGroup>
					<Import Sdk="Purview.BuildSdk" Project="Sdk.props" Version="{sdkPackageVersion}" />
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				Path.Combine(consumerSrcDirectory, "Directory.Build.targets"),
				$"""
				<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
					<Import Sdk="Purview.BuildSdk" Project="Sdk.targets" Version="{sdkPackageVersion}" />
				</Project>
				""",
				cancellationToken
			);

			var otherPackageDirectory = Path.Combine(consumerSrcDirectory, "Proof.OtherPackage");
			await File.WriteAllTextAsync(
				Path.Combine(otherPackageDirectory, "Proof.OtherPackage.csproj"),
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TargetFramework>net10.0</TargetFramework>
						<IsPackable>true</IsPackable>
					</PropertyGroup>
				</Project>
				""",
				cancellationToken
			);

			var otherPackageAgentDirectory = Path.Combine(otherPackageDirectory, "Sdk", ".agents", "agents");
			Directory.CreateDirectory(otherPackageAgentDirectory);
			await File.WriteAllTextAsync(
				Path.Combine(otherPackageAgentDirectory, "other-package-agent.md"),
				"# Other package agent\n",
				cancellationToken
			);

			(code, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"pack \"{Path.Combine(otherPackageDirectory, "Proof.OtherPackage.csproj")}\" -c Release -o \"{feedDirectory}\" "
					+ $"-p:RestoreConfigFile=\"{nugetConfigPath}\" -p:PackageVersion={otherPackageVersion} -p:Version={otherPackageVersion} -p:NoWarn=NU1010",
				consumerDirectory,
				cancellationToken
			);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			var libTestProjectPath = Path.Combine(consumerSrcDirectory, "Proof.LibTest", "Proof.LibTest.csproj");
			var libTestProjectContent = await File.ReadAllTextAsync(libTestProjectPath, cancellationToken);
			libTestProjectContent = libTestProjectContent.Replace(
				"</Project>",
				"""
					<ItemGroup>
						<PackageReference Include="Proof.OtherPackage" />
					</ItemGroup>
				</Project>
				""",
				StringComparison.Ordinal
			);
			await File.WriteAllTextAsync(libTestProjectPath, libTestProjectContent, cancellationToken);

			(code, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"build \"{libTestProjectPath}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -p:CentralPackageFloatingVersionsEnabled=true -p:NoWarn=NU1010",
				consumerDirectory,
				cancellationToken
			);
			await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

			var relayedAgentPath = Path.Combine(consumerDirectory, ".agents", "agents", "other-package-agent.md");
			await Assert
				.That(File.Exists(relayedAgentPath))
				.IsTrue()
				.Because(
					$"Agent bundled by the referenced Proof.OtherPackage package was not relayed to {relayedAgentPath}."
				);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}

	[Test]
	[NotInParallel]
	public async Task PackedSdk_ShipsAnalyzers_AndExposesThemToConsumerProject(CancellationToken cancellationToken)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), $"PurviewSdkAnalyzerDelivery-{Guid.NewGuid():N}");
		var feedDirectory = Path.Combine(tempRoot, "feed");
		var consumerDirectory = Path.Combine(tempRoot, "consumer");
		var consumerSrcDirectory = Path.Combine(consumerDirectory, "src");

		Directory.CreateDirectory(feedDirectory);
		Directory.CreateDirectory(consumerDirectory);
		Directory.CreateDirectory(consumerSrcDirectory);

		try
		{
			var packageVersion = await PackSdkAsync(feedDirectory, cancellationToken);
			await VerifyPackageContainsAnalyzersAsync(feedDirectory, packageVersion, cancellationToken);
			await SetupConsumerProjectAsync(consumerDirectory, consumerSrcDirectory, cancellationToken);
			await WriteConfigurationFilesAsync(
				consumerDirectory,
				consumerSrcDirectory,
				feedDirectory,
				packageVersion,
				cancellationToken
			);
			await WritePds0003TriggerSourceAsync(consumerSrcDirectory, cancellationToken);
			await VerifyAnalyzerItemExposedAsync(consumerDirectory, cancellationToken);
			await VerifyPds0003WarningOnBuildAsync(consumerDirectory, cancellationToken);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}

	[Test]
	[NotInParallel]
	public async Task PackedSdk_ExposesCodeFixAssemblyOnlyInsideVisualStudio(CancellationToken cancellationToken)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), $"PurviewSdkCodeFixDiscovery-{Guid.NewGuid():N}");
		var feedDirectory = Path.Combine(tempRoot, "feed");
		var consumerDirectory = Path.Combine(tempRoot, "consumer");
		var consumerSrcDirectory = Path.Combine(consumerDirectory, "src");

		Directory.CreateDirectory(feedDirectory);
		Directory.CreateDirectory(consumerDirectory);
		Directory.CreateDirectory(consumerSrcDirectory);

		try
		{
			var packageVersion = await PackSdkAsync(feedDirectory, cancellationToken);
			await SetupConsumerProjectAsync(consumerDirectory, consumerSrcDirectory, cancellationToken);
			await WriteConfigurationFilesAsync(
				consumerDirectory,
				consumerSrcDirectory,
				feedDirectory,
				packageVersion,
				cancellationToken
			);

			var cliAnalyzerPaths = await GetAnalyzerItemPathsAsync(
				consumerDirectory,
				extraMsbuildArgs: "",
				cancellationToken
			);
			await Assert
				.That(
					cliAnalyzerPaths.Any(path =>
						path.EndsWith("Purview.BuildSdk.CodeFixers.dll", StringComparison.OrdinalIgnoreCase)
					)
				)
				.IsFalse()
				.Because(
					$"The code-fix assembly must not be an Analyzer item in command-line builds (its Workspaces dependency would emit CS8032).{Environment.NewLine}Analyzers: {string.Join(", ", cliAnalyzerPaths)}"
				);

			var vsAnalyzerPaths = await GetAnalyzerItemPathsAsync(
				consumerDirectory,
				extraMsbuildArgs: "-p:BuildingInsideVisualStudio=true",
				cancellationToken
			);
			await Assert
				.That(
					vsAnalyzerPaths.Any(path =>
						path.EndsWith("Purview.BuildSdk.CodeFixers.dll", StringComparison.OrdinalIgnoreCase)
					)
				)
				.IsTrue()
				.Because(
					$"The code-fix assembly must be an Analyzer item inside Visual Studio so its code fixes are discovered.{Environment.NewLine}Analyzers: {string.Join(", ", vsAnalyzerPaths)}"
				);
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}

	static async Task VerifyPackageContainsAnalyzersAsync(
		string feedDirectory,
		string packageVersion,
		CancellationToken cancellationToken
	)
	{
		var packagePath = Directory
			.GetFiles(feedDirectory, $"Purview.BuildSdk.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert
			.That(packagePath)
			.IsNotNull()
			.Because($"The package {packageVersion} was not found in the feed directory.");

		using (var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken))
		{
			var entries = zip.Entries.Select(entry => entry.FullName).ToList();
			await Assert
				.That(entries)
				.Contains("analyzers/dotnet/cs/Purview.BuildSdk.Analyzers.dll")
				.Because("The analyzer assembly must ship in the SDK package under analyzers/dotnet/cs.");
			await Assert
				.That(entries)
				.Contains("analyzers/dotnet/cs/Purview.BuildSdk.CodeFixers.dll")
				.Because("The code-fix assembly must ship beside the analyzer for IDE discovery.");
		}
	}

	static async Task WritePds0003TriggerSourceAsync(string consumerSrcDirectory, CancellationToken cancellationToken)
	{
		var classPath = Path.Combine(consumerSrcDirectory, "Proof.LibTest", "Class1.cs");
		await File.WriteAllTextAsync(
			classPath,
			"""
			public class Class1
			{
				public void M()
				{
					var x = new object();
					_ = x;
				}
			}
			""",
			cancellationToken
		);
	}

	static async Task VerifyAnalyzerItemExposedAsync(string consumerDirectory, CancellationToken cancellationToken)
	{
		var analyzerPaths = await GetAnalyzerItemPathsAsync(consumerDirectory, extraMsbuildArgs: "", cancellationToken);

		await Assert
			.That(
				analyzerPaths.Any(path =>
					path.EndsWith("Purview.BuildSdk.Analyzers.dll", StringComparison.OrdinalIgnoreCase)
				)
			)
			.IsTrue()
			.Because(
				$"The SDK analyzer must be exposed as an Analyzer item to the consumer project.{Environment.NewLine}Analyzers: {string.Join(", ", analyzerPaths)}"
			);
	}

	static async Task<string[]> GetAnalyzerItemPathsAsync(
		string consumerDirectory,
		string extraMsbuildArgs,
		CancellationToken cancellationToken
	)
	{
		var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");

		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -noconlog -p:RestoreConfigFile=\"{nugetConfigPath}\" -getItem:Analyzer -p:CentralPackageFloatingVersionsEnabled=true {extraMsbuildArgs}",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var evaluationJsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		await Assert.That(evaluationJsonStart >= 0).IsTrue();
		var evaluationJson = stdOut[evaluationJsonStart..];

		using var doc = JsonDocument.Parse(evaluationJson);
		return
		[
			.. doc
				.RootElement.GetProperty("Items")
				.GetProperty("Analyzer")
				.EnumerateArray()
				.Select(item => item.GetProperty("Identity").GetString())
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Select(path => Path.GetFullPath(path!)),
		];
	}

	static async Task VerifyPds0003WarningOnBuildAsync(string consumerDirectory, CancellationToken cancellationToken)
	{
		var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");

		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"build \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -p:CentralPackageFloatingVersionsEnabled=true -p:NoWarn=NU1010",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert
			.That(stdOut + stdErr)
			.Contains("PDS0003")
			.Because("A shipped analyzer rule (PDS0003) must surface as a warning in the consumer build.");
	}

	static async Task<string> PackSdkAsync(string feedDirectory, CancellationToken cancellationToken)
	{
		var sdkProjectPath = Path.GetFullPath(Path.Combine(SdkPaths.SdkDirectory, "..", "BuildSdk.csproj"));
		var sdkProjectDirectory =
			Path.GetDirectoryName(sdkProjectPath)
			?? throw new InvalidOperationException("Unable to determine SDK project directory.");

		var packageVersion = $"0.0.0-integration-test-{Guid.NewGuid():N}";
		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{sdkProjectPath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion={packageVersion} -p:Version={packageVersion}",
			sdkProjectDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		return packageVersion;
	}

	static async Task VerifyPackageContainsEditorConfigAsync(
		string feedDirectory,
		string packageVersion,
		CancellationToken cancellationToken
	)
	{
		var packagePath = Directory
			.GetFiles(feedDirectory, $"Purview.BuildSdk.{packageVersion}.nupkg", SearchOption.TopDirectoryOnly)
			.SingleOrDefault();

		await Assert
			.That(packagePath)
			.IsNotNull()
			.Because($"The package {packageVersion} was not found in the feed directory.");

		using (var zip = await ZipFile.OpenReadAsync(packagePath!, cancellationToken))
		{
			var editorConfigEntry = zip.Entries.Single(entry => entry.FullName == "Sdk/.editorconfig");
			await Assert
				.That(editorConfigEntry)
				.IsNotNull()
				.Because("The .editorconfig file is missing in the SDK package.");

			await using var editorConfigStream = await editorConfigEntry!.OpenAsync(cancellationToken);
			using var reader = new StreamReader(editorConfigStream);
			var editorConfig = await reader.ReadToEndAsync(cancellationToken);

			await Assert
				.That(editorConfig)
				.Contains("[**/Migrations/**.{cs,vb}]")
				.Because("The packed .editorconfig must ship the EF Core Migrations section.");
			await Assert
				.That(editorConfig)
				.Contains("generated_code = true")
				.Because(
					"Generated content (Migrations, .g.cs, Generated, obj/bin) must be treated as generated code."
				);
			await Assert
				.That(editorConfig)
				.Contains("[**/Extensions/**.{cs,vb}]")
				.Because("The packed .editorconfig must ship the Extensions suppression section.");
			await Assert
				.That(editorConfig)
				.Contains("dotnet_diagnostic.CA1724.severity = none")
				.Because(
					"The Extensions section must suppress namespace-conflict diagnostics so no pragmas are required."
				);
		}
	}

	static async Task SetupConsumerProjectAsync(
		string consumerDirectory,
		string consumerSrcDirectory,
		CancellationToken cancellationToken
	)
	{
		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			"new sln -n Proof",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		(code, stdOut, stdErr) = await RunProcessAsync("git", "init", consumerDirectory, cancellationToken);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"new classlib -n Proof.LibTest -o \"{Path.Combine(consumerSrcDirectory, "Proof.LibTest")}\" -f net10.0",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var solutionPath = Directory
			.GetFiles(consumerDirectory, "Proof.sln*", SearchOption.TopDirectoryOnly)
			.FirstOrDefault();

		await Assert
			.That(solutionPath)
			.IsNotNull()
			.Because("The generated solution file was not found in the consumer directory.");

		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"sln \"{solutionPath}\" add \"{Path.Combine(consumerSrcDirectory, "Proof.LibTest", "Proof.LibTest.csproj")}\"",
			consumerDirectory,
			cancellationToken
		);

		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	static async Task WriteConfigurationFilesAsync(
		string consumerDirectory,
		string consumerSrcDirectory,
		string feedDirectory,
		string packageVersion,
		CancellationToken cancellationToken
	)
	{
		await File.WriteAllTextAsync(
			Path.Combine(consumerDirectory, "package.json"), /*lang=json,strict*/
			"""{"name": "proof-consumer", "version": "1.0.0"}""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(consumerDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="*" />
					<PackageVersion Include="TUnit" Version="*" />
					<PackageVersion Include="TUnit.Mocks" Version="*" />
					<PackageVersion Include="Bogus" Version="*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(consumerDirectory, "NuGet.Config"),
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<configuration>
				<packageSources>
					<clear />
					<add key="local" value="{feedDirectory.Replace('\\', '/')}" />
					<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
				</packageSources>
				<packageSourceMapping>
					<clear />
					<packageSource key="local">
						<package pattern="Purview.BuildSdk" />
					</packageSource>
					<packageSource key="nuget.org">
						<package pattern="*" />
					</packageSource>
				</packageSourceMapping>
			</configuration>
			""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(consumerSrcDirectory, "Directory.Build.props"),
			$"""
			<Project>
				<PropertyGroup>
					<NamespacePrefix>Proof</NamespacePrefix>
				</PropertyGroup>
				<Import Sdk="Purview.BuildSdk" Project="Sdk.props" Version="{packageVersion}" />
			</Project>
			""",
			cancellationToken
		);

		await File.WriteAllTextAsync(
			Path.Combine(consumerSrcDirectory, "Directory.Build.targets"),
			$"""
			<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
				<Import Sdk="Purview.BuildSdk" Project="Sdk.targets" Version="{packageVersion}" />
			</Project>
			""",
			cancellationToken
		);
	}

	static async Task VerifyEditorConfigIntegrationAsync(string consumerDirectory, CancellationToken cancellationToken)
	{
		var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");

		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -noconlog -p:RestoreConfigFile=\"{nugetConfigPath}\" -getProperty:EditorConfigFilePath -getItem:EditorConfigFiles -p:CentralPackageFloatingVersionsEnabled=true",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var evaluationJsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		await Assert.That(evaluationJsonStart >= 0).IsTrue();
		var evaluationJson = stdOut[evaluationJsonStart..];

		using var doc = JsonDocument.Parse(evaluationJson);
		var editorConfigPath = doc
			.RootElement.GetProperty("Properties")
			.GetProperty("EditorConfigFilePath")
			.GetString();

		await Assert
			.That(string.IsNullOrWhiteSpace(editorConfigPath))
			.IsFalse()
			.Because("EditorConfigFilePath property is missing or empty.");
		await Assert
			.That(File.Exists(editorConfigPath!))
			.IsTrue()
			.Because($"EditorConfig file not found at path: {editorConfigPath}");

		var itemPaths = doc
			.RootElement.GetProperty("Items")
			.GetProperty("EditorConfigFiles")
			.EnumerateArray()
			.Select(item => item.GetProperty("Identity").GetString())
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Select(path => Path.GetFullPath(path!).TrimEnd('\\', '/'))
			.ToArray();

		var normalizedEditorConfigPath = Path.GetFullPath(editorConfigPath!).TrimEnd('\\', '/');
		await Assert
			.That(
				itemPaths.Any(path =>
					string.Equals(path, normalizedEditorConfigPath, StringComparison.OrdinalIgnoreCase)
				)
			)
			.IsTrue();

		var editorConfigContent = await File.ReadAllTextAsync(editorConfigPath!, cancellationToken);
		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"build \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -p:CentralPackageFloatingVersionsEnabled=true -p:NoWarn=NU1010",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	static async Task VerifyAdditionalFeaturesAsync(string consumerDirectory, CancellationToken cancellationToken)
	{
		var nugetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");

		var bundledSkillPath = Path.Combine(
			consumerDirectory,
			".agents",
			"skills",
			"sdk-configuration-reference",
			"SKILL.md"
		);
		await Assert
			.That(File.Exists(bundledSkillPath))
			.IsTrue()
			.Because($"Bundled skill not found at {bundledSkillPath}.");

		var bundledAgentPath = Path.Combine(consumerDirectory, ".agents", "agents", "sdk-consumer-setup.md");
		await Assert
			.That(File.Exists(bundledAgentPath))
			.IsTrue()
			.Because($"Bundled agent not found at {bundledAgentPath}.");

		var bundledPromptPath = Path.Combine(
			consumerDirectory,
			".agents",
			"prompts",
			"sdk-diagnose-agent-folder-copy.md"
		);
		await Assert
			.That(File.Exists(bundledPromptPath))
			.IsTrue()
			.Because($"Bundled prompt not found at {bundledPromptPath}.");

		var (code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -t:EnsureRepositoryEditorConfigTarget -getProperty:RepositoryEditorConfigFilePath",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var repositoryEditorConfigPath = stdOut.Trim();
		await Assert
			.That(string.IsNullOrWhiteSpace(repositoryEditorConfigPath))
			.IsFalse()
			.Because("RepositoryEditorConfigFilePath property is missing or empty.");
		repositoryEditorConfigPath = Path.GetFullPath(repositoryEditorConfigPath);
		await Assert
			.That(repositoryEditorConfigPath)
			.IsEqualTo(Path.Combine(consumerDirectory, ".editorconfig"))
			.Because("RepositoryEditorConfigFilePath does not match the expected path.");

		await Assert
			.That(File.Exists(repositoryEditorConfigPath))
			.IsTrue()
			.Because($"Repository EditorConfig file not found at path: {repositoryEditorConfigPath}");

		File.Delete(repositoryEditorConfigPath);
		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -p:BootstrapEditorConfigToRepoRoot=false -t:EnsureRepositoryEditorConfigTarget",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert
			.That(File.Exists(repositoryEditorConfigPath))
			.IsFalse()
			.Because("BootstrapEditorConfigToRepoRoot=false should disable the physical repo-level copy.");

		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"msbuild \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -t:EnsureRepositoryGlobalJsonTarget -getProperty:RepositoryGlobalJsonFilePath",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var repositoryGlobalJsonPath = stdOut.Trim();
		await Assert
			.That(string.IsNullOrWhiteSpace(repositoryGlobalJsonPath))
			.IsFalse()
			.Because("RepositoryGlobalJsonFilePath property is missing or empty.");
		repositoryGlobalJsonPath = Path.GetFullPath(repositoryGlobalJsonPath);
		await Assert
			.That(repositoryGlobalJsonPath)
			.IsEqualTo(Path.Combine(consumerDirectory, "global.json"))
			.Because("RepositoryGlobalJsonFilePath does not match the expected path.");
		await Assert
			.That(File.Exists(repositoryGlobalJsonPath))
			.IsTrue()
			.Because($"Repository GlobalJson file not found at path: {repositoryGlobalJsonPath}");

		var repositoryGlobalJsonContent = await File.ReadAllTextAsync(repositoryGlobalJsonPath, cancellationToken);
		await Assert
			.That(repositoryGlobalJsonContent)
			.Contains("\"runner\": \"Microsoft.Testing.Platform\"")
			.Because(
				$"Repository GlobalJson file at path {repositoryGlobalJsonPath} does not contain the expected content."
			);
		await Assert
			.That(repositoryGlobalJsonContent)
			.Contains("\"Purview.BuildSdk\"")
			.Because(
				$"Repository GlobalJson file at path {repositoryGlobalJsonPath} does not contain the expected content."
			);

		(code, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"build \"{Path.Combine("src", "Proof.LibTest", "Proof.LibTest.csproj")}\" -nologo -p:RestoreConfigFile=\"{nugetConfigPath}\" -p:AgentPackDestinationFolder=.custom-agents",
			consumerDirectory,
			cancellationToken
		);
		await Assert.That(code).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));

		var customDestinationSkillPath = Path.Combine(
			consumerDirectory,
			".custom-agents",
			"skills",
			"sdk-configuration-reference",
			"SKILL.md"
		);
		await Assert.That(File.Exists(customDestinationSkillPath)).IsTrue();
		var customDestinationGitIgnorePath = Path.Combine(
			consumerDirectory,
			".custom-agents",
			"skills",
			"sdk-configuration-reference",
			".gitignore"
		);
		await Assert.That(File.Exists(customDestinationGitIgnorePath)).IsTrue();

		var customDestinationAgentPath = Path.Combine(
			consumerDirectory,
			".custom-agents",
			"agents",
			"sdk-consumer-setup.md"
		);
		await Assert.That(File.Exists(customDestinationAgentPath)).IsTrue();

		var customDestinationPromptPath = Path.Combine(
			consumerDirectory,
			".custom-agents",
			"prompts",
			"sdk-diagnose-agent-folder-copy.md"
		);
		await Assert.That(File.Exists(customDestinationPromptPath)).IsTrue();
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

		// Isolate the throwaway consumer from the host CI environment so repo-root discovery
		// (and therefore the .agents copy destination) resolves inside the consumer directory
		// rather than GITHUB_WORKSPACE on CI runners.
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
