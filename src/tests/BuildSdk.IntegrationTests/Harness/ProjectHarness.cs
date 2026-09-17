using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Purview.BuildSdk.Harness;

/// <summary>
/// Creates throwaway consumer projects on disk that import the SDK from source,
/// allowing integration tests to invoke MSBuild and assert on observable build behaviour.
/// </summary>
partial class ProjectHarness : IDisposable
{
	readonly bool _ownsWorkDir;
	readonly IReadOnlyDictionary<string, string> _extraEnv;
	bool _disposedValue;

	public string ProjectName { get; }

	public string ProjectDirectory { get; }

	public string ProjectFilePath { get; }

	public string SolutionDirectory { get; }

	internal ProjectHarness(
		string workDir,
		string projectName,
		IReadOnlyDictionary<string, string>? extraEnv = null,
		bool ownsWorkDir = true
	)
	{
		SolutionDirectory = workDir;
		_ownsWorkDir = ownsWorkDir;
		ProjectName = projectName;
		ProjectDirectory = Path.Combine(workDir, projectName);
		ProjectFilePath = Path.Combine(ProjectDirectory, $"{projectName}.csproj");
		_extraEnv = extraEnv ?? ImmutableDictionary<string, string>.Empty;
	}

	public static ProjectHarnessBuilder For(string projectName) => new(projectName);

	/// <summary>
	/// Creates a standard SDK-style consumer project.
	/// </summary>
	public static async Task<ProjectHarness> CreateAsync(
		string projectName,
		string sdk = "Microsoft.NET.Sdk",
		string targetFramework = "net10.0",
		string namespacePrefix = "Test",
		bool withDockerfile = false,
		string? extraProps = null,
		string? extraItems = null,
		IDictionary<string, string>? extraEnv = null,
		string? preImportProps = null,
		CancellationToken cancellationToken = default
	)
	{
		var builder = For(projectName)
			.WithSdk(sdk)
			.WithTargetFramework(targetFramework)
			.WithNamespacePrefix(namespacePrefix);

		if (withDockerfile)
			builder.WithDockerfile();

		if (!string.IsNullOrWhiteSpace(preImportProps))
			builder.WithPreImportPropertiesRaw(preImportProps);

		if (!string.IsNullOrWhiteSpace(extraProps))
			builder.AddPropertyRaw(extraProps);

		if (!string.IsNullOrWhiteSpace(extraItems))
			builder.AddItemRaw(extraItems);

		if (extraEnv is not null)
			builder.WithEnvironmentVariables(extraEnv);

		return await builder.BuildAsync(cancellationToken);
	}

	/// <summary>
	/// Creates a consumer project with fully custom file content.
	/// The directory still gets the standard Directory.Build.props/targets bootstrapping.
	/// </summary>
	public static async Task<ProjectHarness> CreateWithContentAsync(
		string projectName,
		string projectFileContent,
		string namespacePrefix = "Test",
		CancellationToken cancellationToken = default
	)
	{
		return await For(projectName)
			.WithNamespacePrefix(namespacePrefix)
			.WithProjectFileContent(projectFileContent)
			.BuildAsync(cancellationToken);
	}

	internal async Task WriteBoilerplateAsync(
		string namespacePrefix,
		string projectName,
		string? preImportProps,
		CancellationToken cancellationToken
	)
	{
		Directory.CreateDirectory(SolutionDirectory);
		Directory.CreateDirectory(ProjectDirectory);

		var preImportBlock = preImportProps is null
			? ""
			: $"\n\t<PropertyGroup>\n\t\t{preImportProps}\n\t</PropertyGroup>";

		var solutionPath = Path.Combine(SolutionDirectory, "TestingSolution.slnx");
		await CreateOrUpdateSolution(solutionPath, projectName, cancellationToken);

		var directoryBuildPropsPath = Path.Combine(SolutionDirectory, "Directory.Build.props");
		if (!File.Exists(directoryBuildPropsPath))
		{
			await File.WriteAllTextAsync(
				directoryBuildPropsPath,
				$"""
				<Project>
					<PropertyGroup>
						<NamespacePrefix>{namespacePrefix}</NamespacePrefix>
					</PropertyGroup>{preImportBlock}
					<Import Project="{SdkPaths.SdkDirectory}/Sdk.props" />
				</Project>
				""",
				cancellationToken
			);
		}

		var directoryBuildTargetsPath = Path.Combine(SolutionDirectory, "Directory.Build.targets");
		if (!File.Exists(directoryBuildTargetsPath))
		{
			await File.WriteAllTextAsync(
				directoryBuildTargetsPath,
				$"""
				<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
					<Import Project="{SdkPaths.SdkDirectory}/Sdk.targets" />
				</Project>
				""",
				cancellationToken
			);
		}

		var directoryPackagesPropsPath = Path.Combine(SolutionDirectory, "Directory.Packages.props");
		if (!File.Exists(directoryPackagesPropsPath))
		{
			await File.WriteAllTextAsync(
				directoryPackagesPropsPath,
				"""
				<Project>
					<PropertyGroup>
						<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
					</PropertyGroup>
					<ItemGroup>
						<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					</ItemGroup>
				</Project>
				""",
				cancellationToken
			);
		}
	}

	async Task CreateOrUpdateSolution(string solutionPath, string projectName, CancellationToken cancellationToken)
	{
		var projectPath = $"{projectName}/{projectName}.csproj";

		if (File.Exists(solutionPath))
		{
			var document = XDocument.Load(solutionPath);
			var solution =
				document.Root
				?? throw new InvalidOperationException($"The solution file '{solutionPath}' has no root element.");

			var alreadyExists = solution
				.Elements("File")
				.Any(element =>
					string.Equals(element.Attribute("Path")?.Value, projectPath, StringComparison.OrdinalIgnoreCase)
				);

			if (!alreadyExists)
			{
				solution.Add(
					new XText(Environment.NewLine + "\t"),
					new XElement("File", new XAttribute("Path", projectPath)),
					new XText(Environment.NewLine)
				);

				await using var stream = File.Create(solutionPath);
				await document.SaveAsync(stream, SaveOptions.DisableFormatting, cancellationToken);
			}
		}
		else
		{
			await File.WriteAllTextAsync(
				solutionPath,
				$"""
				<Solution>
					<File Path="{projectPath}" />
				</Solution>
				""",
				cancellationToken
			);
		}
	}

	/// <summary>
	/// Evaluates one or more MSBuild properties via <c>dotnet msbuild -getProperty</c>
	/// without triggering a build or package restore.
	/// </summary>
	public async Task<IReadOnlyDictionary<string, string>> GetPropertiesAsync(
		CancellationToken cancellationToken,
		params string[] propertyNames
	)
	{
		if (propertyNames.Length == 0)
			return ImmutableDictionary<string, string>.Empty;

		var propList = string.Join(",", propertyNames);
		var args = $"msbuild \"{ProjectFilePath}\" -nologo -noconlog -getProperty:{propList}";

		var (exitCode, stdOut, stdErr) = await RunAsync("dotnet", args, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(stdErr ?? "No error returned");

		stdOut = stdOut.Trim();

		// -getProperty outputs plain text for a single property, JSON for multiple.
		var jsonStart = stdOut.IndexOf('{', StringComparison.Ordinal);
		if (jsonStart < 0)
		{
			// Single property — plain text value.
			return propertyNames.Length == 1
				? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [propertyNames[0]] = stdOut }
				: propertyNames.ToDictionary(p => p, _ => "", StringComparer.OrdinalIgnoreCase);
		}

		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using var doc = JsonDocument.Parse(stdOut[jsonStart..]);
			if (doc.RootElement.TryGetProperty("Properties", out var propsEl))
			{
				foreach (var prop in propsEl.EnumerateObject())
				{
					var value = prop.Value.GetString();
					result[prop.Name] = value ?? string.Empty;
				}
			}
		}
		catch (JsonException)
		{ /* return what we have */
			throw;
		}

		return result;
	}

	/// <summary>Evaluates a single MSBuild property without building.</summary>
	public async Task<string> GetPropertyAsync(string propertyName, CancellationToken cancellationToken)
	{
		var props = await GetPropertiesAsync(cancellationToken, propertyName);
		return props.TryGetValue(propertyName, out var v) ? v : string.Empty;
	}

	/// <summary>
	/// Evaluates one or more MSBuild properties and item types in a single
	/// <c>dotnet msbuild -getProperty -getItem</c> invocation, returning both
	/// results from one JSON payload. This avoids one process spawn per query.
	/// </summary>
	public async Task<ProjectEvaluation> EvaluateAsync(
		IReadOnlyCollection<string>? propertyNames = null,
		IReadOnlyCollection<string>? itemTypes = null,
		CancellationToken cancellationToken = default
	)
	{
		propertyNames ??= [];
		itemTypes ??= [];

		if (propertyNames.Count == 0 && itemTypes.Count == 0)
			return new(
				ImmutableDictionary<string, string>.Empty,
				ImmutableDictionary<string, IReadOnlyList<string>>.Empty
			);

		var parts = new List<string>(2);
		if (propertyNames.Count > 0)
			parts.Add($"-getProperty:{string.Join(",", propertyNames)}");
		if (itemTypes.Count > 0)
			parts.Add($"-getItem:{string.Join(",", itemTypes)}");

		var args = $"msbuild \"{ProjectFilePath}\" -nologo -noconlog {string.Join(" ", parts)}";
		var (exitCode, stdOut, stdErr) = await RunAsync("dotnet", args, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(stdErr ?? "No error returned");

		var jsonStart = stdOut.Trim().IndexOf('{', StringComparison.Ordinal);
		if (jsonStart < 0)
		{
			// -getProperty emits plain text (not JSON) for a single property.
			if (propertyNames.Count == 1)
			{
				var singleProperty = propertyNames.First();
				return new(
					new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
					{
						[singleProperty] = stdOut.Trim(),
					},
					ImmutableDictionary<string, IReadOnlyList<string>>.Empty
				);
			}

			return new(
				ImmutableDictionary<string, string>.Empty,
				ImmutableDictionary<string, IReadOnlyList<string>>.Empty
			);
		}

		using var doc = JsonDocument.Parse(stdOut[jsonStart..]);
		var root = doc.RootElement;

		var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (root.TryGetProperty("Properties", out var propsEl))
		{
			foreach (var prop in propsEl.EnumerateObject())
				properties[prop.Name] = prop.Value.GetString() ?? string.Empty;
		}

		var items = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
		if (root.TryGetProperty("Items", out var itemsEl))
		{
			foreach (var itemType in itemTypes)
			{
				if (itemsEl.TryGetProperty(itemType, out var typeEl))
				{
					var values = new List<string>();
					foreach (var item in typeEl.EnumerateArray())
					{
						if (item.TryGetProperty("Identity", out var identityValue))
							values.Add(identityValue.GetString() ?? string.Empty);
					}

					items[itemType] = values;
				}
				else
				{
					items[itemType] = [];
				}
			}
		}

		return new(properties, items);
	}

	/// <summary>
	/// Evaluates one or more MSBuild items via <c>dotnet msbuild -getItem</c>
	/// without triggering a build or package restore.
	/// </summary>
	public async Task<IReadOnlyList<string>> GetItemIdentitiesAsync(
		string itemType,
		CancellationToken cancellationToken = default
	)
	{
		return await GetItemValuesAsync(itemType, "Identity", cancellationToken);
	}

	/// <summary>
	/// Evaluates item identities via <c>dotnet msbuild -getItem</c> with additional MSBuild
	/// arguments, for example <c>-p:IsTestProject=false</c> to reproduce the restore-phase
	/// evaluation where dynamic test-package detection has not yet set IsTestProject.
	/// </summary>
	public Task<IReadOnlyList<string>> GetItemIdentitiesAsync(
		string itemType,
		string extraMsBuildArguments,
		CancellationToken cancellationToken = default
	) => GetItemValuesAsync(itemType, "Identity", null, extraMsBuildArguments, cancellationToken);

	public Task<IReadOnlyList<string>> GetProjectReferencesAsync(CancellationToken cancellationToken = default) =>
		GetItemIdentitiesAsync("ProjectReference", cancellationToken);

	public async Task<bool> HasProjectReferenceAsync(
		string projectReferencePath,
		CancellationToken cancellationToken = default
	)
	{
		var references = await GetProjectReferencesAsync(cancellationToken);
		return references.Any(r => string.Equals(r, projectReferencePath, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Evaluates metadata values for a given MSBuild item type.
	/// </summary>
	public async Task<IReadOnlyList<string>> GetItemMetadataValuesAsync(
		string itemType,
		string metadataName,
		CancellationToken cancellationToken = default
	)
	{
		return await GetItemValuesAsync(itemType, metadataName, cancellationToken);
	}

	public async Task<IReadOnlyList<string>> GetItemMetadataValuesAsync(
		string itemType,
		string metadataName,
		string? identity,
		CancellationToken cancellationToken = default
	)
	{
		return await GetItemValuesAsync(itemType, metadataName, identity, null, cancellationToken);
	}

	Task<IReadOnlyList<string>> GetItemValuesAsync(
		string itemType,
		string metadataName,
		CancellationToken cancellationToken
	) => GetItemValuesAsync(itemType, metadataName, null, null, cancellationToken);

	async Task<IReadOnlyList<string>> GetItemValuesAsync(
		string itemType,
		string metadataName,
		string? identity,
		string? extraMsBuildArguments,
		CancellationToken cancellationToken
	)
	{
		var extra = string.IsNullOrWhiteSpace(extraMsBuildArguments)
			? string.Empty
			: $" {extraMsBuildArguments.Trim()}";
		var args = $"msbuild \"{ProjectFilePath}\" -nologo -noconlog{extra} -getItem:{itemType}";
		var (exitCode, stdOut, stdErr) = await RunAsync("dotnet", args, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(stdErr ?? "No error returned");

		var jsonStart = stdOut.Trim().IndexOf('{', StringComparison.Ordinal);
		if (jsonStart < 0)
			return [];

		using var doc = JsonDocument.Parse(stdOut[jsonStart..]);
		if (
			doc.RootElement.TryGetProperty("Items", out var itemsEl) && itemsEl.TryGetProperty(itemType, out var typeEl)
		)
		{
			var values = new List<string>();
			foreach (var item in typeEl.EnumerateArray())
			{
				if (identity != null)
				{
					if (item.TryGetProperty("Identity", out var identityValue))
					{
						if (identity != identityValue.GetString())
							continue;
					}
					else
						continue;
				}

				if (item.TryGetProperty(metadataName, out var metadataValue))
					values.Add(metadataValue.GetString() ?? string.Empty);
			}

			return values;
		}

		return [];
	}

	public async Task<(int Code, string StdOut, string StdErr)> RunMSBuildAsync(
		string msbuildArguments,
		CancellationToken cancellationToken = default
	)
	{
		var args = $"msbuild \"{ProjectFilePath}\" -nologo {msbuildArguments}";
		return await RunAsync("dotnet", args, cancellationToken);
	}

	/// <summary>
	/// Runs a full build of the consumer project.
	/// Pass <paramref name="restore"/>=<c>true</c> when the project references NuGet packages.
	/// </summary>
	public async Task<(bool Success, string Output, string Errors)> BuildAsync(
		bool restore = false,
		bool verbose = false,
		CancellationToken cancellationToken = default
	)
	{
		var restoreFlag = restore ? "" : "--no-restore ";
		var verbosityFlag = verbose ? "-v:detailed" : "-v:quiet";
		var args = $"build \"{ProjectFilePath}\" {restoreFlag}-nologo {verbosityFlag}";
		var (code, stdout, stderr) = await RunAsync("dotnet", args, cancellationToken);
		return (code == 0, stdout, stderr);
	}

	async Task<(int Code, string StdOut, string StdErr)> RunAsync(
		string fileName,
		string arguments,
		CancellationToken cancellationToken
	)
	{
		using Process process = new()
		{
			StartInfo = new()
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = ProjectDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		if (_extraEnv.Count > 0)
		{
			foreach (var (key, value) in _extraEnv)
				process.StartInfo.Environment[key] = value;
		}

		IsolateFromHostEnvironment(process.StartInfo.Environment, _extraEnv);

		process.Start();

		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdoutTask, await stderrTask);
	}

	/// <summary>
	/// Throwaway consumer projects must not inherit the CI runner's environment. Env vars like
	/// <c>GITHUB_WORKSPACE</c> (version detection), <c>GITHUB_ACTIONS</c>/<c>TF_BUILD</c> (CI
	/// detection) and friends would change SDK behaviour and make tests environment-dependent.
	/// They are removed unless the harness explicitly overrides them via <paramref name="overrides"/>.
	/// </summary>
	internal static void IsolateFromHostEnvironment(
		IDictionary<string, string?> environment,
		IReadOnlyDictionary<string, string> overrides
	)
	{
		foreach (var name in CiEnvironmentVariables)
		{
			if (!overrides.ContainsKey(name))
				environment.Remove(name);
		}
	}

	static readonly string[] CiEnvironmentVariables =
	[
		"CI",
		"GITHUB_ACTIONS",
		"GITHUB_WORKSPACE",
		"GITHUB_SHA",
		"GITHUB_REF",
		"GITHUB_HEAD_REF",
		"GITHUB_BASE_REF",
		"TF_BUILD",
		"BUILD_SOURCESDIRECTORY",
		"BUILD_REPOSITORY_LOCALPATH",
		"CI_PROJECT_DIR",
		"RUNNER_OS",
		"RUNNER_ARCH",
		"RUNNER_TEMP",
	];

	public async Task<XDocument> GetPreprocessProjectAsync(CancellationToken cancellationToken)
	{
		var args = $"msbuild \"{ProjectFilePath}\" -nologo -noconlog -preprocess:EvaluatedProject.xml";
		var (exitCode, _, stdErr) = await RunAsync("dotnet", args, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(stdErr ?? "No error returned");

		return XDocument.Load(Path.Combine(ProjectDirectory, "EvaluatedProject.xml"));
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				try
				{
					if (_ownsWorkDir && Directory.Exists(SolutionDirectory))
						Directory.Delete(SolutionDirectory, recursive: true);
				}
				catch (IOException)
				{
					// Best-effort cleanup; don't fail tests on leftover temp files.
				}
			}

			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// The combined result of a single <c>dotnet msbuild -getProperty -getItem</c> evaluation.
/// </summary>
sealed record ProjectEvaluation(
	IReadOnlyDictionary<string, string> Properties,
	IReadOnlyDictionary<string, IReadOnlyList<string>> Items
);
