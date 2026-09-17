using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers;

/// <summary>
/// Shared Roslyn test infrastructure for analyzer/suppressor/code-fix integration tests.
/// </summary>
static class AnalyzerTestInfrastructure
{
	static readonly string[] TrustedAssemblies = (
		(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? ""
	).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

	public static ImmutableArray<MetadataReference> BuildBclReferences() =>
		[.. TrustedAssemblies.Select(p => MetadataReference.CreateFromFile(p))];

	public static CSharpCompilation CreateCompilation(string source, string filePath)
	{
		var syntaxTree = CSharpSyntaxTree.ParseText(source, path: NormalizeFakePath(filePath));
		return CSharpCompilation.Create(
			"TestAssembly",
			[syntaxTree],
			BuildBclReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
		);
	}

	public static AnalyzerOptions CreateAnalyzerOptions(
		string projectDir = @"C:\FakeProject\",
		string rootNamespace = "An.Example.Project"
	)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["build_property.ProjectDir"] = NormalizeFakePath(projectDir),
			["build_property.RootNamespace"] = rootNamespace,
		};

		InMemoryAnalyzerConfigOptions options = new(values);
		InMemoryAnalyzerConfigOptionsProvider provider = new(options);
		return new AnalyzerOptions([], provider);
	}

	/// <summary>
	/// Converts a Windows-style fake path literal (e.g. <c>C:\FakeProject\Extensions\System\Foo.cs</c>)
	/// into an absolute path using the current platform's directory separators. The
	/// <c>ExtensionsNamespaceHelper</c> performs relative-path math with platform path APIs,
	/// so tests must feed it native-format paths on every OS.
	/// </summary>
	public static string NormalizeFakePath(string windowsPath)
	{
		var slashed = windowsPath.Replace('\\', '/');

		if (slashed.Length >= 2 && slashed[1] == ':')
			slashed = slashed[2..];

		return Path.Combine(Path.GetTempPath(), slashed.TrimStart('/'));
	}

	sealed class InMemoryAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}

	sealed class InMemoryAnalyzerConfigOptionsProvider(AnalyzerConfigOptions options) : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions => options;

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => options;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => options;
	}
}
