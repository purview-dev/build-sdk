using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Purview.BuildSdk.Analyzers.ExtensionsNamespace;

namespace Purview.BuildSdk.Analyzers.IntegrationTests.Extensions;

/// <summary>
/// Integration tests for <see cref="ExtensionsNamespaceSuppressor"/> behavior.
/// </summary>
[Category("Integration")]
public sealed class ExtensionsNamespaceSuppressorTests
{
	static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
		string filePath,
		string source,
		CancellationToken cancellationToken
	)
	{
		var compilation = AnalyzerTestInfrastructure.CreateCompilation(source, filePath);

		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
			new FakeIde0130Analyzer(),
			new ExtensionsNamespaceSuppressor()
		);
		var analysisOptions = AnalyzerTestInfrastructure.CreateAnalyzerOptions();

		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, analysisOptions);
		return await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken);
	}

	[Test]
	public async Task Suppressor_SuppressesIde0130_ForFileUnderExtensionsRoot(CancellationToken cancellationToken)
	{
		const string source = """
			namespace System
			{
				public static class StringExtensions { }
			}
			""";

		var diagnostics = await AnalyzeAsync(
			@"C:\FakeProject\Extensions\System\StringExtensions.cs",
			source,
			cancellationToken
		);

		Diagnostic[] ide0130Diagnostics = [.. diagnostics.Where(d => d.Id == "IDE0130")];
		await Assert.That(ide0130Diagnostics.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Suppressor_DoesNotSuppressIde0130_ForFileOutsideExtensionsRoot(
		CancellationToken cancellationToken
	)
	{
		const string source = """
			namespace Wrong.Namespace
			{
				public class MyService { }
			}
			""";

		var diagnostics = await AnalyzeAsync(@"C:\FakeProject\Services\MyService.cs", source, cancellationToken);

		Diagnostic[] ide0130Diagnostics = [.. diagnostics.Where(d => d.Id == "IDE0130")];
		await Assert.That(ide0130Diagnostics).HasSingleItem();
		await Assert.That(ide0130Diagnostics[0].IsSuppressed).IsFalse();
	}

	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	sealed class FakeIde0130Analyzer : DiagnosticAnalyzer
	{
		static readonly DiagnosticDescriptor Rule = new(
			"IDE0130",
			"Namespace does not match folder structure",
			"Namespace does not match folder structure",
			"Style",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true
		);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterSyntaxNodeAction(
				AnalyzeNamespace,
				Microsoft.CodeAnalysis.CSharp.SyntaxKind.NamespaceDeclaration
			);
		}

		static void AnalyzeNamespace(SyntaxNodeAnalysisContext context)
		{
			if (
				context.Node is not Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax namespaceDeclaration
			)
			{
				return;
			}

			context.ReportDiagnostic(Diagnostic.Create(Rule, namespaceDeclaration.Name.GetLocation()));
		}
	}
}
