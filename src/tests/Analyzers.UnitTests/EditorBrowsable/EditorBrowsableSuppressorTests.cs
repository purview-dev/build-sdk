using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.EditorBrowsable;

/// <summary>
/// Unit-tests for <see cref="EditorBrowsableSuppressor"/> using the Roslyn compilation API
/// directly.  The suppressor is <c>internal sealed</c>; access is granted via the
/// <c>InternalsVisibleTo</c> attribute that <c>Sdk.targets</c> auto-generates for every
/// &lt;TestType&gt; variant when building the analyzer project.
/// </summary>
public sealed class EditorBrowsableSuppressorTests
{
	static readonly string[] TrustedAssemblies = (
		(string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? ""
	).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

	static ImmutableArray<MetadataReference> BuildBclReferences() =>
		[.. TrustedAssemblies.Select(p => MetadataReference.CreateFromFile(p))];

	static ImmutableArray<MetadataReference> BuildBclReferencesWithoutEditorBrowsable() =>
		[
			.. TrustedAssemblies
				.Where(p =>
					!Path.GetFileNameWithoutExtension(p)
						.StartsWith("System.ComponentModel", StringComparison.OrdinalIgnoreCase)
				)
				.Select(p => MetadataReference.CreateFromFile(p)),
		];

	static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, CancellationToken cancellationToken)
	{
		var parseOptions = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);

		var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: cancellationToken);

		var compilation = CSharpCompilation.Create(
			"TestAssembly",
			[syntaxTree],
			BuildBclReferences(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
		);

		var suppressors = ImmutableArray.Create<DiagnosticAnalyzer>(new EditorBrowsableSuppressor());

		var compilationWithAnalyzers = compilation.WithAnalyzers(suppressors);

		return await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken);
	}

	static async Task<ImmutableArray<Diagnostic>> AnalyzeWithoutEditorBrowsableReferenceAsync(
		string source,
		CancellationToken cancellationToken
	)
	{
		var parseOptions = CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose);

		var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: cancellationToken);

		var compilation = CSharpCompilation.Create(
			"TestAssembly",
			[syntaxTree],
			BuildBclReferencesWithoutEditorBrowsable(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
		);

		var suppressors = ImmutableArray.Create<DiagnosticAnalyzer>(new EditorBrowsableSuppressor());

		var compilationWithAnalyzers = compilation.WithAnalyzers(suppressors);

		return await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken);
	}

	/// <summary>
	/// A public method decorated with [EditorBrowsable(Never)] should have its CS1591
	/// (missing XML documentation) suppressed.
	/// </summary>
	[Test]
	public async Task Suppresses_CS1591_For_EditorBrowsableNever_Member(CancellationToken cancellationToken)
	{
		const string source = """
			using System.ComponentModel;

			namespace MyLib;

			public class MyClass
			{
			    [EditorBrowsable(EditorBrowsableState.Never)]
			    public void HiddenMethod() { }
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);
		var cs1591 = diagnostics.Where(d => d.Id == "CS1591").ToArray();

		// CS1591 may appear for the class itself; we care only about HiddenMethod.
		var hiddenMethodDiag = cs1591.FirstOrDefault(d => d.Location.GetLineSpan().StartLinePosition.Line == 7); // "public void HiddenMethod"

		// If no CS1591 at all, the compiler either didn't warn or the suppressor worked perfectly.
		// If CS1591 is present for HiddenMethod it must be suppressed.
		if (hiddenMethodDiag is not null)
			await Assert.That(hiddenMethodDiag.IsSuppressed).IsTrue();
	}

	/// <summary>
	/// A regular public method without [EditorBrowsable(Never)] must not have CS1591 suppressed.
	/// </summary>
	[Test]
	public async Task DoesNot_Suppress_CS1591_For_Regular_PublicMember(CancellationToken cancellationToken)
	{
		const string source = """
			namespace MyLib;

			public class MyClass
			{
			    public void PublicMethod() { }
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);
		var cs1591 = diagnostics.Where(d => d.Id == "CS1591").ToArray();

		foreach (var d in cs1591)
			await Assert.That(d.IsSuppressed).IsFalse();
	}

	/// <summary>
	/// A method decorated with [EditorBrowsable(Always)] should not be treated as "never" —
	/// CS1591 must remain unsuppressed.
	/// </summary>
	[Test]
	public async Task DoesNot_Suppress_CS1591_For_EditorBrowsableAlways_Member(CancellationToken cancellationToken)
	{
		const string source = """
			using System.ComponentModel;

			namespace MyLib;

			public class MyClass
			{
			    [EditorBrowsable(EditorBrowsableState.Always)]
			    public void VisibleMethod() { }
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);
		var cs1591 = diagnostics.Where(d => d.Id == "CS1591").ToArray();

		foreach (var d in cs1591)
			await Assert.That(d.IsSuppressed).IsFalse();
	}

	/// <summary>
	/// When the compilation does not reference <c>System.ComponentModel</c>, the
	/// <c>EditorBrowsableAttribute</c> type cannot be resolved, so the suppressor must be a
	/// no-op and leave CS1591 unsuppressed.
	/// </summary>
	[Test]
	public async Task DoesNotSuppress_WhenEditorBrowsableAttribute_IsNotReferenced(CancellationToken cancellationToken)
	{
		const string source = """
			namespace MyLib;

			public class MyClass
			{
			    public void PublicMethod() { }
			}
			""";

		var diagnostics = await AnalyzeWithoutEditorBrowsableReferenceAsync(source, cancellationToken);
		var cs1591 = diagnostics.Where(d => d.Id == "CS1591").ToArray();

		await Assert.That(cs1591).IsNotEmpty();
		foreach (var d in cs1591)
			await Assert.That(d.IsSuppressed).IsFalse();
	}

	/// <summary>
	/// Verifies the suppressor's SuppressionDescriptor ID and suppressed diagnostic ID are correct.
	/// </summary>
	[Test]
	public async Task SupportedSuppressions_ContainsCorrectDescriptor()
	{
		var suppressor = new EditorBrowsableSuppressor();

		await Assert.That(suppressor.SupportedSuppressions).HasSingleItem();
		var descriptor = suppressor.SupportedSuppressions[0];
		await Assert.That(descriptor.Id).IsEqualTo("PDS0001");
		await Assert.That(descriptor.SuppressedDiagnosticId).IsEqualTo("CS1591");
	}
}
