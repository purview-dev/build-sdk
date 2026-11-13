using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Purview.BuildSdk.PropertySchema;

/// <summary>A property assignment in a shipped <c>.props</c>/<c>.targets</c> file.</summary>
sealed record SdkPropertyDefinition(string Name, string File, int Line, bool InTarget, string Value);

/// <summary>A literal <c>CompilerVisibleProperty</c> item in a shipped file.</summary>
sealed record SdkCompilerVisibleProperty(string Name, string File, int Line);

/// <summary>
/// A static read of every <c>.props</c>/<c>.targets</c> file under <c>Sdk/</c>: what is defined, what
/// is compiler-visible, and what is read while <c>Sdk.props</c> is evaluated.
/// </summary>
/// <remarks>
/// Static on purpose. Evaluation only reports the properties a given project happens to reach, so a
/// property defined under a condition the reference project does not meet would be invisible to it.
/// The XML is the complete list.
/// </remarks>
sealed partial class SdkPropertyInventory
{
	[GeneratedRegex(@"\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)")]
	private static partial Regex PropertyReferencePattern();

	SdkPropertyInventory(
		string sdkDirectory,
		IReadOnlyList<SdkPropertyDefinition> definitions,
		IReadOnlyList<SdkCompilerVisibleProperty> compilerVisibleProperties,
		IReadOnlyDictionary<string, string> propsReads,
		IReadOnlySet<string> references
	)
	{
		SdkDirectory = sdkDirectory;
		Definitions = definitions;
		CompilerVisibleProperties = compilerVisibleProperties;
		PropsReads = propsReads;
		References = references;
	}

	public string SdkDirectory { get; }

	public IReadOnlyList<SdkPropertyDefinition> Definitions { get; }

	public IReadOnlyList<SdkCompilerVisibleProperty> CompilerVisibleProperties { get; }

	/// <summary>
	/// Properties read during the <em>property</em> pass of <c>Sdk.props</c> (and the files it imports),
	/// with the first <c>file:line</c> that reads each one. Those are the properties a project file sets
	/// too late. A property's own <c>'$(X)' == ''</c> default guard is not a read.
	/// </summary>
	/// <remarks>
	/// A read inside an <c>ItemGroup</c> or <c>ItemDefinitionGroup</c> - an item's <c>Include</c>, its
	/// metadata, or the group's <c>Condition</c> - is not one: MSBuild evaluates every property, the
	/// project body's included, before it evaluates a single item, so such a read sees the project's
	/// value. Counting it made the schema tell people to set <c>SourceLinkPackageName</c> before the
	/// import when a project file works.
	/// </remarks>
	public IReadOnlyDictionary<string, string> PropsReads { get; }

	/// <summary>Every property referenced as <c>$(Name)</c> anywhere in a shipped file.</summary>
	public IReadOnlySet<string> References { get; }

	/// <summary>Public property names: defined in a shipped file, and not <c>_</c>-prefixed.</summary>
	public IReadOnlySet<string> PublicDefinedNames =>
		Definitions.Select(d => d.Name).Where(n => n[0] != '_').ToHashSet(StringComparer.Ordinal);

	public SdkPropertyDefinition? FirstDefinition(string name) =>
		Definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));

	public bool IsDefinedOrRead(string name) =>
		Definitions.Any(d => string.Equals(d.Name, name, StringComparison.Ordinal)) || References.Contains(name);

	public string Relative(string path) =>
		Path.GetRelativePath(SdkDirectory, path).Replace(Path.DirectorySeparatorChar, '/');

	public static SdkPropertyInventory Load(string sdkDirectory)
	{
		var files = Directory
			.EnumerateFiles(sdkDirectory, "*.*", SearchOption.AllDirectories)
			.Where(f =>
				f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal)
			)
			.Order(StringComparer.Ordinal)
			.ToArray();

		var propsChain = ResolvePropsChain(Path.Combine(sdkDirectory, "Sdk.props"));

		List<SdkPropertyDefinition> definitions = [];
		List<SdkCompilerVisibleProperty> compilerVisible = [];
		Dictionary<string, string> propsReads = new(StringComparer.Ordinal);
		HashSet<string> references = new(StringComparer.Ordinal);

		foreach (var file in files)
		{
			var relative = Path.GetRelativePath(sdkDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
			var document = XDocument.Load(file, LoadOptions.SetLineInfo);
			var inPropsChain = propsChain.Contains(Path.GetFullPath(file));

			foreach (var element in document.Descendants())
			{
				var line = ((IXmlLineInfo)element).LineNumber;
				var parent = element.Parent?.Name.LocalName;
				var inPropertyPass = !element
					.AncestorsAndSelf()
					.Any(a => a.Name.LocalName is "ItemGroup" or "ItemDefinitionGroup" or "Target");

				if (parent == "PropertyGroup")
				{
					var inTarget = element.Ancestors().Any(a => a.Name.LocalName == "Target");
					definitions.Add(new(element.Name.LocalName, relative, line, inTarget, element.Value.Trim()));
				}
				else if (
					parent == "ItemGroup"
					&& element.Name.LocalName == "CompilerVisibleProperty"
					&& element.Attribute("Include")?.Value is { } include
					&& !include.StartsWith("@(", StringComparison.Ordinal)
				)
				{
					compilerVisible.Add(new(include, relative, line));
				}

				foreach (var text in ReferencingText(element))
				{
					foreach (Match match in PropertyReferencePattern().Matches(text))
					{
						var name = match.Groups["name"].Value;
						references.Add(name);

						if (inPropsChain && inPropertyPass && parent != null && !IsOwnDefaultGuard(element, name, text))
							propsReads.TryAdd(name, $"{relative}:{line}");
					}
				}
			}
		}

		return new(sdkDirectory, definitions, compilerVisible, propsReads, references);
	}

	/// <summary>The element's own attributes and its direct text, so a nested element is read once.</summary>
	static IEnumerable<string> ReferencingText(XElement element)
	{
		foreach (var attribute in element.Attributes())
			yield return attribute.Value;

		foreach (var node in element.Nodes())
		{
			if (node is XText text)
				yield return text.Value;
		}
	}

	/// <summary>
	/// <c>&lt;X Condition="'$(X)' == ''"&gt;</c> reads <c>X</c> only to avoid overwriting it, which is
	/// what makes a value set later still win. It is not a read that captures the value.
	/// </summary>
	static bool IsOwnDefaultGuard(XElement element, string name, string text) =>
		element.Parent?.Name.LocalName == "PropertyGroup"
		&& string.Equals(element.Name.LocalName, name, StringComparison.Ordinal)
		&& element.Attribute("Condition")?.Value == text
		&& Regex.Count(text, $@"\$\({Regex.Escape(name)}\)") == 1
		&& Regex.IsMatch(text, $@"'\$\({Regex.Escape(name)}\)'\s*==\s*''");

	/// <summary><c>Sdk.props</c> and every file it imports, transitively, by full path.</summary>
	static HashSet<string> ResolvePropsChain(string sdkProps)
	{
		HashSet<string> chain = new(StringComparer.OrdinalIgnoreCase);
		Stack<string> pending = new([Path.GetFullPath(sdkProps)]);

		while (pending.TryPop(out var file))
		{
			if (!chain.Add(file) || !File.Exists(file))
				continue;

			var directory = Path.GetDirectoryName(file)!;
			foreach (var import in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "Import"))
			{
				var project = import.Attribute("Project")?.Value.Trim();
				if (
					string.IsNullOrEmpty(project)
					|| !project.StartsWith("$(MSBuildThisFileDirectory)", StringComparison.Ordinal)
				)
					continue;

				var relative = project["$(MSBuildThisFileDirectory)".Length..];
				pending.Push(Path.GetFullPath(Path.Combine(directory, relative)));
			}
		}

		return chain;
	}
}
