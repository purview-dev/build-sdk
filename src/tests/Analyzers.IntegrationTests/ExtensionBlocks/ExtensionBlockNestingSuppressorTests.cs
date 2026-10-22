using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.ExtensionBlocks;

/// <summary>
/// Integration tests for <see cref="ExtensionBlockNestingSuppressor"/> behaviour.
/// </summary>
/// <remarks>
/// CA1034 is reported by a stand-in analyzer rather than by the real CodeQuality rule, matching
/// <c>UnionEqualitySuppressorTests</c>. A suppressor only ever sees a diagnostic id and a location, so the
/// rule's own implementation is not what is under test — which declarations the suppression lands on is. The
/// stand-in reports on every externally visible nested type at that type's own source location, which is
/// what the real rule does and why an extension block is caught by it at all: the block is compiled into a
/// nested type, and the report lands on the <c>extension</c> keyword naming nothing.
/// </remarks>
[Category("Integration")]
sealed class ExtensionBlockNestingSuppressorTests
{
	const string ExtensionBlockSource = """
		namespace Sample
		{
			public static class StringExtensions
			{
				extension(string value)
				{
					public bool IsBlank => string.IsNullOrWhiteSpace(value);
				}

				public class Nested
				{
					public int Value;
				}
			}
		}
		""";

	static async Task<Diagnostic[]> AnalyzeAsync(string source, CancellationToken cancellationToken)
	{
		var compilation = AnalyzerTestInfrastructure.CreateCompilation(
			source,
			@"C:\FakeProject\Extensions.cs",
			LanguageVersion.Preview
		);

		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
			new FakeCa1034Analyzer(),
			new ExtensionBlockNestingSuppressor()
		);

		// reportSuppressedDiagnostics keeps a suppressed diagnostic in the results, carrying IsSuppressed.
		// Without it a suppressed diagnostic is simply filtered out, and "suppressed" would be
		// indistinguishable from "the stand-in analyzer never reported it" - so a broken stand-in would make
		// every suppression test pass for the wrong reason.
		CompilationWithAnalyzersOptions analysisOptions = new(
			AnalyzerTestInfrastructure.CreateAnalyzerOptions(),
			onAnalyzerException: null,
			concurrentAnalysis: false,
			logAnalyzerExecutionTime: false,
			reportSuppressedDiagnostics: true
		);

		var diagnostics = await compilation
			.WithAnalyzers(analyzers, analysisOptions)
			.GetAllDiagnosticsAsync(cancellationToken);

		return [.. diagnostics.Where(diagnostic => diagnostic.Id == "CA1034")];
	}

	static async Task AssertSuppressedAsync(Diagnostic[] diagnostics, string target, bool expected)
	{
		var diagnostic = diagnostics.SingleOrDefault(d => TargetName(d) == target);

		await Assert.That(diagnostic).IsNotNull();
		await Assert.That(diagnostic!.IsSuppressed).IsEqualTo(expected);
	}

	[Test]
	public async Task Suppressor_GivenAnExtensionBlock_SuppressesCa1034(CancellationToken cancellationToken)
	{
		var diagnostics = await AnalyzeAsync(ExtensionBlockSource, cancellationToken);

		await AssertSuppressedAsync(diagnostics, ExtensionTarget, expected: true);
	}

	/// <summary>
	/// An ordinary nested type keeps the warning, even when it shares a static class with an extension block.
	/// This is the assertion that stops the suppression widening into "every type in an extension holder".
	/// </summary>
	[Test]
	public async Task Suppressor_GivenAnOrdinaryTypeNestedBesideAnExtensionBlock_DoesNotSuppressCa1034(
		CancellationToken cancellationToken
	)
	{
		var diagnostics = await AnalyzeAsync(ExtensionBlockSource, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "Nested", expected: false);
	}

	/// <summary>
	/// A type nested inside an extension block's own body is still an ordinary nested type.
	/// </summary>
	[Test]
	public async Task Suppressor_GivenATypeNestedInAnExtensionBlock_SuppressesOnlyTheBlock(
		CancellationToken cancellationToken
	)
	{
		const string source = """
			namespace Sample
			{
				public static class StringExtensions
				{
					extension(string value)
					{
						public bool IsBlank => string.IsNullOrWhiteSpace(value);

						public class Inner
						{
							public int Value;
						}
					}
				}
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);

		await AssertSuppressedAsync(diagnostics, ExtensionTarget, expected: true);
		await AssertSuppressedAsync(diagnostics, "Inner", expected: false);
	}

	[Test]
	public async Task Suppressor_GivenAnOrdinaryNestedType_DoesNotSuppressCa1034(CancellationToken cancellationToken)
	{
		const string source = """
			namespace Sample
			{
				public class Outer
				{
					public class Nested
					{
						public int Value;
					}
				}
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "Nested", expected: false);
	}

	/// <summary>
	/// The name the stand-in reports for the extension block's generated type, which has no source name.
	/// </summary>
	const string ExtensionTarget = "<extension>";

	static string TargetName(Diagnostic diagnostic) => diagnostic.Properties["TypeName"]!;

	/// <summary>
	/// Reports CA1034 on every externally visible nested type, the way the real rule does, carrying the type
	/// name so a test can tell the reports apart.
	/// </summary>
	[SuppressMessage(
		"MicrosoftCodeAnalysisDesign",
		"RS1029:Do not use reserved diagnostic IDs",
		Justification = "The id is deliberately the real CA1034, because that is the id the suppressor under test declares as suppressible. This analyzer is a test stand-in and never ships."
	)]
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	sealed class FakeCa1034Analyzer : DiagnosticAnalyzer
	{
		static readonly DiagnosticDescriptor Rule = new(
			"CA1034",
			"Nested types should not be visible",
			"Do not nest type {0}",
			"Design",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true
		);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
		}

		static void AnalyzeType(SymbolAnalysisContext context)
		{
			if (
				context.Symbol is not INamedTypeSymbol { ContainingType: not null } type
				|| type.Locations.FirstOrDefault(location => location.IsInSource) is not { } location
			)
			{
				return;
			}

			var name = string.IsNullOrEmpty(type.Name) ? ExtensionTarget : type.Name;

			context.ReportDiagnostic(
				Diagnostic.Create(
					Rule,
					location,
					ImmutableDictionary<string, string?>.Empty.Add("TypeName", name),
					name
				)
			);
		}
	}
}
