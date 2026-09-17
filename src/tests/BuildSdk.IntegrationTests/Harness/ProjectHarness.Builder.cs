using System.Text;

namespace Purview.BuildSdk.Harness;

sealed class ProjectHarnessBuilder
{
	static readonly string TempBase = Path.Combine(Path.GetTempPath(), "PurviewSdkTests");

	readonly string _projectName;

	readonly Dictionary<string, string> _environment = [with(StringComparer.OrdinalIgnoreCase)];
	readonly List<(string Name, string Value)> _properties = [];
	readonly List<string> _rawPropertyEntries = [];
	readonly List<ItemDefinition> _items = [];
	readonly List<string> _rawItemEntries = [];

	string _sdk = "Microsoft.NET.Sdk";
	string _targetFramework = "net10.0";
	string _namespacePrefix = "Test";
	bool _withDockerfile;
	string _dockerBaseImage = "mcr.microsoft.com/dotnet/runtime:10.0";
	string? _preImportPropsRaw;
	string? _projectFileContent;
	string? _solutionDirectory;

	internal ProjectHarnessBuilder(string projectName)
	{
		_projectName = projectName;
	}

	public ProjectHarnessBuilder WithSdk(string sdk)
	{
		_sdk = sdk;
		return this;
	}

	public ProjectHarnessBuilder WithTargetFramework(string targetFramework)
	{
		_targetFramework = targetFramework;
		return this;
	}

	public ProjectHarnessBuilder WithNamespacePrefix(string namespacePrefix)
	{
		_namespacePrefix = namespacePrefix;
		return this;
	}

	public ProjectHarnessBuilder WithDockerfile(string baseImage = "mcr.microsoft.com/dotnet/runtime:10.0")
	{
		_withDockerfile = true;
		_dockerBaseImage = baseImage;
		return this;
	}

	public ProjectHarnessBuilder WithEnvironmentVariable(string key, string value)
	{
		_environment[key] = value;
		return this;
	}

	public ProjectHarnessBuilder WithEnvironmentVariables(IDictionary<string, string> values)
	{
		foreach (var (key, value) in values)
			_environment[key] = value;

		return this;
	}

	public ProjectHarnessBuilder WithPreImportProperty(string name, string value)
	{
		var encodedValue = System.Security.SecurityElement.Escape(value) ?? string.Empty;
		return WithPreImportPropertiesRaw($"<{name}>{encodedValue}</{name}>");
	}

	public ProjectHarnessBuilder WithPreImportPropertiesRaw(string rawProperties)
	{
		if (string.IsNullOrWhiteSpace(_preImportPropsRaw))
			_preImportPropsRaw = rawProperties;
		else
			_preImportPropsRaw += Environment.NewLine + rawProperties;

		return this;
	}

	public ProjectHarnessBuilder AddProperty(string name, string value)
	{
		_properties.Add((name, value));
		return this;
	}

	public ProjectHarnessBuilder AddPropertyRaw(string rawPropertyElement)
	{
		_rawPropertyEntries.Add(rawPropertyElement);
		return this;
	}

	public ProjectHarnessBuilder AddItem(
		string itemType,
		string include,
		IReadOnlyDictionary<string, string>? metadata = null
	)
	{
		_items.Add(new ItemDefinition(itemType, include, metadata));
		return this;
	}

	public ProjectHarnessBuilder AddProjectReference(
		string include,
		IReadOnlyDictionary<string, string>? metadata = null
	) => AddItem("ProjectReference", include, metadata);

	public ProjectHarnessBuilder AddItemRaw(string rawItemElement)
	{
		_rawItemEntries.Add(rawItemElement);
		return this;
	}

	public ProjectHarnessBuilder WithProjectFileContent(string projectFileContent)
	{
		_projectFileContent = projectFileContent;
		return this;
	}

	public ProjectHarnessBuilder WithSolutionDirectory(string solutionDirectory)
	{
		_solutionDirectory = solutionDirectory;
		return this;
	}

	public async Task<ProjectHarness> BuildAsync(CancellationToken cancellationToken = default)
	{
		var workDir = string.IsNullOrWhiteSpace(_solutionDirectory)
			? Path.Combine(TempBase, Guid.NewGuid().ToString("N"))
			: _solutionDirectory;

		var ownsWorkDir = string.IsNullOrWhiteSpace(_solutionDirectory);
		ProjectHarness harness = new(
			workDir,
			_projectName,
			new Dictionary<string, string>(_environment, StringComparer.OrdinalIgnoreCase),
			ownsWorkDir
		);

		await harness.WriteBoilerplateAsync(_namespacePrefix, _projectName, _preImportPropsRaw, cancellationToken);

		if (!string.IsNullOrWhiteSpace(_projectFileContent))
		{
			await File.WriteAllTextAsync(harness.ProjectFilePath, _projectFileContent, cancellationToken);
		}
		else
		{
			await File.WriteAllTextAsync(harness.ProjectFilePath, BuildProjectFileContent(), cancellationToken);
		}

		if (_withDockerfile)
		{
			await File.WriteAllTextAsync(
				Path.Combine(harness.ProjectDirectory, "Dockerfile"),
				$"FROM {_dockerBaseImage}",
				cancellationToken
			);
		}

		return harness;
	}

	string BuildProjectFileContent()
	{
		var sb = new StringBuilder();
		sb.AppendLine("<Project Sdk=\"" + _sdk + "\">");
		sb.AppendLine("\t<PropertyGroup>");
		sb.AppendLine("\t\t<TargetFramework>" + _targetFramework + "</TargetFramework>");
		sb.AppendLine("\t</PropertyGroup>");

		if (_properties.Count > 0 || _rawPropertyEntries.Count > 0)
		{
			sb.AppendLine("\t<PropertyGroup>");
			foreach (var (name, value) in _properties)
			{
				var encodedValue = System.Security.SecurityElement.Escape(value) ?? string.Empty;
				sb.AppendLine("\t\t<" + name + ">" + encodedValue + "</" + name + ">");
			}

			foreach (var rawPropertyEntry in _rawPropertyEntries)
				sb.AppendLine("\t\t" + rawPropertyEntry);

			sb.AppendLine("\t</PropertyGroup>");
		}

		if (_items.Count > 0 || _rawItemEntries.Count > 0)
		{
			sb.AppendLine("\t<ItemGroup>");
			foreach (var item in _items)
				sb.AppendLine(item.ToXml("\t\t"));

			foreach (var rawItemEntry in _rawItemEntries)
				sb.AppendLine("\t\t" + rawItemEntry);

			sb.AppendLine("\t</ItemGroup>");
		}

		sb.AppendLine("</Project>");
		return sb.ToString();
	}

	readonly record struct ItemDefinition(
		string ItemType,
		string Include,
		IReadOnlyDictionary<string, string>? Metadata
	)
	{
		public string ToXml(string indent)
		{
			var encodedInclude = System.Security.SecurityElement.Escape(Include) ?? string.Empty;
			if (Metadata is null || Metadata.Count == 0)
				return indent + "<" + ItemType + " Include=\"" + encodedInclude + "\" />";

			var sb = new StringBuilder();
			sb.AppendLine(indent + "<" + ItemType + " Include=\"" + encodedInclude + "\">");
			foreach (var (name, value) in Metadata)
			{
				var encodedValue = System.Security.SecurityElement.Escape(value) ?? string.Empty;
				sb.AppendLine(indent + "\t<" + name + ">" + encodedValue + "</" + name + ">");
			}

			sb.Append(indent + "</" + ItemType + ">");
			return sb.ToString();
		}
	}
}
